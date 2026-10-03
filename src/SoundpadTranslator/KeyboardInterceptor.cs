using static SoundpadTranslator.Interception;

namespace SoundpadTranslator;

sealed class InterceptionUnavailableException() : Exception("Interception driver is not installed or not loaded.");

/// <summary>Which physical keyboard is the soundboard. Device numbers can shift after replugging, hardware IDs don't.</summary>
sealed record SoundboardTarget(string HardwareId, int Device);

/// <summary>
/// Owns the Interception context and a high-priority thread that receives every keystroke from every keyboard.
/// Strokes from the soundboard keyboard are swallowed and reported via <see cref="SoundboardKeyDown"/>;
/// everything else is forwarded untouched. Events fire on the interception thread, so handlers must be quick:
/// while this thread is busy, all keyboards are frozen.
/// </summary>
sealed class KeyboardInterceptor : IDisposable
{
    static readonly Predicate IsKeyboardPredicate = d => IsKeyboard(d) ? 1 : 0;
    const long HardwareIdCacheMs = 2000;

    readonly IntPtr context;
    readonly Thread thread;
    volatile bool running = true;
    volatile bool learning;
    volatile SoundboardTarget? target;

    // Only touched on the interception thread.
    readonly HashSet<int> swallowedDown = [];
    readonly string?[] hardwareIds = new string?[MaxKeyboard + 1];
    readonly long[] hardwareIdTimes = new long[MaxKeyboard + 1];

    public volatile bool Enabled = true;

    /// <summary>Key id (see <see cref="KeyNames"/>) pressed on the soundboard keyboard. Auto-repeat is filtered out.</summary>
    public event Action<int>? SoundboardKeyDown;

    /// <summary>Raised after <see cref="StartLearning"/> when any keyboard sends a key down.</summary>
    public event Action<SoundboardTarget>? DeviceLearned;

    /// <summary>The soundboard keyboard was found under a different device number (e.g. after replugging).</summary>
    public event Action<SoundboardTarget>? DeviceMoved;

    KeyboardInterceptor(IntPtr context)
    {
        this.context = context;
        interception_set_filter(context, IsKeyboardPredicate, FilterKeyAll);
        thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "Interception" };
        thread.Start();
    }

    public static KeyboardInterceptor Create()
    {
        var context = interception_create_context();
        if (context == IntPtr.Zero)
            throw new InterceptionUnavailableException();
        return new KeyboardInterceptor(context);
    }

    public SoundboardTarget? Target
    {
        get => target;
        set => target = value;
    }

    public void StartLearning() => learning = true;
    public void CancelLearning() => learning = false;

    void Loop()
    {
        var stroke = new Stroke();
        while (running)
        {
            int device = interception_wait_with_timeout(context, 200);
            if (device == 0)
                continue;
            if (interception_receive(context, device, ref stroke, 1) <= 0)
                continue;

            bool swallow = false;
            try
            {
                swallow = Handle(device, stroke);
            }
            catch
            {
                // A bug here must never eat the user's typing.
            }
            if (!swallow)
                interception_send(context, device, ref stroke, 1);
        }
    }

    bool Handle(int device, Stroke stroke)
    {
        bool down = (stroke.State & KeyUp) == 0;
        int key = KeyNames.ToKeyId(stroke.Code, stroke.State);

        if (learning && down)
        {
            learning = false;
            var id = HardwareId(device, refresh: true);
            if (id != null)
                DeviceLearned?.Invoke(new SoundboardTarget(id, device));
            // Eat the identifying press (and its release) so it doesn't type into or click anything.
            swallowedDown.Add(key);
            return true;
        }

        if (!down)
        {
            // Only swallow a release whose press we swallowed. Otherwise a key held while the soundboard
            // was enabled/learned would stay "stuck down" in Windows.
            return swallowedDown.Remove(key);
        }

        if (!Enabled || !IsSoundboard(device))
            return false;

        if (swallowedDown.Add(key))
            SoundboardKeyDown?.Invoke(key);
        return true;
    }

    bool IsSoundboard(int device)
    {
        var t = target;
        if (t == null || HardwareId(device) != t.HardwareId)
            return false;
        if (device == t.Device)
            return true;
        // Same model as the soundboard. If the configured slot still holds that model, this is a second
        // identical keyboard (the user's main one), not the soundboard.
        if (HardwareId(t.Device) == t.HardwareId)
            return false;
        var moved = t with { Device = device };
        target = moved;
        DeviceMoved?.Invoke(moved);
        return true;
    }

    string? HardwareId(int device, bool refresh = false)
    {
        if (!IsKeyboard(device))
            return null;
        long now = Environment.TickCount64;
        if (refresh || now - hardwareIdTimes[device] > HardwareIdCacheMs)
        {
            hardwareIds[device] = GetHardwareId(context, device);
            hardwareIdTimes[device] = now;
        }
        return hardwareIds[device];
    }

    public void Dispose()
    {
        running = false;
        thread.Join(1000);
        interception_destroy_context(context);
    }
}
