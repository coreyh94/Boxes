using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace DesktopBoxes;

/// <summary>Shared look for every Boxes menu: rounded Windows 11 popup, Fluent glyphs, accent-tinted hover.</summary>
internal static class Menus
{
    internal static readonly Color Back = Color.FromArgb(27, 32, 43), Line = Color.FromArgb(46, 54, 70);
    internal static readonly Color Danger = Color.FromArgb(240, 150, 168);
    internal static readonly Font Font = new("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
    static readonly string GlyphFont = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    internal static ContextMenuStrip Create(Color accent)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new MenuRenderer { Accent = accent }, BackColor = Back, ForeColor = Theme.Ink, Font = Font,
            ShowImageMargin = true, ImageScalingSize = new Size(16, 16), DropShadowEnabled = false,
        };
        menu.HandleCreated += (_, _) =>
        {
            // Native rounded corners, shadow and a subtle border on Windows 11; ignored on older versions.
            int round = 2, border = ColorTranslator.ToWin32(Line);
            DwmSetWindowAttribute(menu.Handle, 33, ref round, 4); DwmSetWindowAttribute(menu.Handle, 34, ref border, 4);
        };
        return menu;
    }
    internal static void SetAccent(ContextMenuStrip menu, Color accent) { if (menu.Renderer is MenuRenderer r) { r.Accent = accent; menu.Invalidate(); } }

    internal static ToolStripMenuItem Item(string text, char glyph, Action click, bool danger = false)
    {
        var item = new ToolStripMenuItem(text, Glyph(glyph, danger ? Danger : Theme.Muted), (_, _) => click()) { Padding = new Padding(0, 5, 24, 5) };
        if (danger) item.ForeColor = Danger;
        item.Disposed += (_, _) => item.Image?.Dispose();
        return item;
    }
    /// <summary>Smoke-test render without showing the popup on screen; highlights one item to show hover styling.</summary>
    internal static void RenderPreview(ContextMenuStrip menu, string file, int hovered = -1)
    {
        menu.Opacity = 0; menu.Show(Point.Empty); Application.DoEvents(); if (hovered >= 0) menu.Items[hovered].Select();
        using var bmp = new Bitmap(menu.Width, menu.Height); menu.DrawToBitmap(bmp, new Rectangle(Point.Empty, menu.Size)); bmp.Save(file); menu.Close(); menu.Opacity = 1;
    }
    internal static ToolStripSeparator Separator() => new() { AutoSize = false, Height = 11 };
    /// <summary>Breathing room at the top and bottom; the dropdown overrides its own vertical padding.</summary>
    internal static ToolStripSeparator Spacer() => new() { AutoSize = false, Height = 4 };

    internal static Bitmap Glyph(char glyph, Color colour)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp); g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(GlyphFont, 14, FontStyle.Regular, GraphicsUnit.Pixel); using var brush = new SolidBrush(colour);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, 16, 16), format);
        return bmp;
    }

    /// <summary>Segoe Fluent Icons code points used by the menus.</summary>
    internal static class Icons
    {
        internal const char Add = '', Show = '', Hide = '', Folder = '', Settings = '', Exit = '';
        internal const char Rename = '', Delete = '', Open = '', Reveal = '', Remove = '';
    }
}

internal sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    internal Color Accent = Color.FromArgb(0x73, 0x9F, 0xF4);
    internal MenuRenderer() { RoundedEdges = false; }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Menus.Back);
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Dropdown menus lay items out wider than the popup, so size the pill from the popup itself.
        var width = Math.Min(e.Item.Width, (e.ToolStrip?.Width ?? e.Item.Width) - e.Item.Bounds.X);
        using var path = Theme.Round(new RectangleF(6, 1, width - 12, e.Item.Height - 2), 6);
        var danger = e.Item.ForeColor == Menus.Danger;
        using var brush = new SolidBrush(Theme.Mix(Menus.Back, danger ? Menus.Danger : Accent, danger ? .16f : .22f)); e.Graphics.FillPath(brush, path);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        if (e.Item.Height < 8) return; // spacer
        using var pen = new Pen(Menus.Line); int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 14, y, e.Item.Width - 14, y);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripMenuItem) { var r = e.TextRectangle; r.Offset(8, 0); e.TextRectangle = r; }
        e.TextColor = !e.Item.Enabled ? Theme.Mix(Menus.Back, Theme.Muted, .6f) : e.Item.ForeColor == Menus.Danger ? Menus.Danger : Theme.Ink;
        base.OnRenderItemText(e);
    }
    // Menu items ignore the dropdown's padding, so glyphs are nudged in line with the custom rows.
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { if (e.Image != null) e.Graphics.DrawImage(e.Image, new Rectangle(e.ImageRectangle.X + 10, e.ImageRectangle.Y, 16, 16)); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Muted; base.OnRenderArrow(e); }
}

/// <summary>Row of colour swatches; the current colour is ringed and the hovered one is named.</summary>
internal sealed class SwatchRow : ToolStripItem
{
    internal static readonly (string Name, Color Colour)[] Palette =
        [.. new[] { ("Ocean", 0x739FF4), ("Mint", 0x65C8AD), ("Lavender", 0xAC91ED), ("Rose", 0xE796AE), ("Amber", 0xDFBA78), ("Slate", 0x97ABC4) }
            .Select(p => (p.Item1, Color.FromArgb(255, Color.FromArgb(p.Item2))))];
    const int Left = 16, Pitch = 32, Diameter = 20, Top = 30;
    internal Color Current;
    internal event Action<Color>? Picked;
    int hover = -1;
    internal SwatchRow() { AutoSize = false; Size = GetPreferredSize(Size.Empty); Margin = new Padding(-30, 0, 0, 0); }
    public override Size GetPreferredSize(Size constrainingSize) => new(Left * 2 + Pitch * (Palette.Length - 1) + Diameter + 8, 62);
    int HitTest(Point p) { for (int i = 0; i < Palette.Length; i++) if (Slot(i, 4).Contains(p)) return i; return -1; }
    static Rectangle Slot(int i, int grow = 0) => Rectangle.Inflate(new Rectangle(Left + 4 + i * Pitch, Top, Diameter, Diameter), grow, grow);
    protected override void OnMouseMove(MouseEventArgs e) { var h = HitTest(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var i = HitTest(e.Location); if (i < 0) return;
        Current = Palette[i].Colour; Picked?.Invoke(Current); (Owner as ToolStripDropDown)?.Close(ToolStripDropDownCloseReason.ItemClicked);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var label = hover >= 0 ? Palette[hover].Name : Palette.FirstOrDefault(p => p.Colour.ToArgb() == Current.ToArgb()).Name ?? "";
        TextRenderer.DrawText(g, "Colour", Theme.Small, new Point(Left, 8), Theme.Muted);
        TextRenderer.DrawText(g, label, Theme.Small, new Rectangle(0, 8, Width - Left, 16), Theme.Ink, TextFormatFlags.Right);
        for (int i = 0; i < Palette.Length; i++)
        {
            var colour = Palette[i].Colour; bool current = colour.ToArgb() == Current.ToArgb();
            if (current) { using var ring = new Pen(colour, 2); g.DrawEllipse(ring, Slot(i, 4)); }
            using var brush = new SolidBrush(i == hover ? Theme.Mix(colour, Color.White, .18f) : colour); g.FillEllipse(brush, Slot(i, i == hover && !current ? 1 : 0));
        }
    }
}

/// <summary>Non-interactive menu title: app mark, name and a short summary line.</summary>
internal sealed class MenuHeader : ToolStripItem
{
    readonly Func<string> detail;
    static readonly Font HeaderFont = new("Segoe UI Semibold", 14, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Bitmap mark = AppIcon.Draw(28);
    internal MenuHeader(Func<string> detail) { this.detail = detail; AutoSize = false; Size = new Size(220, 50); Enabled = false; Margin = new Padding(-30, 0, 0, 0); }
    public override Size GetPreferredSize(Size constrainingSize) => new(220, 50);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImage(mark, 14, 9, 28, 28);
        TextRenderer.DrawText(e.Graphics, "Boxes", HeaderFont, new Point(50, 6), Theme.Ink);
        TextRenderer.DrawText(e.Graphics, detail(), Theme.Small, new Point(51, 25), Theme.Muted);
    }
    protected override void Dispose(bool disposing) { if (disposing) mark.Dispose(); base.Dispose(disposing); }
}
