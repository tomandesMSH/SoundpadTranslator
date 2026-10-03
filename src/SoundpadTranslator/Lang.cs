global using static SoundpadTranslator.Lang;

namespace SoundpadTranslator;

enum AppLanguage
{
    Czech,
    English,
}

/// <summary>UI language. Texts are written inline as <c>T("česky", "English")</c>.</summary>
static class Lang
{
    public static AppLanguage Current { get; set; }

    public static string T(string czech, string english) => Current == AppLanguage.English ? english : czech;

    /// <summary>Display names, deliberately not translated so each is readable to its own speakers.</summary>
    public static string DisplayName(AppLanguage language) => language switch
    {
        AppLanguage.English => "English",
        _ => "Čeština",
    };
}

/// <summary>Asked once on first start, before anything else is shown.</summary>
sealed class LanguagePickerForm : Form
{
    public AppLanguage? Result { get; private set; }

    public LanguagePickerForm()
    {
        Text = "Soundpad Translator";
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TopMost = true;

        var prompt = new Label
        {
            Text = "Vyber jazyk / Choose language",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 12f, FontStyle.Bold),
            Margin = new Padding(12, 16, 12, 12),
        };
        var buttons = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(8), Margin = new Padding(0) };
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            var button = new Button
            {
                Text = DisplayName(language),
                Size = new Size(140, 40),
                Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 11f),
            };
            button.Click += (_, _) => { Result = language; DialogResult = DialogResult.OK; Close(); };
            buttons.Controls.Add(button);
        }

        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };
        layout.Controls.AddRange([prompt, buttons]);
        Controls.Add(layout);
    }
}
