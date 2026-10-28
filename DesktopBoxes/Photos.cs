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
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        var frame = Frame; using var path = Round(frame, S(8));
        if (photo == null)
        {
            using var pen = new Pen(Mix(BackColor, Accent, .35f), Math.Max(1, Theme.Scale)) { DashPattern = [4, 3] }; g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Placeholder, Small, Rectangle.Inflate(frame, -S(12), 0), Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
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
    }
    protected override void Dispose(bool disposing) { if (disposing) { fadeTimer.Dispose(); photo?.Dispose(); cache?.Dispose(); fading?.Dispose(); } base.Dispose(disposing); }
}

/// <summary>
/// Drives a slideshow box: shuffles the photos in a folder (and its subfolders), shows the next one every few seconds,
/// and rescans after each full pass so new photos appear. Decoding runs in the background; it pauses while hidden.
/// </summary>
internal sealed class PhotoReel : IDisposable
{
    const int MaxPhotos = 5000;
    readonly PhotoPanel panel; readonly string folder;
    readonly System.Windows.Forms.Timer timer;
    List<string> queue = [];
    bool loading, disposed;
    internal string? Current { get; private set; }
    internal PhotoReel(PhotoPanel panel, string folder, int seconds)
    {
        this.panel = panel; this.folder = folder;
        timer = new() { Interval = Math.Max(3, seconds) * 1000 };
        timer.Tick += async (_, _) => await NextAsync();
    }
    /// <summary>Changing the interval restarts the countdown, so the current photo gets the full new time.</summary>
    internal void SetInterval(int seconds) => timer.Interval = Math.Max(3, seconds) * 1000;
    internal int IntervalSeconds => timer.Interval / 1000;
    internal async Task StartAsync() { panel.Placeholder = "Loading photos…"; panel.Invalidate(); await NextAsync(); if (!disposed) timer.Start(); }
    /// <summary>Shows the next photo. Skips work while the box is hidden, unless nothing has been shown yet.</summary>
    internal async Task NextAsync()
    {
        if (loading || disposed || (!panel.Visible && panel.HasPhoto)) return;
        loading = true;
        try
        {
            for (int attempts = 0; attempts < 2; attempts++)
            {
                if (queue.Count == 0) queue = await Task.Run(() => Scan(folder, Current));
                while (queue.Count > 0)
                {
                    var path = queue[^1]; queue.RemoveAt(queue.Count - 1);
                    var image = await Task.Run(() => PhotoPanel.Read(path));
                    if (disposed) { image?.Dispose(); return; }
                    if (image == null) continue; // unreadable or removed since the scan
                    Current = path; panel.SetPhoto(image, fade: true); return;
                }
            }
            Current = null;
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
    public void Dispose() { disposed = true; timer.Dispose(); }
}
