using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using static DesktopBoxes.Theme;

namespace DesktopBoxes;

/// <summary>Shared look for every Boxes menu: rounded Windows 11 popup, Fluent glyphs, accent-tinted hover. Sizes scale with display DPI.</summary>
internal static class Menus
{
    internal static readonly Color Back = Color.FromArgb(27, 32, 43), Line = Color.FromArgb(46, 54, 70);
    internal static readonly Color Danger = Color.FromArgb(240, 150, 168);
    internal static readonly Font Font = new("Segoe UI", 13 * Theme.Scale, FontStyle.Regular, GraphicsUnit.Pixel);
    static readonly string GlyphFont = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    /// <summary>Custom rows (swatches, header) sit in the image margin the dropdown reserves on the left; this pulls them back to the edge.</summary>
    internal static Padding CustomRowMargin => new(-(S(16) + 14), 0, 0, 0);

    internal static ContextMenuStrip Create(Color accent)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new MenuRenderer { Accent = accent }, BackColor = Back, ForeColor = Ink, Font = Font,
            ShowImageMargin = true, ImageScalingSize = new Size(S(16), S(16)), DropShadowEnabled = false,
        };
        menu.HandleCreated += (_, _) =>
        {
            // Native rounded corners, shadow and a subtle border on Windows 11; ignored on older versions.
            int round = 2, border = ColorTranslator.ToWin32(Line);
            Native.DwmSetWindowAttribute(menu.Handle, 33, ref round, 4); Native.DwmSetWindowAttribute(menu.Handle, 34, ref border, 4);
        };
        return menu;
    }
    internal static void SetAccent(ContextMenuStrip menu, Color accent) { if (menu.Renderer is MenuRenderer r) { r.Accent = accent; menu.Invalidate(); } }

    internal static ToolStripMenuItem Item(string text, char glyph, Action click, bool danger = false)
    {
        var item = new ToolStripMenuItem(text, Glyph(glyph, danger ? Danger : Muted), (_, _) => click()) { Padding = new Padding(0, S(5), S(24), S(5)) };
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
    /// <summary>Item that opens a styled dropdown of choices; the chosen one shows a tick (see <see cref="Tick"/>).</summary>
    internal static ToolStripMenuItem Submenu(string text, char glyph, params (string Text, Action Click)[] choices)
    {
        var item = Item(text, glyph, () => { });
        var drop = Create(Color.SteelBlue); item.DropDown = drop; item.Disposed += (_, _) => drop.Dispose();
        drop.Items.Add(Spacer());
        foreach (var (choice, click) in choices) drop.Items.Add(Item(choice, Icons.Check, click));
        drop.Items.Add(Spacer());
        return item;
    }
    /// <summary>Ticks the choice matching <paramref name="index"/>; the others keep their glyph space so text stays aligned.</summary>
    internal static void Tick(ToolStripMenuItem submenu, int index)
    {
        var choices = submenu.DropDownItems.OfType<ToolStripMenuItem>().ToArray();
        for (int i = 0; i < choices.Length; i++) choices[i].DisplayStyle = i == index ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text;
    }
    internal static ToolStripSeparator Separator() => new() { AutoSize = false, Height = S(11) };
    /// <summary>Breathing room at the top and bottom; the dropdown overrides its own vertical padding.</summary>
    internal static ToolStripSeparator Spacer() => new() { AutoSize = false, Height = S(4) };

    internal static Bitmap Glyph(char glyph, Color colour, int size = 0)
    {
        if (size == 0) size = S(16);
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias;
        // Centre the glyph's actual outline; icon fonts sit high in their line box.
        using var family = new FontFamily(GlyphFont); using var path = new GraphicsPath(); using var brush = new SolidBrush(colour);
        path.AddString(glyph.ToString(), family, 0, size * .875f, PointF.Empty, StringFormat.GenericTypographic);
        var b = path.GetBounds(); using var shift = new Matrix(); shift.Translate((size - b.Width) / 2 - b.X, (size - b.Height) / 2 - b.Y); path.Transform(shift);
        g.FillPath(brush, path);
        return bmp;
    }

    /// <summary>Segoe Fluent Icons code points used by the menus.</summary>
    internal static class Icons
    {
        internal const char Update = '', Cut = '', Copy = '', Paste = '', Link = '', Info = '', Shield = '', OpenWith = '';
        internal const char Timer = '', Check = '';
        internal const char Add = '', Show = '', Hide = '', Folder = '', Settings = '', Exit = '';
        internal const char Rename = '', Delete = '', Open = '', Reveal = '', Remove = '', More = '', Photo = '', Slideshow = '', Next = '';
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
        using var path = Round(new RectangleF(S(6), 1, width - S(12), e.Item.Height - 2), S(6));
        var danger = e.Item.ForeColor == Menus.Danger;
        using var brush = new SolidBrush(Mix(Menus.Back, danger ? Menus.Danger : Accent, danger ? .16f : .22f)); e.Graphics.FillPath(brush, path);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        if (e.Item.Height < S(8)) return; // spacer
        using var pen = new Pen(Menus.Line); int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, S(14), y, e.Item.Width - S(14), y);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripMenuItem) { var r = e.TextRectangle; e.TextRectangle = new Rectangle(r.X + S(8), 0, r.Width, e.Item.Height); e.TextFormat = (e.TextFormat & ~(TextFormatFlags.Top | TextFormatFlags.Bottom)) | TextFormatFlags.VerticalCenter; }
        e.TextColor = !e.Item.Enabled ? Mix(Menus.Back, Muted, .6f) : e.Item.ForeColor == Menus.Danger ? Menus.Danger : Ink;
        base.OnRenderItemText(e);
    }
    // Menu items ignore the dropdown's padding, so glyphs are nudged in line with the custom rows.
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { if (e.Image != null) e.Graphics.DrawImage(e.Image, new Rectangle(e.ImageRectangle.X + S(10), (e.Item.Height - S(16)) / 2, S(16), S(16))); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Muted; base.OnRenderArrow(e); }
}

/// <summary>Row of colour swatches; the current colour is ringed and the hovered one is named.</summary>
internal sealed class SwatchRow : ToolStripItem
{
    internal static readonly (string Name, Color Colour)[] Palette =
        [.. new[] { ("Ocean", 0x739FF4), ("Mint", 0x65C8AD), ("Lavender", 0xAC91ED), ("Rose", 0xE796AE), ("Amber", 0xDFBA78), ("Slate", 0x97ABC4) }
            .Select(p => (p.Item1, Color.FromArgb(255, Color.FromArgb(p.Item2))))];
    static readonly int Left = S(16), Pitch = S(32), Diameter = S(20), Top = S(30);
    internal Color Current;
    internal event Action<Color>? Picked;
    int hover = -1;
    internal SwatchRow() { AutoSize = false; Size = GetPreferredSize(Size.Empty); Margin = Menus.CustomRowMargin; }
    public override Size GetPreferredSize(Size constrainingSize) => new(Left * 2 + Pitch * (Palette.Length - 1) + Diameter + S(8), S(62));
    int HitTest(Point p) { for (int i = 0; i < Palette.Length; i++) if (Slot(i, S(4)).Contains(p)) return i; return -1; }
    static Rectangle Slot(int i, int grow = 0) => Rectangle.Inflate(new Rectangle(Left + S(4) + i * Pitch, Top, Diameter, Diameter), grow, grow);
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
        TextRenderer.DrawText(g, "Colour", Small, new Point(Left, S(8)), Muted);
        TextRenderer.DrawText(g, label, Small, new Rectangle(0, S(8), Width - Left, S(16)), Ink, TextFormatFlags.Right);
        for (int i = 0; i < Palette.Length; i++)
        {
            var colour = Palette[i].Colour; bool current = colour.ToArgb() == Current.ToArgb();
            if (current) { using var ring = new Pen(colour, S(2)); g.DrawEllipse(ring, Slot(i, S(4))); }
            using var brush = new SolidBrush(i == hover ? Mix(colour, Color.White, .18f) : colour); g.FillEllipse(brush, Slot(i, i == hover && !current ? S(1) : 0));
        }
    }
}

/// <summary>Non-interactive menu title: app mark, name and a short summary line.</summary>
internal sealed class MenuHeader : ToolStripItem
{
    readonly Func<string> detail;
    static readonly Font HeaderFont = new("Segoe UI Semibold", 14 * Theme.Scale, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Bitmap mark = AppIcon.Draw(S(28));
    internal MenuHeader(Func<string> detail) { this.detail = detail; AutoSize = false; Size = GetPreferredSize(Size.Empty); Enabled = false; Margin = Menus.CustomRowMargin; }
    public override Size GetPreferredSize(Size constrainingSize) => new(S(220), S(50));
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImage(mark, S(14), S(9), S(28), S(28));
        TextRenderer.DrawText(e.Graphics, "Boxes", HeaderFont, new Point(S(50), S(6)), Ink);
        TextRenderer.DrawText(e.Graphics, detail(), Small, new Point(S(51), S(25)), Muted);
    }
    protected override void Dispose(bool disposing) { if (disposing) mark.Dispose(); base.Dispose(disposing); }
}

/// <summary>Shared look of the opacity sliders (box menu row and Settings): label and value on top, rounded track below.</summary>
internal static class Slider
{
    internal const int Min = 30, Max = 100, Step = 5;
    static readonly int Pad = S(16), TrackTop = S(30), TrackHeight = S(18);
    internal static int Height => S(56);
    static Rectangle Track(Rectangle bounds) => new(bounds.Left + Pad + S(4), bounds.Top + TrackTop, bounds.Width - Pad * 2 - S(8), TrackHeight);
    internal static int ValueAt(Rectangle bounds, int x)
    {
        var track = Track(bounds); float t = Math.Clamp((x - track.Left) / (float)track.Width, 0, 1);
        return Math.Clamp((int)Math.Round((Min + t * (Max - Min)) / Step) * Step, Min, Max);
    }
    internal static void Paint(Graphics g, Rectangle bounds, string label, int value, Color accent, Color back, bool hover)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g, label, Small, new Point(bounds.Left + Pad, bounds.Top + S(8)), Muted);
        TextRenderer.DrawText(g, value + "%", Small, new Rectangle(bounds.Left, bounds.Top + S(8), bounds.Width - Pad, S(16)), Ink, TextFormatFlags.Right);
        var track = Track(bounds); float h = S(4), y = track.Top + track.Height / 2f;
        float at = track.Left + track.Width * (value - Min) / (float)(Max - Min), d = S(hover ? 16 : 14);
        using (var rail = Round(new RectangleF(track.Left, y - h / 2, track.Width, h), h / 2)) using (var brush = new SolidBrush(Mix(back, Ink, .16f))) g.FillPath(brush, rail);
        if (at - track.Left >= h) using (var fill = Round(new RectangleF(track.Left, y - h / 2, at - track.Left, h), h / 2)) using (var brush = new SolidBrush(accent)) g.FillPath(brush, fill);
        using (var thumb = new SolidBrush(Ink)) g.FillEllipse(thumb, at - d / 2, y - d / 2, d, d);
        using (var ring = new Pen(accent, S(2))) g.DrawEllipse(ring, at - d / 2, y - d / 2, d, d);
    }
}

/// <summary>Opacity slider row in a box's menu. Changed fires while dragging; Committed once on release.</summary>
internal sealed class OpacityRow : ToolStripItem
{
    internal int Value = Slider.Max; internal Color Accent = Color.FromArgb(0x73, 0x9F, 0xF4);
    internal event Action<int>? Changed, Committed;
    bool dragging, hover;
    internal OpacityRow() { AutoSize = false; Size = GetPreferredSize(Size.Empty); Margin = Menus.CustomRowMargin; }
    public override Size GetPreferredSize(Size constrainingSize) => new(new SwatchRow().GetPreferredSize(Size.Empty).Width, Slider.Height);
    void Set(int x) { var v = Slider.ValueAt(new Rectangle(Point.Empty, Size), x); if (v != Value) { Value = v; Changed?.Invoke(v); Invalidate(); } }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; Set(e.X); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) Set(e.X); if (!hover) { hover = true; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (dragging) { dragging = false; Committed?.Invoke(Value); } base.OnMouseUp(e); }
    protected override void OnMouseLeave(EventArgs e) { if (dragging) { dragging = false; Committed?.Invoke(Value); } hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e) => Slider.Paint(e.Graphics, new Rectangle(Point.Empty, Size), "Opacity", Value, Accent, Menus.Back, hover);
}

/// <summary>The same slider as a standalone control, for the Settings window. Arrow keys step by 5%.</summary>
internal sealed class SliderControl : Control
{
    internal string Label = ""; internal Color Accent = Color.FromArgb(0x73, 0x9F, 0xF4);
    int current = Slider.Max; bool dragging, hover;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int Value { get => current; set { value = Math.Clamp(value, Slider.Min, Slider.Max); if (current != value) { current = value; Changed?.Invoke(value); Invalidate(); } } }
    internal event Action<int>? Changed, Committed;
    internal SliderControl() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true); Height = Slider.Height; Cursor = Cursors.Hand; TabStop = true; }
    void Set(int x) => Value = Slider.ValueAt(ClientRectangle, x);
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Focus(); dragging = true; Capture = true; Set(e.X); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) Set(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (dragging) { dragging = false; Capture = false; Committed?.Invoke(Value); } base.OnMouseUp(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Left or Keys.Right) { Value += e.KeyCode == Keys.Right ? Slider.Step : -Slider.Step; Committed?.Invoke(Value); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Parent?.BackColor ?? BackColor); Slider.Paint(e.Graphics, ClientRectangle, Label, Value, Accent, Parent?.BackColor ?? BackColor, hover || Focused); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}
