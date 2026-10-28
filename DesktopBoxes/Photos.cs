using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using static DesktopBoxes.Theme;

namespace DesktopBoxes;

/// <summary>Body of a photo box: the photo cropped to fill, with rounded corners, like a card image on a website.</summary>
internal sealed class PhotoPanel : Control
{
    Bitmap? photo, cache;
    internal Color Accent = Color.SteelBlue;
    internal bool HasPhoto => photo != null;
    internal PhotoPanel() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }

    /// <summary>Loads into memory (no file lock) and applies the camera's EXIF rotation.</summary>
    internal void Load(string? path)
    {
        photo?.Dispose(); cache?.Dispose(); photo = cache = null;
        if (path != null) photo = Read(path);
        Invalidate();
    }
    static Bitmap? Read(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var image = Image.FromStream(stream);
            var copy = new Bitmap(image);
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
    internal static Size BoxSizeFor(string path, Size screen)
    {
        using var image = Read(path);
        float aspect = image == null ? 4f / 3 : (float)image.Width / image.Height;
        int header = S(48), inset = S(10), maxHeight = Math.Max(192, screen.Height / 2 / 48 * 48);
        int width = aspect >= 1 ? 432 : 336;
        int height = header + (int)((width - inset * 2) / aspect) + inset;
        if (height > maxHeight) { height = maxHeight; width = (int)((height - header - inset) * aspect) + inset * 2; }
        static int Snap(int v, int min) => Math.Max(min, (int)Math.Round(v / 48d) * 48);
        return new Size(Snap(width, 240), Snap(height, 192));
    }
    Rectangle Frame => new(S(10), 0, Math.Max(1, Width - S(20)), Math.Max(1, Height - S(10)));
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        var frame = Frame; using var path = Round(frame, S(8));
        if (photo == null)
        {
            using var pen = new Pen(Mix(BackColor, Accent, .35f), Math.Max(1, Theme.Scale)) { DashPattern = [4, 3] }; g.DrawPath(pen, path);
            TextRenderer.DrawText(g, "Photo unavailable — use Change photo…", Small, frame, Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
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
        using var brush = new TextureBrush(cache, WrapMode.Clamp); brush.TranslateTransform(frame.X, frame.Y);
        g.FillPath(brush, path);
    }
    protected override void Dispose(bool disposing) { if (disposing) { photo?.Dispose(); cache?.Dispose(); } base.Dispose(disposing); }
}
