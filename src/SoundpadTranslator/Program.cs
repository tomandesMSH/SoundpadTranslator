namespace SoundpadTranslator;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\SoundpadTranslator", out bool isFirstInstance);
        if (!isFirstInstance)
            return;

        ApplicationConfiguration.Initialize();

        var config = AppConfig.Load();
        if (config.Language == null)
        {
            using var picker = new LanguagePickerForm();
            if (picker.ShowDialog() == DialogResult.OK && picker.Result is { } language)
            {
                config.Language = language;
                config.Save();
            }
        }
        Lang.Current = config.Language ?? AppLanguage.Czech;

        KeyboardInterceptor interceptor;
        try
        {
            interceptor = KeyboardInterceptor.Create();
        }
        catch (InterceptionUnavailableException)
        {
            Application.Run(new DriverSetupForm());
            return;
        }
        catch (DllNotFoundException ex)
        {
            MessageBox.Show(T("Chybí interception.dll vedle programu.", "interception.dll is missing next to the program.") + $"\n\n{ex.Message}", "Soundpad Translator",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using (interceptor)
        {
            bool startHidden = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
            Application.Run(new MainForm(config, interceptor, startHidden));
        }
    }
}
