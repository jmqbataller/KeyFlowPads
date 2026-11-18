using System.Diagnostics;
using System.Text;
using KeyFlowPads.App;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        // Use a separate WebView2 profile and fake YouTube origin. The fixture makes no internet requests.
        var root = Directory.CreateTempSubdirectory("KeyFlowPads-web-test-");
        using var form = new Form { Opacity = 0, ShowInTaskbar = false, ClientSize = new Size(630, 480) };
        using var view = new WebView2 { Dock = DockStyle.Fill };
        using var timeout = new System.Windows.Forms.Timer { Interval = 30000 };
        int code = 1;
        timeout.Tick += (_, _) =>
        {
            Console.Error.WriteLine("Timed out waiting for the WebView2 checks.");
            form.Close();
        };
        form.Controls.Add(view);
        form.Shown += async (_, _) =>
        {
            timeout.Start();
            try
            {
                CheckPlayerShortcuts();
                CheckUrls();
                await CheckWebView(root, view);
                code = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally
            {
                timeout.Stop();
                form.Close();
            }
        };
        Application.Run(form);
        view.Dispose();
        try { root.Delete(recursive: true); }
        catch (IOException) { } // The WebView2 process may still be releasing its profile.
        return code;
    }

    private static void CheckPlayerShortcuts()
    {
        using var artwork = new Theme();
        foreach (bool compact in new[] { false, true })
        {
            using var player = new PlayerWindow(artwork, new PlayerPreferences { Compact = compact });
            var buttons = player.Controls.OfType<Button>().ToArray();
            var shortcuts = new[] { "Import Music", "Library", "YouTube" }
                .Select(label => buttons.Single(button => button.Text == label)).ToArray();
            var stop = buttons.Single(button => button.Text == "Stop");
            Assert(shortcuts.All(button => player.ClientRectangle.Contains(button.Bounds) &&
                button.Top > stop.Bottom && button.TabStop), "A media shortcut was missing from the main player layout.");
            Assert(shortcuts[0].Right < shortcuts[1].Left && shortcuts[1].Right < shortcuts[2].Left,
                "The media shortcuts overlap.");
        }
        Console.WriteLine("Main-window Import Music, Library, and YouTube buttons fit in standard and compact modes.");
    }

    private static void CheckUrls()
    {
        string[] ads =
        [
            "https://googleads.g.doubleclick.net/pagead/id",
            "https://www.youtube.com/pagead?request=1",
            "https://m.youtube.com/api/stats/ads",
            "https://adservice.google.com/"
        ];
        string[] playback =
        [
            "https://www.youtube.com/results?search_query=pagead",
            "https://www.youtube.com/watch?v=test",
            "https://www.youtube.com/youtubei/v1/player",
            "https://r1.googlevideo.com/videoplayback",
            "https://i.ytimg.com/vi/test/default.jpg",
            "https://consent.youtube.com/",
            "https://www.youtube.com/pageadmin",
            "https://doubleclick.net.example.com/"
        ];
        Assert(ads.All(YouTubeAdBlocker.IsAdRequest), "An ad URL was allowed.");
        Assert(playback.All(url => !YouTubeAdBlocker.IsAdRequest(url)), "A playback, consent, search, or unrelated URL was blocked.");
    }

    private static async Task CheckWebView(DirectoryInfo root, WebView2 view)
    {
        const string fixture = """
            <!doctype html>
            <html lang="en"><head><link rel="icon" href="data:,"></head><body>
            <ytd-ad-slot-renderer id="ad">Sponsored listing</ytd-ad-slot-renderer>
            <div id="movie_player" class="ad-showing">
              <button disabled class="ytp-skip-ad-button" onclick="window.skipCount++">Skip Ad</button>
            </div>
            <script>window.skipCount = 0;</script>
            </body></html>
            """;

        var options = new CoreWebView2EnvironmentOptions("--proxy-server=127.0.0.1:1");
        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(root.FullName, "profile"), options: options);
        await view.EnsureCoreWebView2Async(environment);
        CoreWebView2 web = view.CoreWebView2;
        var blocker = new YouTubeAdBlocker(web, enabled: true);
        await blocker.AttachAsync();
        foreach (string file in new[] { "index.html", "video.txt" })
            web.AddWebResourceRequestedFilter($"https://www.youtube.com/{file}",
                CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        web.WebResourceRequested += (_, args) =>
        {
            if (args.Response is not null)
                return;
            bool home = args.Request.Uri == "https://www.youtube.com/index.html";
            string body = home ? fixture : "fixture resource";
            string contentType = home ? "text/html" : "text/plain";
            args.Response = environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)),
                200, "OK", $"Content-Type: {contentType}; charset=utf-8\r\nCache-Control: no-store");
        };
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        web.NavigationCompleted += (_, args) => loaded.TrySetResult(args.IsSuccess);
        web.Navigate("https://www.youtube.com/index.html");
        Assert(await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10)), "The local YouTube fixture failed to load.");

        await WaitFor(web, "getComputedStyle(ad).display === 'none'");
        int adStatus = await FetchStatus(web, "/pagead/fixture.txt");
        Assert(adStatus == 204, $"The ad request returned HTTP {adStatus}.");
        Assert(await FetchStatus(web, "/video.txt") == 200, "WebView2 blocked normal content.");
        await Task.Delay(650);
        Assert(await web.ExecuteScriptAsync("skipCount === 0") == "true", "The disabled Skip button was clicked.");
        await web.ExecuteScriptAsync("document.querySelector('button').disabled = false");
        await WaitFor(web, "skipCount === 1");
        await Task.Delay(650);
        Assert(await web.ExecuteScriptAsync("skipCount === 1") == "true", "The same Skip button was clicked again.");

        blocker.Enabled = false;
        await blocker.ApplyPageAsync();
        await WaitFor(web, "getComputedStyle(ad).display !== 'none'");
        Assert(await FetchStatus(web, "/pagead/fixture.txt") == 200, "The toggle did not restore requests.");
        Console.WriteLine($"WebView2 {environment.BrowserVersionString}: ad requests blocked; normal content allowed; sponsored slot hidden; enabled Skip button clicked once; toggle restored page and network behavior.");
    }

    private static async Task<int> FetchStatus(CoreWebView2 web, string path)
    {
        await web.ExecuteScriptAsync($"window.requestStatus = 0; fetch('{path}', {{ cache: 'no-store' }}).then(res => {{ window.requestStatus = res.status; }});");
        await WaitFor(web, "window.requestStatus !== 0");
        return int.Parse(await web.ExecuteScriptAsync("window.requestStatus"));
    }

    private static async Task WaitFor(CoreWebView2 web, string condition)
    {
        var watch = Stopwatch.StartNew();
        while (await web.ExecuteScriptAsync(condition) != "true")
        {
            Assert(watch.Elapsed < TimeSpan.FromSeconds(5), $"Timed out checking {condition}.");
            await Task.Delay(50);
        }
    }

    private static void Assert(bool passed, string message)
    {
        if (!passed)
            throw new InvalidOperationException(message);
    }
}
