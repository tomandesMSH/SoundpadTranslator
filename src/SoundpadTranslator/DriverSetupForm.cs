using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace SoundpadTranslator;

/// <summary>Shown when the Interception driver can't be opened: installs it (elevated) and offers a restart.</summary>
sealed class DriverSetupForm : Form
{
    const string KeyboardClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E96B-E325-11CE-BFC1-08002BE10318}";

    readonly Label message = new() { Dock = DockStyle.Fill, Padding = new Padding(12), Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10f) };
    readonly Button installButton = new() { Text = T("Nainstalovat ovladač", "Install driver"), AutoSize = true };
    readonly Button restartButton = new() { Text = T("Restartovat počítač", "Restart computer"), AutoSize = true };
    readonly Button closeButton = new() { Text = T("Zavřít", "Close"), AutoSize = true, DialogResult = DialogResult.Cancel };

    static string InstallerPath => Path.Combine(AppContext.BaseDirectory, "install-interception.exe");

    /// <summary>The driver is registered as a keyboard class filter. It only starts working after a reboot.</summary>
    public static bool IsDriverInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyboardClassKey);
        return key?.GetValue("UpperFilters") is string[] filters
            && filters.Contains("keyboard", StringComparer.OrdinalIgnoreCase);
    }

    public DriverSetupForm()
    {
        Text = T("Soundpad Translator - ovladač", "Soundpad Translator - driver");
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(520, 280);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.AddRange([closeButton, restartButton, installButton]);
        Controls.Add(message);
        Controls.Add(buttons);
        CancelButton = closeButton;

        installButton.Click += (_, _) => Install();
        restartButton.Click += (_, _) => Restart();
        UpdateState();
    }

    void UpdateState()
    {
        bool installed = IsDriverInstalled();
        installButton.Visible = !installed;
        restartButton.Visible = installed;
        message.Text = installed
            ? T("Ovladač Interception je nainstalovaný, ale začne fungovat až po restartu počítače.\n\nPo restartu spusť Soundpad Translator znovu.",
                "The Interception driver is installed, but it only starts working after a restart.\n\nRun Soundpad Translator again after restarting.")
            : T("Aby šlo druhou klávesnici použít jako soundboard, potřebuje aplikace ovladač Interception. " +
                "Ten umí rozlišit, ze které klávesnice stisk přišel.\n\n" +
                "Instalace si vyžádá oprávnění správce a potom restart počítače.",
                "To use a second keyboard as a soundboard, the app needs the Interception driver. " +
                "It can tell which keyboard a key press came from.\n\n" +
                "Installing it asks for administrator rights and then a restart.");
    }

    void Install()
    {
        if (!File.Exists(InstallerPath))
        {
            MessageBox.Show(this, T("Instalátor nebyl nalezen:", "Installer not found:") + $"\n{InstallerPath}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        try
        {
            using var process = Process.Start(new ProcessStartInfo(InstallerPath, "/install")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            process?.WaitForExit();
        }
        catch (Win32Exception)
        {
            // UAC prompt declined.
            return;
        }

        if (!IsDriverInstalled())
            MessageBox.Show(this, T("Instalace ovladače se nepovedla.", "Driver installation failed."), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        UpdateState();
    }

    void Restart()
    {
        if (MessageBox.Show(this, T("Restartovat počítač teď? Ulož si rozdělanou práci.", "Restart the computer now? Save your work first."), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false, CreateNoWindow = true });
        Close();
    }
}
