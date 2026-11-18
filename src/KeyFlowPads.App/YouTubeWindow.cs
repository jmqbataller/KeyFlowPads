using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KeyFlowPads.App;

internal sealed class YouTubeWindow : Form
{
    private readonly WebView2 player = new() { Dock = DockStyle.Fill };
    private readonly TextBox query;
    private readonly Label status;
    private readonly CheckBox blockAds;
    private readonly Action onAudioStarted;
    private YouTubeAdBlocker? adBlocker;
    private Task? initialize;
    private Uri? lastPage;
    private int searchVersion;
    private bool active;

    internal YouTubeWindow(Theme theme, Action onAudioStarted, bool blockAdsByDefault, Action<bool> onBlockAdsChanged)
    {
        this.onAudioStarted = onAudioStarted;
        Text = "KeyFlow Pads · YouTube";
        Icon = theme.Icon;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        ClientSize = new Size(636, 502);
        MinimumSize = new Size(495, 424);

        var top = new Panel { Dock = DockStyle.Top, Height = 50 };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 31 };
        blockAds = new CheckBox
        {
            Text = "Block ads", AccessibleName = "Block YouTube ads", Checked = blockAdsByDefault,
            Dock = DockStyle.Left, Width = 124, Padding = new Padding(12, 0, 0, 0), ForeColor = Theme.Text
        };
        blockAds.CheckedChanged += async (_, _) =>
        {
            onBlockAdsChanged(blockAds.Checked);
            if (adBlocker is { } blocker)
            {
                blocker.Enabled = blockAds.Checked;
                await blocker.ApplyPageAsync();
            }
        };
        status = new Label
        {
            ForeColor = Theme.Muted, AutoEllipsis = true, Dock = DockStyle.Fill,
            Padding = new Padding(12, 0, 12, 0), TextAlign = ContentAlignment.MiddleLeft,
            Text = "YouTube plays while this window is active."
        };
        footer.Controls.Add(status);
        footer.Controls.Add(blockAds);
        query = new TextBox
        {
            PlaceholderText = "Search YouTube",
            AccessibleName = "YouTube search",
            BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            Bounds = new Rectangle(12, 15, 380, 25),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        top.Size = new Size(ClientSize.Width, 50);
        var search = new PlayerButton("Search", ButtonKind.Plain, theme.Font(8.4f, true))
        {
            Bounds = new Rectangle(404, 10, 83, 31), Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        var browser = new PlayerButton("Open in browser", ButtonKind.Plain, theme.Font(8.4f, true))
        {
            Bounds = new Rectangle(493, 10, 131, 31), Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        top.Controls.AddRange([query, search, browser]);
        search.Click += async (_, _) => await Search();
        browser.Click += (_, _) => OpenBrowser();
        AcceptButton = search;
        Controls.AddRange([player, footer, top]);
        player.Visible = false;
    }

    private static Uri SearchPage(string text) => string.IsNullOrWhiteSpace(text)
        ? new("https://www.youtube.com/")
        : new("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(text.Trim()));

    private async Task Search()
    {
        int version = ++searchVersion;
        Uri address = SearchPage(query.Text);
        lastPage = address;
        status.Text = "Opening YouTube…";
        try
        {
            initialize ??= Configure();
            await initialize;
            if (IsDisposed || !active || searchVersion != version)
                return;
            player.CoreWebView2.IsMuted = false;
            player.Visible = true;
            player.CoreWebView2.Navigate(address.AbsoluteUri);
        }
        catch (Exception)
        {
            initialize = null;
            if (!IsDisposed)
                status.Text = "YouTube could not open here. Use Open in browser.";
        }
    }

    private async Task Configure()
    {
        string path = Path.Combine(PlayerPreferences.DirectoryPath, "YouTube");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: path);
        if (IsDisposed)
            return;
        await player.EnsureCoreWebView2Async(environment);
        if (IsDisposed)
            return;
        var web = player.CoreWebView2;
        var blocker = new YouTubeAdBlocker(web, blockAds.Checked);
        try
        {
            await blocker.AttachAsync();
            if (IsDisposed)
                return;
            blocker.Enabled = blockAds.Checked;
            adBlocker = blocker;
        }
        catch (Exception error) when (error is NotImplementedException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            if (IsDisposed)
                return;
            blockAds.Text = "Filter unavailable";
            blockAds.Enabled = false;
        }
        web.Settings.IsWebMessageEnabled = false;
        web.Settings.AreHostObjectsAllowed = false;
        web.Settings.AreDevToolsEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
        web.Settings.AreDefaultScriptDialogsEnabled = false;
        web.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        web.DownloadStarting += (_, args) => args.Cancel = true;
        web.NavigationStarting += (_, args) =>
        {
            if (args.Uri != "about:blank" && !IsYouTubePage(args.Uri))
            {
                args.Cancel = true;
                status.Text = "Use Open in browser to continue outside YouTube.";
            }
        };
        web.NavigationCompleted += (_, args) =>
        {
            if (!player.Visible || !active)
                return;
            if (args.IsSuccess && IsYouTubePage(web.Source))
            {
                if (new Uri(web.Source).Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
                    lastPage = new Uri(web.Source);
                status.Text = "YouTube plays while this window is active.";
            }
            else
                status.Text = "YouTube could not load here. Use Open in browser.";
        };
        web.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (args.IsUserInitiated && IsYouTubePage(args.Uri))
                web.Navigate(args.Uri);
        };
        web.IsDocumentPlayingAudioChanged += (_, _) =>
        {
            if (active && web.IsDocumentPlayingAudio)
                onAudioStarted();
        };
    }

    private static bool IsYouTubePage(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host is "www.youtube.com" or "youtube.com" or "m.youtube.com" or "consent.youtube.com" or "consent.google.com";

    private void OpenBrowser()
    {
        var address = player.Visible && lastPage is not null ? lastPage : SearchPage(query.Text);
        StopPlaying();
        try { Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { status.Text = "Windows could not open the default browser."; }
    }

    private void StopPlaying()
    {
        searchVersion++;
        if (!IsDisposed && !Disposing && player.CoreWebView2 is { } web && player.Visible)
        {
            web.IsMuted = true;
            player.Visible = false;
            web.Navigate("about:blank");
            status.Text = "Playback stopped. Search again to reopen YouTube.";
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        active = true;
        base.OnActivated(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        active = false;
        StopPlaying();
        base.OnDeactivate(e);
    }
}
