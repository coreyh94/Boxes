using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using static DesktopBoxes.Theme;

namespace DesktopBoxes;

/// <summary>Body of a photo or slideshow box: the photo cropped to fill, with rounded corners, crossfading on change.</summary>
internal sealed class PhotoPanel : Control
{
    /// <summary>Photos are decoded at most this large; plenty for a desktop card and keeps memory small.</summary>
    const int MaxSide = 2048, FadeMs = 600;
    Bitmap? photo, cache, fading;
    readonly Stopwatch fadeClock = new();
    readonly System.Windows.Forms.Timer fadeTimer = new() { Interval = 15 };
    internal Color Accent = Color.SteelBlue;
    internal string Placeholder = "Photo unavailable — use Change photo…";
    internal bool HasPhoto => photo != null;
    /// <summary>Slideshow boxes show previous, pause/play and next buttons over the photo while the mouse is on it.</summary>
    internal bool ShowControls;
    internal bool Paused, CanGoBack;
    /// <summary>Raised with the button pressed: 0 previous, 1 pause/play, 2 next.</summary>
    internal event Action<int>? ControlPressed;
    bool mouseInside; int hoverButton = -1;
    internal PhotoPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        fadeTimer.Tick += (_, _) => { if (fadeClock.ElapsedMilliseconds >= FadeMs) { fadeTimer.Stop(); fading?.Dispose(); fading = null; } Invalidate(); };
    }

    internal void Load(string? path) => SetPhoto(path == null ? null : Read(path));
    /// <summary>Takes ownership of the bitmap. With fade, the previous photo dissolves into the new one.</summary>
    internal void SetPhoto(Bitmap? image, bool fade = false)
    {
        fading?.Dispose(); fading = null; fadeTimer.Stop();
        if (fade && cache != null && image != null) { fading = cache; cache = null; fadeClock.Restart(); fadeTimer.Start(); }
        photo?.Dispose(); cache?.Dispose(); photo = image; cache = null;
        Invalidate();
    }
    /// <summary>Loads into memory (no file lock), applies the camera's EXIF rotation, and downsizes huge photos. Null if unreadable.</summary>
    internal static Bitmap? Read(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var image = Image.FromStream(stream);
            float scale = Math.Min(1f, (float)MaxSide / Math.Max(image.Width, image.Height));
            var copy = new Bitmap(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
            using (var g = Graphics.FromImage(copy)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(image, 0, 0, copy.Width, copy.Height); }
            if (image.PropertyIdList.Contains(0x0112))
                copy.RotateFlip(BitConverter.ToUInt16(image.GetPropertyItem(0x0112)!.Value!, 0) switch
                {
                    2 => RotateFlipType.RotateNoneFlipX, 3 => RotateFlipType.Rotate180FlipNone, 4 => RotateFlipType.Rotate180FlipX,
                    5 => RotateFlipType.Rotate90FlipX, 6 => RotateFlipType.Rotate90FlipNone, 7 => RotateFlipType.Rotate270FlipX,
                    8 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone,
                });
            return copy;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or ExternalException) { return null; }
    }
    /// <summary>Box size that fits the photo's shape: ~432px wide (narrower for portraits), snapped to the 48px grid.</summary>
    internal static Size BoxSizeFor(string? path, Size screen)
    {
        using var image = path == null ? null : Read(path);
        float aspect = image == null ? 4f / 3 : (float)image.Width / image.Height;
        int header = S(48), inset = S(10), maxHeight = Math.Max(192, screen.Height / 2 / 48 * 48);
        int width = aspect >= 1 ? 432 : 336;
        int height = header + (int)((width - inset * 2) / aspect) + inset;
        if (height > maxHeight) { height = maxHeight; width = (int)((height - header - inset) * aspect) + inset * 2; }
        static int Snap(int v, int min) => Math.Max(min, (int)Math.Round(v / 48d) * 48);
        return new Size(Snap(width, 240), Snap(height, 192));
    }
    /// <summary>Gap above the photo; only needed when the box's title bar is hidden.</summary>
    internal int TopInset;
    Rectangle Frame => new(S(10), TopInset, Math.Max(1, Width - S(20)), Math.Max(1, Height - TopInset - S(10)));
    const int ButtonSize = 34, ButtonGap = 2;
    /// <summary>Draws the buttons without the mouse over the photo, for preview renders.</summary>
    internal bool PreviewControls;
    bool ControlsShown => ShowControls && HasPhoto && (mouseInside || PreviewControls);
    /// <summary>The three round buttons, centred near the bottom of the photo. Empty when the box is too small for them.</summary>
    Rectangle[] Buttons()
    {
        var frame = Frame; int size = S(ButtonSize), gap = S(ButtonGap), pad = S(4);
        int width = size * 3 + gap * 2 + pad * 2, height = size + pad * 2;
        if (frame.Width < width + S(16) || frame.Height < height + S(24)) return [];
        int x = frame.X + (frame.Width - width) / 2 + pad, y = frame.Bottom - S(12) - height + pad;
        return [.. Enumerable.Range(0, 3).Select(i => new Rectangle(x + i * (size + gap), y, size, size))];
    }
    int ButtonAt(Point p) => ControlsShown ? Array.FindIndex(Buttons(), b => b.Contains(p)) : -1;
    void SetHover(int button) { if (button != hoverButton) { hoverButton = button; Invalidate(); } }
    protected override void OnMouseEnter(EventArgs e) { mouseInside = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { mouseInside = false; hoverButton = -1; Invalidate(); base.OnMouseLeave(e); }
    // Presses on a button stay here, so they never start a move, a resize, or a double-click to open the photo.
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!mouseInside) { mouseInside = true; Invalidate(); }
        int button = e.Button == MouseButtons.None ? ButtonAt(e.Location) : -1; SetHover(button);
        if (button >= 0) Cursor = Cursors.Hand; else base.OnMouseMove(e);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        int button = ButtonAt(e.Location);
        if (button < 0) { base.OnMouseDown(e); return; }
        if (e.Button == MouseButtons.Left && (button != 0 || CanGoBack)) ControlPressed?.Invoke(button);
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { if (ButtonAt(e.Location) < 0) base.OnMouseDoubleClick(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (ButtonAt(e.Location) < 0 || Capture) base.OnMouseUp(e); }
    void PaintControls(Graphics g)
    {
        var buttons = Buttons(); int pad = S(4);
        if (buttons.Length == 0) return;
        var bar = Rectangle.FromLTRB(buttons[0].Left - pad, buttons[0].Top - pad, buttons[2].Right + pad, buttons[2].Bottom + pad);
        using (var pill = Round(bar, bar.Height / 2f)) using (var fill = new SolidBrush(Color.FromArgb(150, 20, 20, 24))) g.FillPath(fill, pill);
        char[] glyphs = [Menus.Icons.Previous, Paused ? Menus.Icons.Play : Menus.Icons.Pause, Menus.Icons.Next];
        for (int i = 0; i < 3; i++)
        {
            bool enabled = i != 0 || CanGoBack;
            if (i == hoverButton && enabled) { using var hover = new SolidBrush(Color.FromArgb(60, 255, 255, 255)); g.FillEllipse(hover, buttons[i]); }
            using var glyph = Menus.Glyph(glyphs[i], Color.FromArgb(enabled ? 255 : 90, 255, 255, 255), S(16));
            g.DrawImage(glyph, buttons[i].X + (buttons[i].Width - glyph.Width) / 2, buttons[i].Y + (buttons[i].Height - glyph.Height) / 2);
        }
    }
    /// <summary>A small pause badge in the corner, so a paused slideshow doesn't look stuck.</summary>
    void PaintPausedBadge(Graphics g)
    {
        var frame = Frame; int size = S(26);
        if (frame.Width < size * 3 || frame.Height < size * 3) return;
        var badge = new Rectangle(frame.Right - S(10) - size, frame.Y + S(10), size, size);
        using (var fill = new SolidBrush(Color.FromArgb(150, 20, 20, 24))) g.FillEllipse(fill, badge);
        using var glyph = Menus.Glyph(Menus.Icons.Pause, Color.White, S(12));
        g.DrawImage(glyph, badge.X + (badge.Width - glyph.Width) / 2, badge.Y + (badge.Height - glyph.Height) / 2);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        var frame = Frame; using var path = Round(frame, S(8));
        if (photo == null)
        {
            using var pen = new Pen(Mix(BackColor, Accent, .35f), Math.Max(1, Theme.Scale)) { DashPattern = [4, 3] }; g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Placeholder, Small, Rectangle.Inflate(frame, -S(12), 0), MutedOn(BackColor), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        // Cover-crop once per size; resizing re-renders, painting reuses the cache.
        if (cache == null || cache.Size != frame.Size)
        {
            cache?.Dispose(); cache = new Bitmap(frame.Width, frame.Height);
            using var cg = Graphics.FromImage(cache); cg.InterpolationMode = InterpolationMode.HighQualityBicubic; cg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = Math.Max((float)frame.Width / photo.Width, (float)frame.Height / photo.Height);
            float w = frame.Width / scale, h = frame.Height / scale;
            cg.DrawImage(photo, new Rectangle(Point.Empty, frame.Size), new RectangleF((photo.Width - w) / 2, (photo.Height - h) / 2, w, h), GraphicsUnit.Pixel);
        }
        using (var brush = new TextureBrush(cache, WrapMode.Clamp)) { brush.TranslateTransform(frame.X, frame.Y); g.FillPath(brush, path); }
        if (fading != null)
        {
            // Ease-out dissolve of the previous photo over the new one.
            float t = Math.Min(1f, fadeClock.ElapsedMilliseconds / (float)FadeMs), alpha = 1 - t * t * (3 - 2 * t);
            using var attributes = new ImageAttributes(); attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
            g.SetClip(path); g.DrawImage(fading, frame, 0, 0, fading.Width, fading.Height, GraphicsUnit.Pixel, attributes); g.ResetClip();
        }
        if (ControlsShown) PaintControls(g);
        else if (ShowControls && Paused) PaintPausedBadge(g);
    }
    protected override void Dispose(bool disposing) { if (disposing) { fadeTimer.Dispose(); photo?.Dispose(); cache?.Dispose(); fading?.Dispose(); } base.Dispose(disposing); }
}

/// <summary>
/// Drives a slideshow box: shuffles the photos in a folder (and its subfolders), shows the next one every few seconds,
/// and rescans after each full pass so new photos appear. Decoding runs in the background; it pauses while hidden.
/// Remembers the photos it has shown, so Previous steps back through them and Next then replays them in order.
/// </summary>
internal sealed class PhotoReel : IDisposable
{
    const int MaxPhotos = 5000, MaxHistory = 200;
    readonly PhotoPanel panel; readonly string folder;
    readonly System.Windows.Forms.Timer timer;
    List<string> queue = [];
    /// <summary>Photos shown so far, oldest first; <see cref="position"/> is the one on screen.</summary>
    readonly List<string> history = [];
    int position = -1;
    bool loading, disposed, started;
    internal string? Current { get; private set; }
    internal bool Paused { get; private set; }
    internal bool CanGoBack => position > 0;
    internal bool TimerRunning => timer.Enabled;
    internal PhotoReel(PhotoPanel panel, string folder, int seconds, bool paused = false)
    {
        this.panel = panel; this.folder = folder; Paused = paused;
        timer = new() { Interval = Math.Max(3, seconds) * 1000 };
        timer.Tick += async (_, _) => await NextAsync();
        panel.ShowControls = true; panel.Paused = paused; panel.CanGoBack = false;
    }
    /// <summary>Changing the interval restarts the countdown, so the current photo gets the full new time.</summary>
    internal void SetInterval(int seconds) => timer.Interval = Math.Max(3, seconds) * 1000;
    internal int IntervalSeconds => timer.Interval / 1000;
    internal async Task StartAsync() { panel.Placeholder = "Loading photos…"; panel.Invalidate(); await NextAsync(); started = true; if (!disposed && !Paused) timer.Start(); }
    /// <summary>Pausing keeps the current photo up; playing gives it a fresh full interval before moving on.</summary>
    internal void SetPaused(bool paused)
    {
        Paused = paused; panel.Paused = paused; panel.Invalidate();
        timer.Stop(); if (!paused && started && !disposed) timer.Start();
    }
    /// <summary>Goes back to the photo shown before this one, skipping any deleted since.</summary>
    internal async Task PreviousAsync()
    {
        if (loading || disposed || !panel.Visible) return;
        loading = true;
        try
        {
            while (position > 0)
            {
                var image = await Task.Run(() => PhotoPanel.Read(history[position - 1]));
                if (disposed) { image?.Dispose(); return; }
                if (image == null) { history.RemoveAt(position - 1); position--; continue; }
                position--; Show(history[position], image); return;
            }
            panel.CanGoBack = false; panel.Invalidate();
        }
        finally { loading = false; }
    }
    void Show(string path, Bitmap image)
    {
        Current = path; panel.CanGoBack = CanGoBack; panel.SetPhoto(image, fade: true);
        // A photo picked by hand gets the full interval before the slideshow moves on.
        if (timer.Enabled) { timer.Stop(); timer.Start(); }
    }
    /// <summary>Shows the next photo: forward through ones already seen after going back, otherwise a new one.
    /// Skips work while the box is hidden, unless nothing has been shown yet.</summary>
    internal async Task NextAsync()
    {
        if (loading || disposed || (!panel.Visible && panel.HasPhoto)) return;
        loading = true;
        try
        {
            while (position < history.Count - 1)
            {
                var image = await Task.Run(() => PhotoPanel.Read(history[position + 1]));
                if (disposed) { image?.Dispose(); return; }
                if (image == null) { history.RemoveAt(position + 1); continue; }
                position++; Show(history[position], image); return;
            }
            for (int attempts = 0; attempts < 2; attempts++)
            {
                if (queue.Count == 0) queue = await Task.Run(() => Scan(folder, Current));
                while (queue.Count > 0)
                {
                    var path = queue[^1]; queue.RemoveAt(queue.Count - 1);
                    var image = await Task.Run(() => PhotoPanel.Read(path));
                    if (disposed) { image?.Dispose(); return; }
                    if (image == null) continue; // unreadable or removed since the scan
                    history.Add(path); position = history.Count - 1;
                    if (history.Count > MaxHistory) { history.RemoveAt(0); position--; }
                    Show(path, image); return;
                }
            }
            Current = null; history.Clear(); position = -1; panel.CanGoBack = false;
            panel.Placeholder = Directory.Exists(folder) ? "No photos in this folder yet" : "Folder unavailable — use Change folder…";
            panel.SetPhoto(null);
        }
        finally { loading = false; }
    }
    /// <summary>Shuffled photo paths, with the one on screen moved so it doesn't repeat straight away.</summary>
    static List<string> Scan(string folder, string? current)
    {
        if (!Directory.Exists(folder)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        List<string> photos;
        try { photos = [.. Directory.EnumerateFiles(folder, "*", options).Where(Boxes.IsPhoto).Take(MaxPhotos)]; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(photos));
        if (photos.Count > 1 && photos[^1] == current) (photos[0], photos[^1]) = (photos[^1], photos[0]);
        return photos;
    }
    public void Dispose() { disposed = true; timer.Dispose(); panel.ShowControls = false; panel.Invalidate(); }
}
