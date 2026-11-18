using KeyFlowPads.Audio;

namespace KeyFlowPads.App;

internal sealed class SettingsWindow : Form
{
    private static readonly double[] Durations = [0.5, 1, 1.5, 2, 3, 4, 5];
    private readonly ComboBox voice;
    private readonly NumericUpDown semitones;
    private readonly Button reset;
    private readonly Label preview;
    private bool loadingProfile;

    internal SettingsWindow(Theme theme, PlayerPreferences preferences)
    {
        Result = preferences.Copy();
        Text = "KeyFlow Pads · Settings";
        Icon = theme.Icon;
        Font = new Font("Segoe UI", 9);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(416, 494);

        AddLabel("Settings", 16, 10, 240, 30, 14, true);
        AddLabel("AUDIO", 18, 53, 150, 20, 8.2f, true, Theme.Teal);
        var devices = AddCombo("Audio output", 161, 76, 235);
        AudioDevice[] available;
        try { available = PadOutput.ListDevices(); }
        catch (Exception ex)
        {
            available = [new("", "System default")];
            string message = "Audio devices could not be listed. " + ex.Message;
            var label = AddLabel("Audio devices could not be listed.", 18, 172, 375, 22, 8.2f, false, Theme.Pink);
            deviceHint.SetToolTip(label, message);
        }
        devices.Items.AddRange(available);
        int deviceIndex = Array.FindIndex(available, device => device.Id == Result.DeviceId);
        if (deviceIndex < 0)
            deviceIndex = devices.Items.Add(new AudioDevice(Result.DeviceId, "Saved device · unavailable"));
        devices.SelectedIndex = deviceIndex;
        AddLabel("Output device", 18, 73, 135, 27);
        var crossfade = AddCombo("Crossfade duration", 161, 106, 108);
        crossfade.Items.AddRange(Durations.Select(number => (object)$"{number:0.0} s").ToArray());
        crossfade.SelectedIndex = Array.IndexOf(Durations, Result.Crossfade);
        if (crossfade.SelectedIndex == -1)
            crossfade.SelectedIndex = 3;
        AddLabel("Crossfade", 18, 103, 135, 27);
        var fade = AddNumber("Transport fade duration in milliseconds", 161, 136, 108, 50, 1000, 50,
            (decimal)Math.Round(Result.TransportFade * 1000));
        AddLabel("Transport fade (ms)", 18, 132, 138, 27);
        AddLabel("∞   Continuous", 285, 102, 115, 27, 8.7f, false, Theme.Muted);
        var volume = AddNumber("Master volume", 161, 166, 108, 0, 100, 1, Result.Volume);
        AddLabel("Master volume (%)", 18, 162, 140, 27);

        AddLabel("VOCAL RANGE", 18, 210, 150, 20, 8.2f, true, Theme.Blue);
        voice = AddCombo("Voice profile", 161, 234, 235);
        voice.Items.AddRange(PlayerPreferences.Profiles);
        voice.SelectedIndex = Result.Profile;
        AddLabel("Voice profile", 18, 230, 135, 27);
        semitones = AddNumber("Transpose in semitones", 161, 264, 108, -6, 6, 1, Result.Offsets[Result.Profile]);
        AddLabel("Transpose (semitones)", 18, 259, 145, 27);
        reset = AddButton("Reset", 281, 260, 115, 28, ButtonKind.Plain, theme, () => semitones.Value = 0);
        preview = AddLabel("", 18, 289, 376, 24, 8.6f, false, Theme.Muted);
        voice.SelectedIndexChanged += (_, _) =>
        {
            loadingProfile = true;
            Result.Profile = voice.SelectedIndex;
            semitones.Value = Result.Offsets[Result.Profile];
            loadingProfile = false;
            UpdateProfilePreview();
        };
        semitones.ValueChanged += (_, _) =>
        {
            if (loadingProfile)
                return;
            Result.Offsets[Result.Profile] = (int)semitones.Value;
            UpdateProfilePreview();
        };
        UpdateProfilePreview();

        AddLabel("WINDOW", 18, 331, 170, 20, 8.2f, true, Theme.Teal);
        var top = AddCheck("Always on top", 18, 354, preferences.AlwaysOnTop);
        var compact = AddCheck("Compact mode", 18, 377, preferences.Compact);
        var position = AddCheck("Remember window position", 18, 400, preferences.RememberLocation);
        var tray = AddCheck("Minimize to tray", 18, 423, preferences.MinimizeToTray);

        AddLabel("ON STARTUP", 225, 331, 170, 20, 8.2f, true, Theme.Teal);
        var key = AddCheck("Restore selected key", 225, 354, preferences.RestoreKey);
        var restoreVolume = AddCheck("Restore volume", 225, 377, preferences.RestoreVolume);
        var profile = AddCheck("Restore voice profile", 225, 400, preferences.RestoreProfile);

        var cancel = AddButton("Cancel", 223, 454, 79, 28, ButtonKind.Plain, theme, () => DialogResult = DialogResult.Cancel);
        var apply = AddButton("Apply", 310, 454, 87, 28, ButtonKind.Plain, theme, () =>
        {
            Result.DeviceId = ((AudioDevice)devices.SelectedItem!).Id;
            Result.Crossfade = Durations[crossfade.SelectedIndex];
            Result.TransportFade = (double)fade.Value / 1000;
            Result.Volume = (int)volume.Value;
            Result.AlwaysOnTop = top.Checked;
            Result.Compact = compact.Checked;
            Result.RememberLocation = position.Checked;
            Result.MinimizeToTray = tray.Checked;
            Result.RestoreKey = key.Checked;
            Result.RestoreVolume = restoreVolume.Checked;
            Result.RestoreProfile = profile.Checked;
            DialogResult = DialogResult.OK;
        });
        AcceptButton = apply;
        CancelButton = cancel;
    }

    private readonly ToolTip deviceHint = new();
    internal PlayerPreferences Result { get; }

    private void UpdateProfilePreview()
    {
        semitones.Enabled = reset.Enabled = Result.Profile != 0;
        preview.Text = $"Selected  {PadLibrary.Key(Result.SelectedKey)}      →      Pad  {PadLibrary.Key(Result.SoundingKey)}";
    }

    private Label AddLabel(string text, int x, int y, int w, int h,
        float size = 9, bool bold = false, Color? color = null)
    {
        var control = new Label
        {
            Text = text, Bounds = new Rectangle(x, y, w, h), TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color ?? Theme.Text
        };
        Controls.Add(control);
        return control;
    }

    private ComboBox AddCombo(string name, int x, int y, int width)
    {
        var control = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Surface, ForeColor = Theme.Text, AccessibleName = name,
            Bounds = new Rectangle(x, y, width, 23)
        };
        Controls.Add(control);
        return control;
    }

    private NumericUpDown AddNumber(string name, int x, int y, int w, int min, int max, int step, decimal value)
    {
        var control = new NumericUpDown
        {
            AccessibleName = name, Minimum = min, Maximum = max, Increment = step, Value = value,
            BackColor = Theme.Surface, ForeColor = Theme.Text, Bounds = new Rectangle(x, y, w, 23)
        };
        Controls.Add(control);
        return control;
    }

    private CheckBox AddCheck(string label, int x, int y, bool value)
    {
        var control = new CheckBox
        {
            Text = label, Checked = value, ForeColor = Theme.Text,
            Bounds = new Rectangle(x, y, x == 18 ? 202 : 173, 23)
        };
        Controls.Add(control);
        return control;
    }

    private Button AddButton(string text, int x, int y, int w, int h, ButtonKind kind, Theme theme, Action click)
    {
        var control = new PlayerButton(text, kind, theme.Font(8.4f, true)) { Bounds = new Rectangle(x, y, w, h) };
        control.Click += (_, _) => click();
        Controls.Add(control);
        return control;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            deviceHint.Dispose();
        base.Dispose(disposing);
    }
}
