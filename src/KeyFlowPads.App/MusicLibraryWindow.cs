using KeyFlowPads.Audio;

namespace KeyFlowPads.App;

internal sealed class MusicLibraryWindow : Form
{
    private readonly MusicLibrary library;
    private readonly Action<MusicTrack> play;
    private readonly TextBox search;
    private readonly ListView tracks;
    private readonly Button importButton;
    private readonly Button playButton;
    private readonly Label status;
    private readonly Label playback;
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<MusicTrack> catalog = [];

    internal MusicLibraryWindow(Theme theme, MusicLibrary library, Action<MusicTrack> play, Action youtube)
    {
        this.library = library;
        this.play = play;
        Text = "KeyFlow Pads · Music Library";
        Icon = theme.Icon;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(442, 363);
        MinimumSize = new Size(393, 300);

        search = new TextBox
        {
            PlaceholderText = "Search your music",
            AccessibleName = "Search your music",
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Bounds = new Rectangle(14, 15, 295, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        search.TextChanged += (_, _) => ShowTracks();

        importButton = MakeButton(theme, "Import files…", 323, 11, 105, AnchorStyles.Top | AnchorStyles.Right);
        importButton.Click += (_, _) => ImportFiles();
        playback = new Label
        {
            AutoEllipsis = true, ForeColor = Theme.Teal, Bounds = new Rectangle(14, 49, 414, 21),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        tracks = new ListView
        {
            Bounds = new Rectangle(14, 75, 414, 201),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = "Imported tracks", BackColor = Theme.Surface, ForeColor = Theme.Text,
            View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        tracks.Columns.Add("Track", 322);
        tracks.Columns.Add("Length", 75, HorizontalAlignment.Right);
        tracks.Resize += (_, _) => tracks.Columns[0].Width = Math.Max(95, tracks.ClientSize.Width - tracks.Columns[1].Width - 17);
        tracks.SelectedIndexChanged += (_, _) => playButton!.Enabled = Selection is not null;
        tracks.ItemActivate += (_, _) => PlaySelection();

        status = new Label
        {
            ForeColor = Theme.Muted, Bounds = new Rectangle(14, 283, 414, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, AutoEllipsis = true
        };
        var online = MakeButton(theme, "Search YouTube…", 14, 319, 146, AnchorStyles.Bottom | AnchorStyles.Left);
        online.Click += (_, _) => youtube();
        playButton = MakeButton(theme, "Play track", 320, 319, 108, AnchorStyles.Bottom | AnchorStyles.Right);
        playButton.Enabled = false;
        playButton.Click += (_, _) => PlaySelection();
        Controls.AddRange([search, importButton, playback, tracks, status, online, playButton]);
        Shown += async (_, _) =>
        {
            try { await Reload(); }
            catch (Exception ex) { if (!IsDisposed) ShowError($"Could not read the music library. {ex.Message}"); }
        };
    }

    private MusicTrack? Selection => tracks.SelectedItems.Count == 0 ? null : tracks.SelectedItems[0].Tag as MusicTrack;

    private static Button MakeButton(Theme theme, string text, int x, int y, int width, AnchorStyles anchor) =>
        new PlayerButton(text, ButtonKind.Plain, theme.Font(8.4f, true))
        {
            Bounds = new Rectangle(x, y, width, 34),
            Anchor = anchor
        };

    private void PlaySelection()
    {
        if (Selection is { } track)
        {
            status.ForeColor = Theme.Muted;
            status.Text = string.Empty;
            play(track);
        }
    }

    internal void SetPlayback(MusicTrack? current, Guid? loading, bool playing, bool mixing)
    {
        string text = loading is { } id && catalog.FirstOrDefault(track => track.Id == id) is { } next
            ? $"Preparing  {next.Title}"
            : current is null ? "Local music" : $"{(mixing ? "Mixing to" : playing ? "Playing" : "Selected")}  {current.Title}";
        if (playback.Text != text)
            playback.Text = text;
    }

    internal void ShowError(string message)
    {
        status.ForeColor = Theme.Pink;
        status.Text = message;
    }

    private async Task Reload()
    {
        var result = await Task.Run(library.List);
        if (IsDisposed)
            return;
        catalog = result;
        ShowTracks();
        if (catalog.Count == 0)
            status.Text = "Import a music file to start your library.";
    }

    private void ShowTracks()
    {
        Guid? selected = Selection?.Id;
        string query = search.Text.Trim();
        tracks.BeginUpdate();
        try
        {
            tracks.Items.Clear();
            foreach (var track in catalog)
            {
                if (!track.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    continue;
                string length = $"{(int)track.Duration.TotalMinutes}:{track.Duration.Seconds:00}";
                var item = tracks.Items.Add(new ListViewItem([track.Title, length]) { Tag = track });
                item.Selected = track.Id == selected;
            }
        }
        finally { tracks.EndUpdate(); }
        playButton.Enabled = Selection is not null;
    }

    internal async void ImportFiles()
    {
        if (!importButton.Enabled)
            return;
        importButton.Enabled = false;
        int imported = 0;
        var failures = new List<string>();
        var cancellation = lifetime.Token;
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Import music",
                Multiselect = true,
                CheckFileExists = true,
                Filter = "Audio files|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.wma|All files|*.*"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            foreach (string path in dialog.FileNames)
            {
                string name = Path.GetFileName(path);
                status.ForeColor = Theme.Muted;
                status.Text = $"Importing {name}…";
                try
                {
                    await Task.Run(() => library.Import(path, cancellation), cancellation);
                    imported++;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
                catch (Exception ex) { failures.Add($"{name} — {ex.Message}"); }
                if (IsDisposed)
                    return;
            }
            search.Clear();
            await Reload();
            if (IsDisposed)
                return;
            if (failures.Count > 0)
                ShowError($"{imported} imported, {failures.Count} skipped. {failures[0]}");
            else
                status.Text = imported == 1 ? "1 track imported." : $"{imported} tracks imported.";
        }
        catch (Exception ex) { if (!IsDisposed) ShowError(ex.Message); }
        finally { if (!IsDisposed) importButton.Enabled = true; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            lifetime.Cancel();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
