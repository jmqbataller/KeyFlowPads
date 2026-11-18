using System.Drawing.Drawing2D;
using System.Drawing.Text;
using KeyFlowPads.Audio;
using Microsoft.Win32;

namespace KeyFlowPads.App;

internal sealed class PlayerWindow : Form
{
    private readonly Theme theme;
    private readonly PlayerButton[] pads = new PlayerButton[PadLibrary.Count];
    private readonly PlayerButton play;
    private readonly PlayerButton stop;
    private readonly PlayerButton settings;
    private readonly PlayerButton importMusic;
    private readonly PlayerButton musicLibraryButton;
    private readonly PlayerButton youtube;
    private readonly PlayerButton compact;
    private readonly PlayerButton hide;
    private readonly TrackBar volume;
    private readonly Label volumeLabel;
    private readonly Label volumeValue;
    private readonly Label voiceLabel;
    private readonly ComboBox profile;
    private readonly Label notice;
    private readonly ToolTip hints = new() { InitialDelay = 450 };
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip trayMenu;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 65 };
    private readonly System.Windows.Forms.Timer save = new() { Interval = 350 };
    private PlayerPreferences preferences;
    private PadLibrary? library;
    private PadMixer? mixer;
    private PadOutput? output;
    private readonly MusicLibrary musicLibrary = new(Path.Combine(PlayerPreferences.DirectoryPath, "Music"));
    private MusicLibraryWindow? musicWindow;
    private YouTubeWindow? youtubeWindow;
    private PreparedMusic? selectedMusic;
    private Guid? preparingMusic;
    private long selectionVersion;
    private long musicPlaybackRequest;
    private MixerStatus? displayed;
    private (bool Live, bool Mixing, Transport Transport, int Selected, int Sounding, bool Error, bool Loaded, bool Closing, Guid? Music, Guid? Preparing)? paintedStatus;
    private string? startupNotice;
    private string? actionNotice;
    private bool requestedPlay;
    private bool applyingPreferences;
    private bool restoringLocation;
    private bool openingSettings;
    private bool closing;
    private bool allowClose;
    private Point? dragOffset;

    internal PlayerWindow(Theme theme) : this(theme, PlayerPreferences.Load(out var warning), warning)
    {
    }

    internal PlayerWindow(Theme theme, PlayerPreferences preferences, string? warning = null)
    {
        this.theme = theme;
        this.preferences = preferences;
        startupNotice = warning;
        Text = "KeyFlow Pads";
        AccessibleName = Text;
        Icon = theme.Icon;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        DoubleBuffered = true;
        TopMost = preferences.AlwaysOnTop;

        settings = AddButton("Settings", ButtonKind.Settings, () => OpenSettings());
        compact = AddButton("Compact mode", ButtonKind.Compact, ToggleCompact);
        hide = AddButton("Minimize", ButtonKind.Hide, Minimize);
        for (int key = 0; key < pads.Length; key++)
        {
            int capturedKey = key;
            pads[key] = AddButton(PadLibrary.Key(key), ButtonKind.Pad, () => SelectKey(capturedKey), 12);
            pads[key].Enabled = false;
        }
        play = AddButton("Play", ButtonKind.Play, TogglePlayback, 9);
        stop = AddButton("Stop", ButtonKind.Stop, StopPlayback, 9);
        play.Enabled = stop.Enabled = false;
        importMusic = AddButton("Import Music", ButtonKind.Plain, ImportMusic, 8.5f);
        musicLibraryButton = AddButton("Library", ButtonKind.Plain, OpenMusic, 8.5f);
        musicLibraryButton.AccessibleName = "Music Library";
        youtube = AddButton("YouTube", ButtonKind.Plain, OpenYouTube, 8.5f);

        volumeLabel = AddLabel("Volume");
        volumeValue = AddLabel("%");
        volumeValue.TextAlign = ContentAlignment.MiddleRight;
        volume = new TrackBar
        {
            Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, AutoSize = false,
            SmallChange = 1, LargeChange = 10, BackColor = Theme.Background,
            AccessibleName = "Master volume", TabStop = true
        };
        volume.ValueChanged += (_, _) =>
        {
            volumeValue.Text = $"{volume.Value}%";
            if (applyingPreferences)
                return;
            preferences.Volume = volume.Value;
            if (mixer is not null)
                mixer.Volume = volume.Value / 100f;
            DeferSave();
        };
        Controls.Add(volume);
        voiceLabel = AddLabel("Vocal range");
        profile = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Surface, ForeColor = Theme.Text, Font = new Font("Segoe UI", 9.25f),
            AccessibleName = "Vocal range", IntegralHeight = false, DropDownHeight = 195
        };
        profile.Items.AddRange(PlayerPreferences.Profiles);
        profile.SelectedIndexChanged += (_, _) =>
        {
            if (applyingPreferences || profile.SelectedIndex < 0)
                return;
            var updated = preferences.Copy();
            updated.Profile = profile.SelectedIndex;
            if (!ApplySoundingKey(updated))
                UpdateControls();
        };
        Controls.Add(profile);
        notice = AddLabel("", 8.3f);
        notice.ForeColor = Theme.Pink;
        notice.AutoEllipsis = true;
        notice.AccessibleName = "Player status message";

        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show KeyFlow Pads", null, (_, _) => RestorePlayer());
        trayMenu.Items.Add("Music Library", null, (_, _) => { RestorePlayer(); OpenMusic(); });
        trayMenu.Items.Add("Pause / Resume", null, (_, _) => TogglePlayback());
        trayMenu.Items.Add("Stop", null, (_, _) => StopPlayback());
        trayMenu.Items.Add("Exit KeyFlow Pads", null, (_, _) => Close());
        tray = new NotifyIcon { Icon = theme.Icon, Text = "KeyFlow Pads", ContextMenuStrip = trayMenu, Visible = true };
        tray.DoubleClick += (_, _) => RestorePlayer();
        refresh.Tick += (_, _) => RefreshState();
        save.Tick += (_, _) => { save.Stop(); SavePreferences(); };
        LocationChanged += (_, _) =>
        {
            if (restoringLocation || !Visible || WindowState != FormWindowState.Normal)
                return;
            if (preferences.RememberLocation)
                preferences.Location = Location;
            DeferSave();
        };
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        UpdateControls();
        LayoutPlayer();
        RestoreLocation();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) // WM_MOUSEACTIVATE: deliver the click and keep presentation focus.
        {
            message.Result = 3; // MA_NOACTIVATE
            return;
        }
        base.WndProc(ref message);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        refresh.Start();
        try
        {
            library = await Task.Run(() => new PadLibrary(Path.Combine(AppContext.BaseDirectory, "audio")));
            if (IsDisposed || closing)
                return;
            mixer = new PadMixer(library);
            UpdateMixerSettings();
            OpenOutput();
            UpdateControls();
            startupNotice ??= Enumerable.Range(0, PadLibrary.Count).Select(library.Error).FirstOrDefault(error => error is not null);
        }
        catch (Exception ex)
        {
            startupNotice = "Could not prepare the pads. " + ex.Message;
        }
        RefreshState();
    }

    private void OpenOutput()
    {
        if (mixer is null || output is { Error: null })
            return;
        output?.Dispose();
        output = null;
        try
        {
            output = new PadOutput(mixer, preferences.DeviceId);
            actionNotice = null;
        }
        catch (Exception ex)
        {
            requestedPlay = false;
            actionNotice = "Audio output unavailable. " + ex.Message;
        }
    }

    private void UpdateMixerSettings()
    {
        if (mixer is null)
            return;
        mixer.Volume = preferences.Volume / 100f;
        mixer.CrossfadeSeconds = preferences.Crossfade;
        mixer.TransportFadeSeconds = preferences.TransportFade;
    }

    private PlayerButton AddButton(string text, ButtonKind kind, Action handler, float size = 9)
    {
        var button = new PlayerButton(text, kind, theme.Font(size, true));
        button.Click += (_, _) => handler();
        Controls.Add(button);
        hints.SetToolTip(button, text);
        return button;
    }

    private Label AddLabel(string text, float size = 8.75f)
    {
        var label = new Label
        {
            Text = text, Font = theme.Font(size), UseCompatibleTextRendering = true,
            ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(label);
        return label;
    }

    private void SelectKey(int key)
    {
        if (closing)
            return;
        CancelMusicSelection();
        var updated = preferences.Copy();
        updated.SelectedKey = key;
        if (StartKey(updated.SoundingKey))
        {
            selectedMusic = null;
            musicPlaybackRequest = 0;
            preferences = updated;
            DeferSave();
            RefreshState();
        }
    }

    private bool StartKey(int key)
    {
        if (mixer is null || library is null || openingSettings)
            return false;
        if (library.Error(key) is { } missing)
        {
            actionNotice = missing;
            RefreshState();
            return false;
        }
        OpenOutput();
        if (output is null)
        {
            RefreshState();
            return false;
        }
        youtubeWindow?.Close();
        requestedPlay = mixer.Play(key);
        actionNotice = null;
        return requestedPlay;
    }

    private void TogglePlayback()
    {
        if (mixer is null || closing || openingSettings)
            return;
        CancelMusicSelection();
        if (requestedPlay)
        {
            mixer.Pause();
            requestedPlay = false;
        }
        else if (selectedMusic is not null)
            StartMusic(selectedMusic);
        else
            StartKey(preferences.SoundingKey);
        RefreshState();
    }

    private void StopPlayback()
    {
        if (openingSettings)
            return;
        CancelMusicSelection();
        mixer?.Stop();
        requestedPlay = false;
        actionNotice = null;
        RefreshState();
    }

    private bool ApplySoundingKey(PlayerPreferences updated)
    {
        if (selectedMusic is null && requestedPlay && preferences.SoundingKey != updated.SoundingKey && !StartKey(updated.SoundingKey))
            return false;
        preferences = updated;
        UpdateControls();
        DeferSave();
        RefreshState();
        return true;
    }

    private async void OpenSettings()
    {
        if (openingSettings || closing)
            return;
        using var dialog = new SettingsWindow(theme, preferences);
        dialog.TopMost = TopMost;
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var updated = dialog.Result;
        if (preferences.DeviceId != updated.DeviceId)
        {
            openingSettings = true;
            mixer?.Pause();
            requestedPlay = false;
            // Drain the rendered fade before releasing the old endpoint.
            await Task.Delay(TimeSpan.FromSeconds(preferences.TransportFade + 0.12));
            if (IsDisposed || closing)
                return;
            output?.Dispose();
            output = null;
            openingSettings = false;
        }
        if (ApplySoundingKey(updated))
        {
            TopMost = preferences.AlwaysOnTop;
            if (musicWindow is not null)
                musicWindow.TopMost = TopMost;
            UpdateMixerSettings();
            LayoutPlayer();
            KeepOnScreen();
            OpenOutput();
        }
        RefreshState();
    }

    private void UpdateControls()
    {
        applyingPreferences = true;
        volume.Value = preferences.Volume;
        volumeValue.Text = $"{preferences.Volume}%";
        profile.SelectedIndex = preferences.Profile;
        for (int i = 0; i < pads.Length; i++)
        {
            int recording = (i + PadLibrary.Count + preferences.Offsets[preferences.Profile]) % PadLibrary.Count;
            pads[i].Enabled = library is not null && library.Error(recording) is null;
            hints.SetToolTip(pads[i], library?.Error(recording) ?? $"Play {PadLibrary.Key(recording)}");
        }
        applyingPreferences = false;
    }

    private void RefreshState()
    {
        if (IsDisposed)
            return;
        MixerStatus? current = mixer?.Status;
        if (musicPlaybackRequest != 0 && current?.CompletedMusicRequest == musicPlaybackRequest)
        {
            requestedPlay = false;
            musicPlaybackRequest = 0;
        }
        if (output?.Error is { } fault && requestedPlay)
        {
            requestedPlay = false;
            mixer?.Pause();
            actionNotice = "Audio output interrupted. " + fault;
        }
        else if (current?.Error is not null && requestedPlay)
        {
            requestedPlay = false;
            mixer?.Stop();
        }
        displayed = current;
        bool live = requestedPlay || output is { Error: null } && current?.Transport is Transport.Pausing or Transport.Stopping;
        int sounding = selectedMusic is null && requestedPlay ? preferences.SoundingKey : displayed?.Target ?? -1;
        for (int i = 0; i < pads.Length; i++)
            pads[i].SetPadState(selectedMusic is null && i == preferences.SelectedKey, live && i == sounding);
        string playText = requestedPlay ? "Pause" : current?.Transport is Transport.Paused or Transport.Pausing ? "Resume" : "Play";
        ButtonKind kind = preferences.Compact ? requestedPlay ? ButtonKind.IconPause : ButtonKind.IconPlay : ButtonKind.Play;
        if (play.Text != playText || play.Kind != kind || play.Playing != requestedPlay)
        {
            play.Text = play.AccessibleName = playText;
            play.Kind = kind;
            play.Playing = requestedPlay;
            play.Invalidate();
            hints.SetToolTip(play, playText);
        }
        play.Enabled = mixer is not null && !closing;
        stop.Enabled = mixer is not null && (requestedPlay || current?.Transport != Transport.Stopped);
        string message = actionNotice ?? current?.Error ?? startupNotice ?? "";
        if (message != notice.Text)
        {
            notice.Text = message;
            notice.Visible = message.Length != 0;
            hints.SetToolTip(notice, message);
        }
        var visual = (requestedPlay, current?.Switching == true || requestedPlay && current?.Target >= 0 &&
            current.Target != preferences.SoundingKey, current?.Transport ?? Transport.Stopped, preferences.SelectedKey,
            preferences.SoundingKey, output?.Error is not null || actionNotice?.StartsWith("Audio output", StringComparison.Ordinal) == true,
            library is not null, closing, selectedMusic?.Track.Id, preparingMusic);
        if (visual != paintedStatus)
        {
            paintedStatus = visual;
            Invalidate(new Rectangle(D(9), D(52), Width - D(18), D(69)));
        }
        musicWindow?.SetPlayback(selectedMusic?.Track, preparingMusic, requestedPlay, current?.Switching == true);
    }

    private void ToggleCompact()
    {
        preferences.Compact = !preferences.Compact;
        LayoutPlayer();
        KeepOnScreen();
        DeferSave();
        RefreshState();
    }

    private void Minimize()
    {
        youtubeWindow?.Close();
        musicWindow?.Hide();
        if (preferences.MinimizeToTray)
            Hide();
        else
            WindowState = FormWindowState.Minimized;
    }

    private void RestorePlayer()
    {
        WindowState = FormWindowState.Normal;
        KeepOnScreen();
        Show();
    }

    private void RestoreLocation()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        Location = preferences.RememberLocation && preferences.Location is { } stored
            ? stored : new Point(area.Right - Width - D(32), area.Top + D(70));
        KeepOnScreen();
    }

    private void KeepOnScreen()
    {
        Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
        restoringLocation = true;
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        restoringLocation = false;
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(KeepOnScreen);
    }

    private int D(int number) => (int)Math.Round(number * DeviceDpi / 96d);
    private void Place(Control control, int x, int y, int w, int h) => control.SetBounds(D(x), D(y), D(w), D(h));

    private void LayoutPlayer()
    {
        bool mini = preferences.Compact;
        int width = mini ? 264 : 320;
        int margin = mini ? 10 : 12;
        int padWidth = (width - 2 * margin - 18) / 4;
        int padHeight = mini ? 35 : 42;
        int padStart = mini ? 113 : 128;
        SuspendLayout();
        ClientSize = new Size(D(width), D(mini ? 326 : 416));
        Place(hide, width - margin - 27, 13, 27, 28);
        Place(compact, width - margin - 58, 13, 27, 28);
        Place(settings, width - margin - 89, 13, 27, 28);
        settings.Visible = !mini;
        compact.Playing = mini;
        compact.AccessibleName = mini ? "Expand player" : "Compact mode";
        hints.SetToolTip(compact, compact.AccessibleName);
        for (int index = 0; index < pads.Length; index++)
            Place(pads[index], margin + index % 4 * (padWidth + 6), padStart + index / 4 * (padHeight + 5), padWidth, padHeight);

        int bottom = padStart + 3 * (padHeight + 5) + 2;
        int transportWidth = width - margin * 2;
        Place(play, margin, bottom, transportWidth * 2 / 3, 33);
        Place(stop, margin + transportWidth * 2 / 3 + 6, bottom, transportWidth - transportWidth * 2 / 3 - 6, 33);
        stop.Kind = mini ? ButtonKind.IconStop : ButtonKind.Stop;
        int mediaY = bottom + 38;
        int mediaWidth = (transportWidth - 12) / 3;
        Place(importMusic, margin, mediaY, mediaWidth, 30);
        Place(musicLibraryButton, margin + mediaWidth + 6, mediaY, mediaWidth, 30);
        Place(youtube, margin + 2 * (mediaWidth + 6), mediaY, transportWidth - 2 * mediaWidth - 12, 30);
        Place(volumeLabel, margin + 1, 344, 53, 23);
        Place(volume, margin + 51, 345, 204, 22);
        Place(volumeValue, width - margin - 41, 344, 40, 23);
        Place(voiceLabel, margin + 1, 372, 74, 24);
        Place(profile, margin + 78, 372, width - 2 * margin - 78, 25);
        Place(notice, margin + 1, mini ? 306 : 397, width - 2 * margin - 2, 17);
        volume.Visible = volumeLabel.Visible = volumeValue.Visible = voiceLabel.Visible = profile.Visible = !mini;
        ResumeLayout(false);
        Invalidate(true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutPlayer();
        KeepOnScreen();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        float s = DeviceDpi / 96f;
        float width = ClientSize.Width / s;
        float height = ClientSize.Height / s;
        bool mini = preferences.Compact;
        int margin = mini ? 10 : 12;
        g.ScaleTransform(s, s);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var frame = new Pen(Theme.Border);
        g.DrawRectangle(frame, 0, 0, width - 1, height - 1);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(theme.Mark, margin - 2, 7, 41, 41);
        Draw(g, "KeyFlow Pads", margin + 43, 12, mini ? 12.3f : 13.4f, true, Theme.Text);
        Draw(g, $"v{typeof(PlayerWindow).Assembly.GetName().Version?.ToString(3)}",
            margin + 44, 34, 7, false, Theme.Muted);

        int cardY = 54;
        int cardHeight = mini ? 51 : 65;
        using var card = Theme.Rounded(new RectangleF(margin + 1, cardY, width - 2 * margin - 2, cardHeight), 9);
        using var fill = new SolidBrush(Theme.Surface);
        g.FillPath(fill, card);
        g.DrawPath(frame, card);
        var current = displayed;
        bool transitioning = requestedPlay && (current is { Switching: true } ||
            current is { Transport: Transport.Playing } && (selectedMusic is null
                ? current.Target != preferences.SoundingKey : current.MusicId != selectedMusic.Track.Id));
        string state = closing ? "Stopping" : library is null ? "Loading" :
            output?.Error is not null || actionNotice?.StartsWith("Audio output", StringComparison.Ordinal) == true ? "Output error" :
            preparingMusic.HasValue ? "Loading" : transitioning ? "Mixing" : requestedPlay ? "Playing" :
            current?.Transport switch
            {
                Transport.Stopping => "Stopping",
                Transport.Pausing => "Pausing",
                Transport.Paused => "Paused",
                _ => "Ready"
            };
        Color liveInk = requestedPlay ? Theme.Teal : Theme.Muted;
        using var stateInk = new SolidBrush(liveInk);
        float keyWidth = mini ? 61 : 72;
        float keyTop = cardY + (mini ? 20 : 24);
        float origin = margin + 12;
        float statusX = width - margin - (mini ? 89 : 101);
        if (selectedMusic is { } music)
        {
            Draw(g, "MUSIC", origin, cardY + 7, 7.1f, true, Theme.Muted);
            using var titleInk = new SolidBrush(Theme.Text);
            using var titleFormat = new StringFormat(StringFormat.GenericTypographic)
            {
                Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap
            };
            g.DrawString(music.Track.Title, theme.Font(11 * 96f / DeviceDpi, true), titleInk,
                new RectangleF(origin, keyTop + 1, statusX - origin - 7, 23), titleFormat);
        }
        else
        {
            Draw(g, "SELECTED", origin, cardY + 7, 7.1f, true, Theme.Muted);
            Draw(g, transitioning ? "MIX TO" : requestedPlay ? "SOUNDING" : "PAD", origin + keyWidth, cardY + 7, 7.1f, true, Theme.Muted);
            Draw(g, PadLibrary.Key(preferences.SelectedKey), origin, keyTop, mini ? 15 : 20, true, Theme.Text);
            Draw(g, PadLibrary.Key(preferences.SoundingKey), origin + keyWidth, keyTop, mini ? 15 : 20, true, liveInk);
        }
        g.FillEllipse(stateInk, statusX, cardY + (mini ? 16 : 18), 5, 5);
        Draw(g, state, statusX + 10, cardY + (mini ? 11 : 13), 8.3f, true, liveInk);
        Draw(g, selectedMusic is null ? "∞  Loop" : "Play once", statusX + 10, cardY + (mini ? 29 : 36), 8.3f, false, Theme.Muted);
    }

    private void Draw(Graphics g, string text, float x, float y, float size, bool bold, Color color)
    {
        using var ink = new SolidBrush(color);
        g.DrawString(text, theme.Font(size * 96f / DeviceDpi, bold), ink, x, y, StringFormat.GenericTypographic);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.Y < D(53))
        {
            dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragOffset is { } offset && Capture)
            Location = new Point(Cursor.Position.X - offset.X, Cursor.Position.Y - offset.Y);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
            dragOffset = null;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragOffset = null;
        Capture = false;
    }

    private void DeferSave()
    {
        save.Stop();
        save.Start();
    }

    private void SavePreferences()
    {
        try { preferences.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            startupNotice = "Settings could not be saved. " + ex.Message;
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowClose && e.CloseReason == CloseReason.UserClosing && mixer?.Status.Transport is Transport.Playing or Transport.Pausing or Transport.Stopping)
        {
            e.Cancel = true;
            if (closing)
                return;
            closing = true;
            StopPlayback();
            await Task.Delay(TimeSpan.FromSeconds(preferences.TransportFade + 0.12));
            if (!IsDisposed)
            {
                allowClose = true;
                Close();
            }
            return;
        }
        SavePreferences();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            refresh.Dispose();
            save.Dispose();
            hints.Dispose();
            tray.Dispose();
            trayMenu.Dispose();
            musicWindow?.Dispose();
            youtubeWindow?.Dispose();
            output?.Dispose();
            mixer?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ImportMusic()
    {
        OpenMusic();
        musicWindow?.ImportFiles();
    }

    private void OpenMusic()
    {
        if (musicWindow is null || musicWindow.IsDisposed)
            musicWindow = new MusicLibraryWindow(theme, musicLibrary, SelectMusic, OpenYouTube);
        musicWindow.TopMost = TopMost;
        if (!musicWindow.Visible)
            musicWindow.Show();
        musicWindow.Activate();
        RefreshState();
    }

    private async void SelectMusic(MusicTrack track)
    {
        if (closing || mixer is null || openingSettings)
            return;
        long version = ++selectionVersion;
        preparingMusic = track.Id;
        RefreshState();
        try
        {
            var prepared = selectedMusic?.Track.Id == track.Id ? selectedMusic :
                await Task.Run(() => musicLibrary.Prepare(track.Id));
            if (IsDisposed || closing || version != selectionVersion)
                return;
            StartMusic(prepared);
        }
        catch (Exception ex)
        {
            if (version == selectionVersion && !IsDisposed)
            {
                actionNotice = $"Could not play {track.Title}. {ex.Message}";
                musicWindow?.ShowError(actionNotice);
            }
        }
        finally
        {
            if (version == selectionVersion && !IsDisposed)
            {
                preparingMusic = null;
                RefreshState();
            }
        }
    }

    private void StartMusic(PreparedMusic music)
    {
        OpenOutput();
        if (output is null || mixer is null)
            return;
        youtubeWindow?.Close();
        selectedMusic = music;
        musicPlaybackRequest = mixer.Play(music);
        requestedPlay = true;
        actionNotice = null;
    }

    private void CancelMusicSelection()
    {
        selectionVersion++;
        preparingMusic = null;
    }

    private void OpenYouTube()
    {
        if (youtubeWindow is null || youtubeWindow.IsDisposed)
            youtubeWindow = new YouTubeWindow(theme, () =>
            {
                if (requestedPlay)
                    TogglePlayback();
            }, preferences.BlockYouTubeAds, enabled =>
            {
                preferences.BlockYouTubeAds = enabled;
                DeferSave();
            });
        youtubeWindow.TopMost = TopMost;
        if (!youtubeWindow.Visible)
            youtubeWindow.Show();
        youtubeWindow.Activate();
    }
}
