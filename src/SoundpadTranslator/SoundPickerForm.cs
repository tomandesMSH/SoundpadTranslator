namespace SoundpadTranslator;

/// <summary>Picks what a soundboard key should do: play one of Soundpad's sounds, or stop / pause playback.</summary>
sealed class SoundPickerForm : Form
{
    sealed record Choice(string Text, BindingAction Action, SoundInfo? Sound)
    {
        public override string ToString() => Text;
    }

    static Choice[] Specials =>
    [
        new(new Binding { Action = BindingAction.Stop }.Describe(), BindingAction.Stop, null),
        new(new Binding { Action = BindingAction.TogglePause }.Describe(), BindingAction.TogglePause, null),
    ];

    readonly IReadOnlyList<SoundInfo> sounds;
    readonly TextBox filter = new() { Dock = DockStyle.Top, PlaceholderText = T("Hledat...", "Search...") };
    readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public Binding? Result { get; private set; }

    public SoundPickerForm(string keyName, IReadOnlyList<SoundInfo> sounds)
    {
        this.sounds = sounds;
        Text = T($"Klávesa {keyName} - vyber zvuk", $"Key {keyName} - choose a sound");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(460, 520);
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = T("Zrušit", "Cancel"), DialogResult = DialogResult.Cancel, Width = 90 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(4) };
        buttons.Controls.AddRange([cancel, ok]);
        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 36,
            Padding = new Padding(4),
            Text = sounds.Count == 0 ? T("Seznam zvuků je prázdný. Běží Soundpad? (Zavři okno a klikni na \"Načíst zvuky\".)",
                "The sound list is empty. Is Soundpad running? (Close this window and click \"Load sounds\".)") : "",
            ForeColor = SystemColors.GrayText,
        };

        Controls.Add(list);
        Controls.Add(filter);
        Controls.Add(hint);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        filter.TextChanged += (_, _) => Fill();
        filter.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Down && list.Items.Count > 0)
            {
                list.Focus();
                list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1);
                e.Handled = true;
            }
        };
        list.DoubleClick += (_, _) => { if (list.SelectedItem != null) { DialogResult = DialogResult.OK; Close(); } };
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK)
                return;
            if (list.SelectedItem is not Choice choice)
            {
                e.Cancel = true;
                return;
            }
            Result = new Binding
            {
                Action = choice.Action,
                SoundIndex = choice.Sound?.Index ?? 0,
                SoundTitle = choice.Sound?.Title,
                SoundUrl = choice.Sound?.Url,
            };
        };

        Fill();
        Shown += (_, _) => filter.Focus();
    }

    void Fill()
    {
        var query = filter.Text.Trim();
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var special in Specials)
            if (query.Length == 0 || special.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                list.Items.Add(special);
        foreach (var sound in sounds)
            if (query.Length == 0 || sound.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                list.Items.Add(new Choice(sound.ToString(), BindingAction.Play, sound));
        list.EndUpdate();

        // Prefer the first real sound when filtering, the first item otherwise.
        int firstSound = Enumerable.Range(0, list.Items.Count).FirstOrDefault(i => ((Choice)list.Items[i]).Sound != null, -1);
        list.SelectedIndex = query.Length > 0 && firstSound >= 0 ? firstSound : (list.Items.Count > 0 ? 0 : -1);
    }
}
