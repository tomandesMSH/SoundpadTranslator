using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundpadTranslator;

enum BindingAction
{
    Play,
    Stop,
    TogglePause,
}

sealed class Binding
{
    public int Key { get; set; }
    public BindingAction Action { get; set; }

    // A sound is remembered by url and title too, because Soundpad indexes shift when the list is reordered.
    public int SoundIndex { get; set; }
    public string? SoundTitle { get; set; }
    public string? SoundUrl { get; set; }

    public string Describe() => Action switch
    {
        BindingAction.Play => SoundTitle ?? $"#{SoundIndex}",
        BindingAction.Stop => T("Zastavit přehrávání", "Stop playback"),
        BindingAction.TogglePause => T("Pauza / pokračovat", "Pause / resume"),
        _ => Action.ToString(),
    };

    /// <summary>Current Soundpad index of the bound sound: by url, then title, then the stored index.</summary>
    public int ResolveIndex(IReadOnlyList<SoundInfo> sounds)
    {
        var match = sounds.FirstOrDefault(s => !string.IsNullOrEmpty(SoundUrl) && string.Equals(s.Url, SoundUrl, StringComparison.OrdinalIgnoreCase))
            ?? sounds.FirstOrDefault(s => !string.IsNullOrEmpty(SoundTitle) && s.Title == SoundTitle);
        return match?.Index ?? SoundIndex;
    }
}

sealed class AppConfig
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundpadTranslator", "config.json");

    public string? KeyboardHardwareId { get; set; }
    public int KeyboardDevice { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Null until the user picks one on first start.</summary>
    public AppLanguage? Language { get; set; }
    public List<Binding> Bindings { get; set; } = [];

    [JsonIgnore]
    public SoundboardTarget? Target => KeyboardHardwareId == null ? null : new SoundboardTarget(KeyboardHardwareId, KeyboardDevice);

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            File.Copy(FilePath, FilePath + ".broken", overwrite: true);
        }
        return new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
