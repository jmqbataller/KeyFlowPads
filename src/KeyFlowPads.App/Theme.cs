using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;

namespace KeyFlowPads.App;

internal sealed class Theme : IDisposable
{
    internal static readonly Color Background = Color.FromArgb(16, 20, 29);
    internal static readonly Color Surface = Color.FromArgb(24, 31, 44);
    internal static readonly Color Control = Color.FromArgb(31, 40, 56);
    internal static readonly Color Border = Color.FromArgb(59, 71, 93);
    internal static readonly Color Text = Color.FromArgb(248, 250, 252);
    internal static readonly Color Muted = Color.FromArgb(163, 178, 200);
    internal static readonly Color Teal = Color.FromArgb(0, 229, 191);
    internal static readonly Color Blue = Color.FromArgb(98, 177, 255);
    internal static readonly Color Pink = Color.FromArgb(255, 117, 187);
    private readonly PrivateFontCollection fonts = new();
    private readonly Dictionary<(float, bool), Font> cache = [];
    private readonly Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KeyFlowPads.Icon")!;
    private readonly Stream markStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KeyFlowPads.Mark")!;

    internal Theme()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "fonts");
        foreach (string file in new[] { "Inter-Regular.ttf", "Inter-Bold.ttf" })
        {
            string path = Path.Combine(directory, file);
            if (File.Exists(path))
                fonts.AddFontFile(path);
        }
        Icon = new Icon(iconStream);
        Mark = Image.FromStream(markStream);
    }

    internal Icon Icon { get; }
    internal Image Mark { get; }

    internal Font Font(float size, bool bold = false)
    {
        if (!cache.TryGetValue((size, bold), out Font? font))
        {
            var family = fonts.Families.FirstOrDefault();
            var style = bold ? FontStyle.Bold : FontStyle.Regular;
            font = family is null || !family.IsStyleAvailable(style)
                ? new Font("Segoe UI", size, style)
                : new Font(family, size, style);
            cache.Add((size, bold), font);
        }
        return font;
    }

    internal static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        foreach (var font in cache.Values)
            font.Dispose();
        fonts.Dispose();
        Icon.Dispose();
        Mark.Dispose();
        iconStream.Dispose();
        markStream.Dispose();
    }
}
