using System.Threading.Channels;
using Microsoft.Win32;

namespace SoundpadTranslator;

sealed class MainForm : Form
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "SoundpadTranslator";

    readonly AppConfig config;
    readonly KeyboardInterceptor interceptor;
    readonly SoundpadClient soundpad = new();
    readonly Channel<Binding> playQueue = Channel.CreateUnbounded<Binding>(new UnboundedChannelOptions { SingleReader = true });

    // Read from the interception thread; replaced wholesale, never mutated.
    volatile IReadOnlyDictionary<int, Binding> bindingsByKey = new Dictionary<int, Binding>();
    volatile IReadOnlyList<SoundInfo> sounds = [];
    volatile bool capturingKey;

    readonly Label keyboardLabel = new() { AutoSize = true };
    readonly Label soundpadLabel = new() { AutoSize = true };
    readonly Label hotkeyWarning = new()
    {
        Dock = DockStyle.Top,
        AutoSize = false,
        Height = 44,
        Padding = new Padding(8, 4, 8, 4),
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Color.FromArgb(255, 243, 205),
        ForeColor = Color.FromArgb(102, 77, 3),
        BorderStyle = BorderStyle.FixedSingle,
    };
    readonly Label statusLabel = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(6), BorderStyle = BorderStyle.Fixed3D };
    readonly Button learnButton = new() { AutoSize = true };
    readonly Button refreshButton = new() { AutoSize = true };
    readonly Button addButton = new() { AutoSize = true };
    readonly Button editButton = new() { AutoSize = true };
    readonly Button removeButton = new() { AutoSize = true };
    readonly CheckBox enabledBox = new() { AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
    readonly CheckBox autostartBox = new() { AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
    readonly Label languageLabel = new() { AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
    readonly ComboBox languageBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(3, 4, 3, 3) };
    readonly ToolStripMenuItem trayOpenItem = new();
    readonly ToolStripMenuItem trayToggleItem = new() { CheckOnClick = true };
    readonly ToolStripMenuItem trayStopItem = new();
    readonly ToolStripMenuItem trayExitItem = new();
    readonly ListView list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
    };
    readonly NotifyIcon tray = new() { Text = "Soundpad Translator", Icon = SystemIcons.Application, Visible = true };

    bool exiting;
    bool trayHintShown;
    bool? soundpadConnected; // null until the first refresh finishes

    public MainForm(AppConfig config, KeyboardInterceptor interceptor, bool startHidden)
    {
        this.config = config;
        this.interceptor = interceptor;

        Text = "Soundpad Translator";
        Icon = SystemIcons.Application;
        Size = new Size(640, 520);
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        BuildTray();
        ApplyTexts();

        interceptor.Target = config.Target;
        interceptor.Enabled = config.Enabled;
        interceptor.SoundboardKeyDown += OnSoundboardKey;
        interceptor.DeviceMoved += t => BeginInvoke(() => { config.KeyboardDevice = t.Device; config.Save(); });

        enabledBox.Checked = config.Enabled;
        autostartBox.Checked = IsAutostartEnabled();
        _ = Task.Run(PlayLoop);
        _ = RefreshSoundsAsync();

        if (startHidden)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Load += (_, _) => { Hide(); WindowState = FormWindowState.Normal; ShowInTaskbar = true; };
        }
        else if (config.KeyboardHardwareId == null)
        {
            // First run: go straight to picking the soundboard keyboard.
            Shown += (_, _) => BeginInvoke(StartLearning);
        }
    }

    void BuildLayout()
    {
        list.Columns.Add("", 150);
        list.Columns.Add("", 420);
        foreach (var language in Enum.GetValues<AppLanguage>())
            languageBox.Items.Add(DisplayName(language));
        languageBox.SelectedIndex = (int)Lang.Current;

        var keyboardRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 0), WrapContents = false };
        keyboardRow.Controls.AddRange([learnButton, keyboardLabel]);
        keyboardLabel.Margin = new Padding(6, 8, 3, 3);

        var soundpadRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 0, 6, 0), WrapContents = false };
        soundpadRow.Controls.AddRange([refreshButton, soundpadLabel]);
        soundpadLabel.Margin = new Padding(6, 8, 3, 3);

        var bindingRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        bindingRow.Controls.AddRange([addButton, editButton, removeButton, enabledBox, autostartBox, languageLabel, languageBox]);

        Controls.Add(list);
        Controls.Add(bindingRow);
        Controls.Add(soundpadRow);
        Controls.Add(keyboardRow);
        Controls.Add(hotkeyWarning);
        Controls.Add(statusLabel);

        learnButton.Click += (_, _) => StartLearning();
        refreshButton.Click += async (_, _) => await RefreshSoundsAsync();
        addButton.Click += (_, _) => ToggleCapture();
        editButton.Click += (_, _) => EditSelected();
        removeButton.Click += (_, _) => RemoveSelected();
        list.DoubleClick += (_, _) => EditSelected();
        list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
        enabledBox.CheckedChanged += (_, _) =>
        {
            config.Enabled = interceptor.Enabled = enabledBox.Checked;
            config.Save();
            SetStatus(enabledBox.Checked
                ? T("Soundboard zapnutý.", "Soundboard on.")
                : T("Soundboard vypnutý - druhá klávesnice píše normálně.", "Soundboard off - the second keyboard types normally."));
        };
        autostartBox.CheckedChanged += (_, _) => SetAutostart(autostartBox.Checked);
        languageBox.SelectedIndexChanged += (_, _) =>
        {
            var language = (AppLanguage)languageBox.SelectedIndex;
            if (language == Lang.Current)
                return;
            Lang.Current = language;
            config.Language = language;
            config.Save();
            ApplyTexts();
            SetStatus(T("Jazyk změněn.", "Language changed."));
        };
    }

    void BuildTray()
    {
        var menu = new ContextMenuStrip();
        trayOpenItem.Click += (_, _) => ShowFromTray();
        trayToggleItem.Click += (_, _) => enabledBox.Checked = trayToggleItem.Checked;
        trayStopItem.Click += (_, _) => playQueue.Writer.TryWrite(new Binding { Action = BindingAction.Stop });
        trayExitItem.Click += (_, _) => { exiting = true; Close(); };
        menu.Items.AddRange([trayOpenItem, trayToggleItem, trayStopItem, new ToolStripSeparator(), trayExitItem]);
        menu.Opening += (_, _) => trayToggleItem.Checked = enabledBox.Checked;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowFromTray();
    }

    /// <summary>Sets every text in the window and tray to the current language.</summary>
    void ApplyTexts()
    {
        hotkeyWarning.Text = T(
            "Pro správnou funkci smaž v Soundpadu všechny klávesové zkratky (hotkeys): "
            + "pravý klik na zvuk -> Remove hotkey. Jinak můžou kolidovat s klávesami soundboardu.",
            "For this to work, remove all hotkeys in Soundpad: "
            + "right-click a sound -> Remove hotkey. Otherwise they can clash with the soundboard keys.");
        learnButton.Text = T("Zvolit klávesnici...", "Choose keyboard...");
        refreshButton.Text = T("Načíst zvuky", "Load sounds");
        addButton.Text = capturingKey ? T("Zrušit", "Cancel") : T("Přidat klávesu...", "Add key...");
        editButton.Text = T("Změnit zvuk...", "Change sound...");
        removeButton.Text = T("Odebrat", "Remove");
        enabledBox.Text = trayToggleItem.Text = T("Soundboard zapnutý", "Soundboard on");
        autostartBox.Text = T("Spouštět s Windows", "Start with Windows");
        languageLabel.Text = T("Jazyk:", "Language:");
        list.Columns[0].Text = T("Klávesa", "Key");
        list.Columns[1].Text = T("Akce", "Action");
        trayOpenItem.Text = T("Otevřít", "Open");
        trayStopItem.Text = T("Zastavit přehrávání", "Stop playback");
        trayExitItem.Text = T("Ukončit", "Exit");
        RebuildBindings();
        UpdateKeyboardLabel();
        UpdateSoundpadLabel();
    }

    void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(3000, "Soundpad Translator", T("Běží dál v oznamovací oblasti. Ukončíš ho přes pravý klik na ikonu.",
                    "Still running in the notification area. Right-click the icon to exit."), ToolTipIcon.Info);
            }
            return;
        }
        playQueue.Writer.TryComplete();
        tray.Visible = false;
        base.OnFormClosing(e);
    }

    // --- Keyboard selection -------------------------------------------------------------------

    void StartLearning()
    {
        CancelCapture();
        using var picker = new KeyboardPickerForm(interceptor, config.Target);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Result is not { } target)
            return;

        config.KeyboardHardwareId = target.HardwareId;
        config.KeyboardDevice = target.Device;
        config.Save();
        interceptor.Target = target;
        UpdateKeyboardLabel();
        SetStatus(T("Klávesnice nastavena. Teď přidej klávesy přes \"Přidat klávesu...\".", "Keyboard set. Now add keys with \"Add key...\"."));
    }

    void UpdateKeyboardLabel()
    {
        keyboardLabel.Text = config.KeyboardHardwareId == null
            ? T("Soundboard klávesnice není nastavená.", "No soundboard keyboard selected.")
            : $"Soundboard: {KeyboardPickerForm.Describe(config.KeyboardHardwareId)}";
    }

    // --- Bindings -------------------------------------------------------------------------------

    void ToggleCapture()
    {
        if (capturingKey)
        {
            CancelCapture();
            SetStatus(T("Zrušeno.", "Cancelled."));
            return;
        }
        if (config.KeyboardHardwareId == null)
        {
            MessageBox.Show(this, T("Nejdřív zvol soundboard klávesnici.", "Choose the soundboard keyboard first."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!interceptor.Enabled)
            enabledBox.Checked = true;
        interceptor.CancelLearning();
        capturingKey = true;
        addButton.Text = T("Zrušit", "Cancel");
        SetStatus(T("Stiskni klávesu na soundboard klávesnici...", "Press a key on the soundboard keyboard..."));
    }

    void CancelCapture()
    {
        capturingKey = false;
        addButton.Text = T("Přidat klávesu...", "Add key...");
    }

    /// <summary>Runs on the interception thread. Must return immediately.</summary>
    void OnSoundboardKey(int key)
    {
        if (capturingKey)
        {
            capturingKey = false;
            BeginInvoke(() => { CancelCapture(); PickSoundFor(key); });
            return;
        }
        if (bindingsByKey.TryGetValue(key, out var binding))
            playQueue.Writer.TryWrite(binding);
        else
            BeginInvoke(() => SetStatus(T($"Klávesa {KeyNames.Describe(key)} nemá přiřazený zvuk.", $"Key {KeyNames.Describe(key)} has no sound assigned.")));
    }

    void PickSoundFor(int key)
    {
        ShowFromTray();
        using var picker = new SoundPickerForm(KeyNames.Describe(key), sounds);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Result == null)
        {
            SetStatus(T("Zrušeno.", "Cancelled."));
            return;
        }
        var binding = picker.Result;
        binding.Key = key;
        config.Bindings.RemoveAll(b => b.Key == key);
        config.Bindings.Add(binding);
        config.Save();
        RebuildBindings(select: binding);
        SetStatus($"{KeyNames.Describe(key)} -> {binding.Describe()}");
    }

    void EditSelected()
    {
        if (list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is Binding binding)
            PickSoundFor(binding.Key);
    }

    void RemoveSelected()
    {
        if (list.SelectedItems.Count != 1 || list.SelectedItems[0].Tag is not Binding binding)
            return;
        config.Bindings.Remove(binding);
        config.Save();
        RebuildBindings();
    }

    void RebuildBindings(Binding? select = null)
    {
        bindingsByKey = config.Bindings.ToDictionary(b => b.Key);
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var b in config.Bindings.OrderBy(b => b.Key))
        {
            var item = new ListViewItem([KeyNames.Describe(b.Key), b.Describe()]) { Tag = b };
            list.Items.Add(item);
            if (b == select)
                item.Selected = true;
        }
        list.EndUpdate();
    }

    // --- Soundpad -------------------------------------------------------------------------------

    async Task RefreshSoundsAsync()
    {
        refreshButton.Enabled = false;
        try
        {
            sounds = await Task.Run(soundpad.GetSounds);
            soundpadConnected = true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            soundpadConnected = false;
        }
        finally
        {
            refreshButton.Enabled = true;
            UpdateSoundpadLabel();
        }
    }

    void UpdateSoundpadLabel()
    {
        soundpadLabel.Text = soundpadConnected switch
        {
            true => T($"Soundpad: připojeno, {sounds.Count} zvuků.", $"Soundpad: connected, {sounds.Count} sounds."),
            false => T("Soundpad: nepřipojeno (běží Soundpad?)", "Soundpad: not connected (is Soundpad running?)"),
            null => "",
        };
    }

    async Task PlayLoop()
    {
        await foreach (var binding in playQueue.Reader.ReadAllAsync())
        {
            try
            {
                switch (binding.Action)
                {
                    case BindingAction.Play:
                        soundpad.PlaySound(binding.ResolveIndex(sounds));
                        break;
                    case BindingAction.Stop:
                        soundpad.StopSound();
                        break;
                    case BindingAction.TogglePause:
                        soundpad.TogglePause();
                        break;
                }
                Post(() => SetStatus(T("Přehrávám: ", "Playing: ") + binding.Describe()));
            }
            catch (Exception ex)
            {
                Post(() => SetStatus(T("Chyba: ", "Error: ") + ex.Message));
            }
        }
    }

    void Post(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(action);
    }

    void SetStatus(string text) => statusLabel.Text = text;

    // --- Autostart ------------------------------------------------------------------------------

    static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --minimized");
        else
            key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            tray.Dispose();
        base.Dispose(disposing);
    }
}
