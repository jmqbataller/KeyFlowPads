using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace KeyFlowPads.App;

internal sealed class YouTubeAdBlocker(CoreWebView2 browser, bool enabled)
{
    private static readonly string[] AdDomains =
        ["doubleclick.net", "googlesyndication.com", "googleadservices.com", "adservice.google.com", "ads.youtube.com", "2mdn.net"];
    private static readonly string[] AdPaths = ["/pagead", "/api/stats/ads", "/get_midroll_info"];
    internal bool Enabled { get; set; } = enabled;

    internal async Task AttachAsync()
    {
        await browser.AddScriptToExecuteOnDocumentCreatedAsync(PageScript);
        foreach (string domain in AdDomains)
        {
            AddFilter($"*://{domain}/*");
            AddFilter($"*://*.{domain}/*");
        }
        foreach (string root in new[] { "youtube.com", "*.youtube.com" })
        {
            foreach (string path in AdPaths)
                AddFilter($"*://{root}{path}*");
        }
        browser.WebResourceRequested += (_, args) =>
        {
            if (Enabled && IsAdRequest(args.Request.Uri))
                args.Response = browser.Environment.CreateWebResourceResponse(
                    null, 204, "No Content", "Cache-Control: no-store");
        };
        browser.DOMContentLoaded += async (_, _) => await ApplyPageAsync();
    }

    private void AddFilter(string pattern) => browser.AddWebResourceRequestedFilter(pattern,
        CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);

    internal static bool IsAdRequest(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            return false;
        string host = uri.IdnHost.TrimEnd('.');
        if (AdDomains.Any(domain => IsDomain(host, domain)))
            return true;
        return IsDomain(host, "youtube.com") && AdPaths.Any(path =>
            uri.AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    internal async Task ApplyPageAsync()
    {
        try
        {
            await browser.ExecuteScriptAsync(Enabled
                ? "globalThis.keyFlowYouTubeAds?.(true)" : "globalThis.keyFlowYouTubeAds?.(false)");
        }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        {
            // The document may have closed or navigated. Its replacement receives DOMContentLoaded.
        }
    }

    // This script is scoped to the visible YouTube page. The player still controls
    // when its own Skip button becomes available.
    private const string PageScript = """
        (() => {
            if (top !== self || !['www.youtube.com', 'm.youtube.com', 'youtube.com'].includes(location.hostname)) return;
            const ads = new CSSStyleSheet();
            ads.replaceSync(`
                ytd-ad-slot-renderer, ytd-in-feed-ad-layout-renderer,
                ytd-promoted-sparkles-web-renderer, ytd-promoted-video-renderer,
                ytd-display-ad-renderer, yt-mealbar-promo-renderer,
                #player-ads, #masthead-ad, .ytp-ad-overlay-container,
                .ytp-ad-image-overlay { display: none !important; }
            `);
            let timer;
            let lastSkipped;

            const skipWhenAvailable = () => {
                if (document.hidden) return;
                const button = document.querySelector(
                    '#movie_player.ad-showing button.ytp-skip-ad-button,' +
                    '#movie_player.ad-showing button.ytp-ad-skip-button,' +
                    '#movie_player.ad-showing button.ytp-ad-skip-button-modern'
                );
                if (!button || button.disabled || button.getAttribute('aria-disabled') === 'true' ||
                    button.getClientRects().length === 0 ||
                    button.checkVisibility?.({ checkOpacity: true, checkVisibilityCSS: true }) === false) {
                    lastSkipped = undefined;
                    return;
                }
                if (button === lastSkipped) return;
                lastSkipped = button;
                button.click();
            };

            globalThis.keyFlowYouTubeAds = enabled => {
                if (enabled && timer === undefined) {
                    document.adoptedStyleSheets = [...document.adoptedStyleSheets, ads];
                    timer = setInterval(skipWhenAvailable, 500);
                } else if (!enabled && timer !== undefined) {
                    document.adoptedStyleSheets = document.adoptedStyleSheets.filter(sheet => sheet !== ads);
                    clearInterval(timer);
                    timer = lastSkipped = undefined;
                }
            };
        })();
        """;
}
