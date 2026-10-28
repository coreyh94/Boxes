using System.Runtime.InteropServices;
namespace DesktopBoxes;

// Uses the supported Shell folder-view API. Vtable slots follow ShObjIdl_core.h.
internal sealed class DesktopIcons : IDisposable
{
    nint view;
    readonly List<nint> pidls = [];
    public List<Point> Positions { get; } = [];
    public Size Spacing { get; }
    public Point Origin { get; }
    public Point AnchorInset { get; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryService(nint self, ref Guid service, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int OutPointer(nint self, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Item(nint self, int index, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Count(nint self, uint flags, out int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Position(nint self, nint pidl, out Point point);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SpacingCall(nint self, out Point point);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Flags(nint self, uint mask, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Move(nint self, uint count, [In] nint[] pidls, [In] Point[] points, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int IconSizeCall(nint self, out uint mode, out int size);
    [DllImport("user32.dll")] static extern bool ClientToScreen(nint window, ref Point point);
    static T Method<T>(nint obj, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));
    static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    public DesktopIcons()
    {
        object? windows = null, desktop = null;
        nint unknown = 0, provider = 0, browser = 0, shellView = 0;
        try
        {
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!)!;
            object location = 0, root = 0;
            desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, out int hwnd, 1);
            if (desktop == null) throw new COMException("Desktop Explorer view unavailable.");
            unknown = Marshal.GetIUnknownForObject(desktop);
            var providerId = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
            Check(Marshal.QueryInterface(unknown, in providerId, out provider));
            var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            var browserId = new Guid("000214E2-0000-0000-C000-000000000046");
            Check(Method<QueryService>(provider, 3)(provider, ref service, ref browserId, out browser));
            Check(Method<OutPointer>(browser, 15)(browser, out shellView)); // QueryActiveShellView
            var viewId = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
            Check(Marshal.QueryInterface(shellView, in viewId, out view));
            Check(Method<OutPointer>(shellView, 3)(shellView, out var viewWindow));
            var origin = Point.Empty;
            if (!ClientToScreen(viewWindow, ref origin)) throw new COMException("Cannot locate desktop view.");
            Origin = origin;
            Check(Method<SpacingCall>(view, 12)(view, out var spacing));
            Check(Method<IconSizeCall>(view, 36)(view, out _, out var iconSize));
            Spacing = new Size(Math.Max(iconSize + 16, spacing.X), Math.Max(iconSize + 32, spacing.Y));
            AnchorInset = new Point(Math.Max(0, (Spacing.Width - iconSize) / 2), 8);
            Check(Method<Count>(view, 7)(view, 2, out var count)); // SVGIO_ALLVIEW
            for (int i = 0; i < count; i++)
            {
                Check(Method<Item>(view, 6)(view, i, out var pidl));
                pidls.Add(pidl);
                Check(Method<Position>(view, 11)(view, pidl, out var point));
                Positions.Add(point + new Size(origin));
            }
        }
        catch { Dispose(); throw; }
        finally
        {
            foreach (var p in new[] { shellView, browser, provider, unknown }) if (p != 0) Marshal.Release(p);
            if (desktop != null) Marshal.ReleaseComObject(desktop);
            if (windows != null) Marshal.ReleaseComObject(windows);
        }
    }
    public void Apply(Point[] points)
    {
        var indices = Enumerable.Range(0, points.Length).Where(i => points[i] != Positions[i]).ToArray();
        if (indices.Length == 0) return;
        // Disable auto-arrange and snap-to-grid: Boxes owns placement and supplies spaced slots.
        Check(Method<Flags>(view, 24)(view, 0x1 | 0x4, 0));
        Check(Method<Move>(view, 16)(view, (uint)indices.Length, indices.Select(i => pidls[i]).ToArray(),
            indices.Select(i => points[i] - new Size(Origin)).ToArray(), 0x80)); // SVSI_POSITIONITEM
        foreach (var i in indices)
        {
            Check(Method<Position>(view, 11)(view, pidls[i], out var actual));
            if (actual + new Size(Origin) != points[i]) throw new COMException("Explorer did not accept icon placement.");
        }
    }
    public void Dispose() { foreach (var p in pidls) Marshal.FreeCoTaskMem(p); pidls.Clear(); if (view != 0) { Marshal.Release(view); view = 0; } }
}

internal static class Placement
{
    // Reserve a full label/spacing cell around each icon anchor, including left/top padding.
    internal static Rectangle Cell(Point p, Size spacing, Point? inset = null) => new(p.X - (inset?.X ?? spacing.Width / 2), p.Y - (inset?.Y ?? 8), spacing.Width, spacing.Height);
    internal static Point[]? Arrange(IReadOnlyList<Point> icons, Size spacing, Rectangle[] boxes, Rectangle[] screens, Point? inset = null)
    {
        var result = icons.ToArray();
        var occupied = new List<Rectangle>(boxes.Select(b => Rectangle.Inflate(b, 8, 8)));
        var moved = new List<int>();
        for (var i = 0; i < result.Length; i++)
        {
            var cell = Cell(result[i], spacing, inset);
            if (occupied.Any(r => r.IntersectsWith(cell))) moved.Add(i);
            else occupied.Add(cell);
        }
        foreach (var i in moved)
        {
            Point? free = null;
            foreach (var area in screens)
            {
                for (int y = area.Top; y + spacing.Height <= area.Bottom && free == null; y += spacing.Height)
                for (int x = area.Left; x + spacing.Width <= area.Right; x += spacing.Width)
                {
                    var p = new Point(x + (inset?.X ?? spacing.Width / 2), y + (inset?.Y ?? 8));
                    if (occupied.Any(r => r.IntersectsWith(Cell(p, spacing, inset)))) continue;
                    free = p; break;
                }
                if (free != null) break;
            }
            if (free == null) return null; // No partial moves on a full desktop.
            result[i] = free.Value; occupied.Add(Cell(free.Value, spacing, inset));
        }
        return result;
    }
    /// <summary>
    /// Where each box should sit on the current monitors. A box whose saved (home) spot still fits stays there, so it
    /// returns home when a monitor or resolution comes back. Others borrow free space on the nearest monitor, shrunk to
    /// fit if needed, without changing their saved spot. Null means no room anywhere (the box is hidden).
    /// </summary>
    internal static Rectangle?[] Fit(Rectangle[] homes, Rectangle[] screens)
    {
        var result = new Rectangle?[homes.Length]; var placed = new List<Rectangle>();
        for (int i = 0; i < homes.Length; i++)
            if (screens.Any(s => s.Contains(homes[i])) && !placed.Any(homes[i].IntersectsWith)) { result[i] = homes[i]; placed.Add(homes[i]); }
        for (int i = 0; i < homes.Length; i++)
        {
            if (result[i] != null) continue;
            var centre = new Point(homes[i].Left + homes[i].Width / 2, homes[i].Top + homes[i].Height / 2);
            foreach (var area in screens.OrderBy(s => Distance(s, centre)))
            {
                var size = new Size(Math.Max(240, Math.Min(homes[i].Width, area.Width / 48 * 48)), Math.Max(192, Math.Min(homes[i].Height, area.Height / 48 * 48)));
                if (FindBox(size, [.. placed], [area]) is not { } slot) continue;
                result[i] = slot; placed.Add(slot); break;
            }
        }
        return result;
    }
    static double Distance(Rectangle r, Point p) { int dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right), dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom); return Math.Sqrt(dx * dx + dy * dy); }
    internal static Rectangle? FindBox(Size size, Rectangle[] boxes, Rectangle[] screens, Point? inset = null)
    {
        foreach (var area in screens)
        for (int y = area.Top; y + size.Height <= area.Bottom; y += 48)
        for (int x = area.Left; x + size.Width <= area.Right; x += 48)
        {
            var candidate = new Rectangle(x, y, size.Width, size.Height);
            if (!boxes.Any(b => b.IntersectsWith(candidate))) return candidate;
        }
        return null;
    }
}


