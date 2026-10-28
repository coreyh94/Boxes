using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DesktopBoxes;

internal static class Theme
{
    internal static readonly Color Ink = Color.FromArgb(237, 241, 248);
    internal static readonly Color Muted = Color.FromArgb(155, 168, 187);
    internal static readonly Font Title = new("Segoe UI Semibold", 18, FontStyle.Regular, GraphicsUnit.Pixel);
    internal static readonly Font Body = new("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
    internal static readonly Font Small = new("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
    internal static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R * (1 - amount) + b.R * amount), (int)(a.G * (1 - amount) + b.G * amount), (int)(a.B * (1 - amount) + b.B * amount));
    internal static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); float d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
}

public sealed class Box : Form
{
    internal readonly State State;
    readonly Boxes owner;
    internal bool Moving;
    /// <summary>The saved spot. Only a deliberate move/resize changes it; monitor changes just borrow space elsewhere.</summary>
    internal Rectangle Home => new(State.X, State.Y, State.Width, State.Height);
    readonly TilePanel items = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 4, 10, 4) };
    readonly Panel header = new() { Dock = DockStyle.Top, Height = 64, Cursor = Cursors.SizeAll };
    readonly TextBox title = new() { BorderStyle = BorderStyle.None, ForeColor = Theme.Ink, Font = Theme.Title };
    readonly Label count = new() { AutoSize = false, Font = Theme.Small, ForeColor = Theme.Muted };
    readonly Panel footer = new() { Dock = DockStyle.Bottom, Height = 30, Cursor = Cursors.SizeNWSE };
    readonly Label status = new() { AutoEllipsis = true, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    readonly SoftButton add = new() { Name = "NewBoxButton", Text = "+ New box", AccessibleName = "Create a new box" };
    readonly SoftButton options = new() { Text = "···", AccessibleName = "Box options" };
    readonly ContextMenuStrip menu = Menus.Create(Color.SteelBlue);
    readonly SwatchRow swatches = new();
    readonly ToolTip tips = new() { InitialDelay = 400, AutoPopDelay = 6000 };
    Color accent;
    Point drag;
    Grid? grid;
    bool draggingOver;
    string? message;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }

    public Box(Boxes owner, State state)
    {
        this.owner = owner; State = state; Text = state.Name;
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; MinimumSize = new Size(240, 192);
        DoubleBuffered = true; Padding = new Padding(1); Font = Theme.Body;
        Bounds = Home; // Boxes.FitToScreens relocates it if this spot is off the current monitors.
        Controls.Add(items); Controls.Add(footer); Controls.Add(header);
        header.Controls.AddRange([title, count, add, options]); footer.Controls.Add(status);
        title.Text = state.Name; title.AccessibleName = "Box title";
        title.Leave += (_, _) => { State.Name = title.Text; Text = title.Text; owner.Save(); };
        title.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { items.Focus(); e.SuppressKeyPress = true; } };
        add.Click += (_, _) => owner.Add(); options.Click += (_, _) => menu.Show(options, new Point(0, options.Height + 6));
        tips.SetToolTip(add, "Create a new box"); tips.SetToolTip(options, "Colours and box options");
        tips.SetToolTip(header, "Drag to move · boxes snap to the grid");
        tips.SetToolTip(footer, "Drag to resize");
        swatches.Picked += colour => { State.Colour = colour.ToArgb(); Recolour(); owner.Save(); };
        menu.Opening += (_, _) => swatches.Current = Color.FromArgb(255, Color.FromArgb(State.Colour));
        menu.Items.AddRange([
            Menus.Spacer(), swatches, Menus.Separator(),
            Menus.Item("New box", Menus.Icons.Add, () => owner.Add()),
            Menus.Item("Rename", Menus.Icons.Rename, () => { title.Focus(); title.SelectAll(); }),
            Menus.Item("Open saved items", Menus.Icons.Folder, () => { Directory.CreateDirectory(owner.Library.Root); Process.Start(new ProcessStartInfo(owner.Library.Root) { UseShellExecute = true }); }),
            Menus.Item("Settings…", Menus.Icons.Settings, () => SettingsWindow.Open(owner)),
            Menus.Separator(),
            Menus.Item("Remove box", Menus.Icons.Delete, () =>
            {
                if (owner.Busy) { SetMessage("Let your items finish saving first"); return; }
                owner.Windows.Remove(this); owner.Save(); Close();
            }, danger: true), Menus.Spacer()]);
        foreach (var c in new Control[] { this, header, count, items, footer }) c.ContextMenuStrip = menu;
        foreach (var c in new Control[] { header, count })
        {
            c.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                Moving = true; drag = PointToClient(c.PointToScreen(e.Location)); c.Capture = true;
                grid = new Grid(Screen.FromControl(this).WorkingArea); grid.Show();
            };
            c.MouseMove += (_, e) =>
            {
                if (!Moving || !c.Capture) return;
                var p = c.PointToScreen(e.Location); Location = new Point(p.X - drag.X, p.Y - drag.Y);
            };
            c.MouseUp += (_, _) => { if (!Moving) return; c.Capture = false; grid?.Close(); grid = null; Snap(); Moving = false; };
        }
        footer.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            Moving = true; Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, 17, 0); Snap(); Moving = false;
        };
        header.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(accent);
            e.Graphics.FillEllipse(brush, 17, 20, 7, 7);
            using var pen = new Pen(Color.FromArgb(28, accent));
            e.Graphics.DrawLine(pen, 16, header.Height - 1, header.Width - 16, header.Height - 1);
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(90, Theme.Muted));
            for (int i = 0; i < 3; i++) e.Graphics.DrawLine(pen, footer.Width - 10 - i * 4, footer.Height - 8, footer.Width - 8, footer.Height - 10 - i * 4);
        };
        foreach (var c in new Control[] { this, items, header, footer }) EnableDrop(c);
        Recolour(); RefreshItems(); LayoutChrome();
    }

    void EnableDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            var valid = !owner.Busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true && (e.AllowedEffect & DragDropEffects.Copy) != 0;
            e.Effect = valid ? DragDropEffects.Copy : DragDropEffects.None;
            draggingOver = valid; Invalidate();
        };
        control.DragLeave += (_, _) => { draggingOver = false; Invalidate(); };
        control.DragDrop += async (_, e) =>
        {
            draggingOver = false; Invalidate();
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) await owner.ImportItemsAsync(this, paths);
        };
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 32 || Height < 32) return;
        using var path = Theme.Round(new RectangleF(0, 0, Width, Height), 16);
        var old = Region; Region = new Region(path); old?.Dispose();
        if (title != null) LayoutChrome();
        Invalidate();
    }
    void LayoutChrome()
    {
        bool compact = ClientSize.Width < 340;
        int addWidth = compact ? 34 : 96;
        add.Text = compact ? "+" : "+ New box";
        options.Bounds = new Rectangle(header.Width - 47, 14, 32, 30);
        add.Bounds = new Rectangle(options.Left - addWidth - 6, 14, addWidth, 30);
        title.Bounds = new Rectangle(33, 14, Math.Max(36, add.Left - 43), 24);
        count.Bounds = new Rectangle(33, 39, Math.Max(36, header.Width - 60), 16);
        status.Bounds = new Rectangle(16, 0, Math.Max(10, footer.Width - 44), 26);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 16);
        using var pen = new Pen(draggingOver ? accent : Theme.Mix(BackColor, accent, .34f), draggingOver ? 3 : 1);
        e.Graphics.DrawPath(pen, path);
    }
    void Recolour()
    {
        accent = Theme.Mix(Color.FromArgb(State.Colour), Color.White, .15f);
        BackColor = Theme.Mix(Color.FromArgb(21, 25, 34), accent, .10f);
        header.BackColor = title.BackColor = Theme.Mix(BackColor, accent, .055f);
        items.BackColor = footer.BackColor = BackColor; items.Accent = accent;
        add.Accent = options.Accent = accent;
        foreach (var tile in items.Controls.OfType<ItemTile>()) { tile.Accent = accent; if (tile.ContextMenuStrip != null) Menus.SetAccent(tile.ContextMenuStrip, accent); }
        Menus.SetAccent(menu, accent);
        Invalidate(true);
    }
    internal void SetMessage(string? text) { message = text; UpdateStatus(); }
    void UpdateStatus()
    {
        count.Text = State.Items.Count == 1 ? "1 item" : $"{State.Items.Count} items";
        status.Text = message ?? (State.Items.Count == 0 ? "Your desktop, a little calmer." : "Saved in Boxes");
        tips.SetToolTip(status, message ?? "Independent copies · originals can be removed from the desktop");
    }
    void Snap()
    {
        var area = Screen.FromControl(this).WorkingArea;
        int Round(int v) => (int)Math.Round(v / 48d) * 48;
        var proposed = new Rectangle(area.Left + Round(Left - area.Left), area.Top + Round(Top - area.Top), Math.Max(240, Round(Width)), Math.Max(192, Round(Height)));
        if (!owner.Place(this, proposed)) { Bounds = new Rectangle(State.X, State.Y, State.Width, State.Height); return; }
        Bounds = proposed; State.X = Left; State.Y = Top; State.Width = Width; State.Height = Height; owner.Save();
    }
    internal void RefreshItems()
    {
        items.SuspendLayout();
        foreach (Control c in items.Controls.Cast<Control>().ToArray()) c.Dispose();
        foreach (var path in State.Items.ToArray())
        {
            var name = Path.GetFileName(path);
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(path);
            var tile = new ItemTile { Text = name, AccessibleName = name, Accent = accent, Image = ShellImage.Load(path) };
            tile.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { SetMessage("Couldn't open this item. Check its saved file."); }
            };
            tips.SetToolTip(tile, name + (owner.Library.Contains(path) ? "\nSaved in Boxes" : "\nOriginal unavailable — drop it again to save a copy"));
            var itemMenu = Menus.Create(accent);
            itemMenu.Items.AddRange([
                Menus.Spacer(), Menus.Item("Open", Menus.Icons.Open, () => tile.PerformClick()),
                Menus.Item("Show saved file", Menus.Icons.Reveal, () => Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true })),
                Menus.Separator(),
                Menus.Item("Remove from box", Menus.Icons.Remove, () => { if (owner.Busy) return; State.Items.Remove(path); RefreshItems(); owner.Save(); }, danger: true), Menus.Spacer()]);
            tile.ContextMenuStrip = itemMenu;
            tile.Disposed += (_, _) => { tile.Image?.Dispose(); itemMenu.Dispose(); };
            EnableDrop(tile); items.Controls.Add(tile);
        }
        items.ResumeLayout(); items.Invalidate(); UpdateStatus();
    }
    internal void RenderPreview()
    {
        // Exercise both populated and compact layouts without touching the user's library.
        State.Name = "Everyday"; title.Text = "Everyday"; Size = new Size(432, 336);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var sample in new[] { ("Project notes", Path.Combine(windows, "notepad.exe")), ("Design files", windows), ("Terminal", Path.Combine(windows, "System32", "cmd.exe")), ("Quick links", Path.Combine(windows, "explorer.exe")) })
        {
            var tile = new ItemTile { Text = sample.Item1, Accent = accent, Image = ShellImage.Load(sample.Item2) };
            tile.Disposed += (_, _) => tile.Image?.Dispose(); items.Controls.Add(tile);
        }
        count.Text = "4 items"; status.Text = "Saved in Boxes";
        PerformLayout(); LayoutChrome();
        var output = Environment.GetEnvironmentVariable("BOXES_PREVIEW_DIR");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, ClientRectangle); bitmap.Save(Path.Combine(output, "boxes-populated.png"));
            swatches.Current = Color.FromArgb(255, Color.FromArgb(State.Colour)); Menus.RenderPreview(menu, Path.Combine(output, "menu-box.png"), 3);
            foreach (Control c in items.Controls.Cast<Control>().ToArray()) c.Dispose();
            Size = new Size(288, 240); count.Text = "0 items"; status.Text = "Your desktop, a little calmer."; PerformLayout(); LayoutChrome();
            using var empty = new Bitmap(Width, Height); DrawToBitmap(empty, ClientRectangle); empty.Save(Path.Combine(output, "boxes-compact.png"));
        }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 hides a box; only Remove box changes the saved layout.
        if (e.CloseReason == CloseReason.UserClosing && owner.Windows.Contains(this)) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { grid?.Dispose(); tips.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class TilePanel : FlowLayoutPanel
{
    internal Color Accent;
    internal TilePanel() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (Controls.Count != 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int x = ClientSize.Width / 2, y = Math.Max(28, ClientSize.Height / 2 - 26);
        using var pen = new Pen(Color.FromArgb(130, Accent), 1.5f);
        using var outline = Theme.Round(new RectangleF(x - 19, y - 18, 38, 34), 8);
        e.Graphics.DrawPath(pen, outline); e.Graphics.DrawLine(pen, x - 6, y - 1, x + 6, y - 1); e.Graphics.DrawLine(pen, x, y - 7, x, y + 5);
        TextRenderer.DrawText(e.Graphics, "Drop icons here", Theme.Body, new Rectangle(0, y + 28, Width, 22), Theme.Ink, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(e.Graphics, "Kept safe in this box", Theme.Small, new Rectangle(0, y + 52, Width, 20), Theme.Muted, TextFormatFlags.HorizontalCenter);
    }
}

internal class SoftButton : Button
{
    internal Color Accent = Color.SteelBlue;
    protected bool Hover;
    internal SoftButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; Font = Theme.Body; ForeColor = Theme.Ink; }
    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8);
        using var brush = new SolidBrush(Theme.Mix(Parent?.BackColor ?? BackColor, Accent, Hover ? .28f : .13f)); e.Graphics.FillPath(brush, path);
        if (Focused) { using var pen = new Pen(Accent); e.Graphics.DrawPath(pen, path); }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
internal sealed class ItemTile : SoftButton
{
    internal ItemTile() { Size = new Size(92, 94); Margin = new Padding(4); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Hover || Focused)
        {
            using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10);
            using var brush = new SolidBrush(Theme.Mix(Parent?.BackColor ?? BackColor, Accent, .15f)); e.Graphics.FillPath(brush, path);
            if (Focused) { using var pen = new Pen(Color.FromArgb(160, Accent)); e.Graphics.DrawPath(pen, path); }
        }
        if (Image != null) e.Graphics.DrawImage(Image, new Rectangle((Width - 32) / 2, 10, 32, 32));
        TextRenderer.DrawText(e.Graphics, Text, Theme.Body, new Rectangle(3, 51, Width - 6, 37), Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }
}
internal static class ShellImage
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileInfo
    {
        internal nint Icon; internal int Index; internal uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern nint SHGetFileInfo(string path, uint attributes, out FileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint icon);
    internal static Bitmap Load(string path)
    {
        SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<FileInfo>(), 0x100);
        if (info.Icon == 0) return SystemIcons.Application.ToBitmap();
        try { using var icon = Icon.FromHandle(info.Icon); return icon.ToBitmap(); }
        finally { DestroyIcon(info.Icon); }
    }
}
sealed class Grid : Form
{
    public Grid(Rectangle area) { StartPosition = FormStartPosition.Manual; Bounds = area; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; BackColor = Color.Black; Opacity = .12; Enabled = false; }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x080000A0; return cp; } }
    protected override void OnPaint(PaintEventArgs e) { for (int x = 0; x < Width; x += 48) for (int y = 0; y < Height; y += 48) e.Graphics.FillEllipse(Brushes.White, x, y, 3, 3); }
}
