using System.Drawing.Drawing2D;
using static DesktopBoxes.Theme;

namespace DesktopBoxes;

/// <summary>
/// Clips a tile panel and scrolls it by offset, with a slim overlay thumb instead of the native scrollbar
/// (whose track can't be coloured). Mouse wheel anywhere over the box scrolls it.
/// </summary>
internal sealed class ScrollHost : Panel
{
    internal readonly TilePanel Content;
    readonly ScrollThumb thumb;
    internal int Offset { get; private set; }
    internal int Max { get; private set; }
    /// <summary>Whether the thumb is set to show (independent of the form being visible).</summary>
    internal bool ThumbShown => thumb.Visible;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color Accent { get => thumb.Accent; set { thumb.Accent = value; thumb.Invalidate(); } }

    internal ScrollHost(TilePanel content)
    {
        Content = content; DoubleBuffered = true;
        thumb = new ScrollThumb(this);
        Controls.Add(content); Controls.Add(thumb); thumb.BringToFront();
        content.ControlAdded += (_, _) => Relayout(); content.ControlRemoved += (_, _) => Relayout();
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }
    protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); thumb.BackColor = BackColor; }

    /// <summary>Sizes the content to its wrapped height at the current width and re-clamps the offset.</summary>
    internal void Relayout()
    {
        int width = ClientSize.Width, height = ClientSize.Height; if (width <= 0 || height <= 0) return;
        // Lay out at the real width, then measure where the tiles actually end; GetPreferredSize overestimates and
        // left a phantom few-pixel overflow (and a scrollbar) on boxes that fit. Hidden bottom padding alone doesn't count.
        Content.Bounds = new Rectangle(0, -Offset, width, Math.Max(height, Content.Height)); Content.PerformLayout();
        int tilesEnd = Content.Controls.Count == 0 ? 0 : Content.Controls.Cast<Control>().Max(c => c.Bottom + c.Margin.Bottom);
        int overflow = tilesEnd - height;
        Max = overflow > 0 ? overflow + Content.Padding.Bottom : 0; Offset = Math.Clamp(Offset, 0, Max);
        Content.Bounds = new Rectangle(0, -Offset, width, height + Max);
       thumb.Bounds = new Rectangle(width - S(8), 0, S(8), height); // fits inside the tile panel's right padding
        thumb.Visible = Max > 0; thumb.Invalidate();
    }
    internal void ScrollTo(int value)
    {
        value = Math.Clamp(value, 0, Max); if (value == Offset) return;
        Offset = value; Content.Top = -Offset; thumb.Invalidate(); Content.Update();
    }
    /// <summary>Wheel messages bubble up from tiles and the panel to here.</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(Offset - e.Delta * S(56) / 120);
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        thumb.Wake();
    }
}

/// <summary>Rounded thumb with no track: faint at rest, brighter and wider on hover or drag.</summary>
internal sealed class ScrollThumb : Control
{
    readonly ScrollHost host;
    internal Color Accent = Color.SteelBlue;
    bool hover, dragging, awake; int dragStartY, dragStartOffset;
    readonly System.Windows.Forms.Timer sleep = new() { Interval = 900 };
    internal ScrollThumb(ScrollHost host)
    {
        this.host = host; Cursor = Cursors.Default;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        sleep.Tick += (_, _) => { sleep.Stop(); awake = false; Invalidate(); };
    }
    /// <summary>Briefly brighten after wheel scrolling so you can see where you are.</summary>
    internal void Wake() { awake = true; sleep.Stop(); sleep.Start(); Invalidate(); }
    int Track => Height - S(8);
    int ThumbHeight => Math.Max(S(28), Track * host.ClientSize.Height / Math.Max(1, host.ClientSize.Height + host.Max));
    Rectangle ThumbRect
    {
        get
        {
            int y = S(4) + (host.Max == 0 ? 0 : (Track - ThumbHeight) * host.Offset / host.Max), w = hover || dragging ? S(6) : S(4);
            return new Rectangle(Width - w - S(1), y, w, ThumbHeight);
        }
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var rect = ThumbRect; rect.Inflate(S(4), 0);
        // Clicking the empty strip jumps so the thumb centres on the pointer, then drags from there.
        if (!rect.Contains(e.Location)) host.ScrollTo((e.Y - S(4) - ThumbHeight / 2) * host.Max / Math.Max(1, Track - ThumbHeight));
        dragging = true; dragStartY = e.Y; dragStartOffset = host.Offset; Capture = true; Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging) host.ScrollTo(dragStartOffset + (e.Y - dragStartY) * host.Max / Math.Max(1, Track - ThumbHeight));
        base.OnMouseMove(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
       if (host.Max <= 0) return; // nothing to scroll: never draw, whatever the visibility state
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ThumbRect;
        using var path = Round(new RectangleF(r.X, r.Y, r.Width, r.Height), r.Width / 2f);
        float strength = dragging ? .6f : hover ? .5f : awake ? .4f : .22f;
        using var brush = new SolidBrush(Mix(BackColor, Mix(InkOn(BackColor), Accent, .35f), strength)); e.Graphics.FillPath(brush, path);
    }
    protected override void Dispose(bool disposing) { if (disposing) sleep.Dispose(); base.Dispose(disposing); }
}
