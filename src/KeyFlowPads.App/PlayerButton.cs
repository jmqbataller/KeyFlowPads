using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace KeyFlowPads.App;

internal enum ButtonKind { Plain, Pad, Play, Stop, Settings, Compact, Hide, IconPlay, IconPause, IconStop }

internal sealed class PlayerButton : Button
{
    private bool hovering;
    private bool down;
    private bool selected;
    private bool sounding;

    internal PlayerButton(string text, ButtonKind kind, Font font)
    {
        Text = text;
        AccessibleName = text;
        Kind = kind;
        Font = font;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal ButtonKind Kind { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Playing { get; set; }

    internal void SetPadState(bool isSelected, bool isSounding)
    {
        if (selected == isSelected && sounding == isSounding)
            return;
        selected = isSelected;
        sounding = isSounding;
        AccessibleDescription = selected ? sounding ? "Selected and sounding" : "Selected" : sounding ? "Sounding" : null;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); down = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float s = DeviceDpi / 96f;
        bool primary = Kind is ButtonKind.Play or ButtonKind.IconPlay or ButtonKind.IconPause;
        bool stop = Kind is ButtonKind.Stop or ButtonKind.IconStop;
        bool pad = Kind == ButtonKind.Pad;
        bool utility = Kind is ButtonKind.Settings or ButtonKind.Compact or ButtonKind.Hide;
        Color fill = !Enabled ? Theme.Surface : primary ? Theme.Teal :
            sounding ? Color.FromArgb(17, 59, 58) : selected ? Color.FromArgb(26, 48, 74) :
            utility ? Theme.Background : Theme.Surface;
        Color edge = !Enabled ? Theme.Control : sounding || primary ? Theme.Teal :
            selected ? Theme.Blue : utility ? Theme.Control : Theme.Border;
        if (Enabled && hovering)
            fill = ControlPaint.Light(fill, down ? 0.20f : 0.10f);
        Color ink = !Enabled ? Theme.Muted : primary ? Theme.Background : stop ? Theme.Pink :
            sounding ? Theme.Teal : Theme.Text;
        using var line = new Pen(edge, (selected || sounding ? 1.5f : 1f) * s);
        using var brush = new SolidBrush(fill);
        using var text = new SolidBrush(ink);
        using var path = Theme.Rounded(new RectangleF(s, s, Width - 2 * s, Height - 2 * s), 8 * s);
        g.FillPath(brush, path);
        g.DrawPath(line, path);

        if (utility || Kind is ButtonKind.IconPlay or ButtonKind.IconPause or ButtonKind.IconStop)
            Glyph(g, Width / 2f, Height / 2f, s, ink);
        else
        {
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            float start = primary || stop ? 12 * s : 0;
            g.DrawString(Text, Font, text, new RectangleF(start, 0, Width - start, Height), format);
            if (primary || stop)
                Glyph(g, Width / 2f - (Text.Length > 4 ? 27 : 22) * s, Height / 2f, s * 0.8f, ink);
        }

        if (pad && (sounding || selected))
        {
            using var indicator = new SolidBrush(sounding ? Theme.Teal : Theme.Blue);
            g.FillEllipse(indicator, Width - 11 * s, 7 * s, 4 * s, 4 * s);
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -(int)(4 * s), -(int)(4 * s)), ink, fill);
    }

    private void Glyph(Graphics g, float x, float y, float scale, Color color)
    {
        var state = g.Save();
        g.TranslateTransform(x, y);
        g.ScaleTransform(scale, scale);
        using var stroke = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var ink = new SolidBrush(color);
        switch (Kind)
        {
            case ButtonKind.Settings:
                for (int row = -1; row <= 1; row++)
                {
                    float h = 4 * row;
                    float p = row == 0 ? 2.5f : -2.5f;
                    g.DrawLine(stroke, -6, h, 6, h);
                    g.FillEllipse(ink, p - 1.8f, h - 1.8f, 3.6f, 3.6f);
                }
                break;
            case ButtonKind.Compact:
                g.DrawRectangle(stroke, -6, -4.5f, 12, 9);
                g.DrawLine(stroke, -3, 0, 3, 0);
                if (Playing)
                    g.DrawLine(stroke, 0, -2.5f, 0, 2.5f);
                break;
            case ButtonKind.Hide:
                g.DrawLine(stroke, -5, 2, 5, 2);
                break;
            case ButtonKind.Stop:
            case ButtonKind.IconStop:
                g.FillRectangle(ink, -4, -4, 8, 8);
                break;
            case ButtonKind.IconPause:
            case ButtonKind.Play when Playing:
                g.FillRectangle(ink, -4, -5, 2.5f, 10);
                g.FillRectangle(ink, 1.5f, -5, 2.5f, 10);
                break;
            default:
                g.FillPolygon(ink, [new PointF(-3.5f, -5.5f), new PointF(5, 0), new PointF(-3.5f, 5.5f)]);
                break;
        }
        g.Restore(state);
    }
}
