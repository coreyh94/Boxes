using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DesktopBoxes;

internal static class Theme
{
    internal static readonly Color Ink = Color.FromArgb(237, 241, 248);
    internal static readonly Color Muted = Color.FromArgb(155, 168, 187);
    /// <summary>Display scaling (1.5 at 150%). The app is system-DPI aware, so all chrome is sized in 96-DPI units times this.</summary>
    internal static readonly float Scale = ReadScale();
    static float ReadScale() { using var g = Graphics.FromHwnd(0); return Math.Max(1f, g.DpiX / 96f); }
    internal static int S(int units) => (int)Math.Round(units * Scale);
    internal static readonly Font Title = new("Segoe UI Semibold", 15 * Scale, FontStyle.Regular, GraphicsUnit.Pixel);
    internal static readonly Font Body = new("Segoe UI", 12 * Scale, FontStyle.Regular, GraphicsUnit.Pixel);
    internal static readonly Font Small = new("Segoe UI", 11 * Scale, FontStyle.Regular, GraphicsUnit.Pixel);
    internal static readonly Font Label = new("Segoe UI", 12 * Scale, FontStyle.Regular, GraphicsUnit.Pixel);
    internal static readonly Color Card = Color.FromArgb(24, 27, 35);
    internal static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R * (1 - amount) + b.R * amount), (int)(a.G * (1 - amount) + b.G * amount), (int)(a.B * (1 - amount) + b.B * amount));
    /// <summary>
    /// A box background in the swatch's own hue: fixed dark lightness so the light text stays readable, and half the
    /// swatch's saturation (capped) so boxes sit calmly together. Blending into <see cref="Card"/> instead washes every colour towards grey.
    /// </summary>
    internal static Color Tint(Color swatch) => FromHsl(swatch.GetHue(), Math.Min(.45f, swatch.GetSaturation() * .5f), .23f);
    /// <summary>Body and title-strip colours for a swatch. Sticky note and Pastel are light cards with a slightly deeper title strip.</summary>
    internal static (Color Body, Color Header) Surface(Color swatch, BoxStyle style)
    {
        float hue = swatch.GetHue(), sat = swatch.GetSaturation();
        return style switch
        {
            BoxStyle.Sticky => (FromHsl(hue, Math.Min(1, sat * 1.2f), .83f), FromHsl(hue, Math.Min(.95f, sat * 1.2f), .76f)),
            BoxStyle.Pastel => (FromHsl(hue, Math.Min(.85f, sat), .91f), FromHsl(hue, Math.Min(.75f, sat), .85f)),
            _ => (Tint(swatch), Tint(swatch)),
        };
    }
    internal static readonly Color DarkInk = Color.FromArgb(32, 33, 36), DarkMuted = Color.FromArgb(78, 84, 96);
    internal static bool IsLight(Color c) => .2126f * c.R + .7152f * c.G + .0722f * c.B > 140;
    /// <summary>Text colour for a surface: dark on light boxes, light on dark ones (and in menus).</summary>
    internal static Color InkOn(Color back) => IsLight(back) ? DarkInk : Ink;
    internal static Color MutedOn(Color back) => IsLight(back) ? DarkMuted : Muted;
    static Color FromHsl(float hue, float saturation, float lightness)
    {
        float c = (1 - Math.Abs(2 * lightness - 1)) * saturation, h = hue / 60, x = c * (1 - Math.Abs(h % 2 - 1)), m = lightness - c / 2;
        var (r, g, b) = (int)h switch { 0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x), 3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x) };
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
    internal static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); float d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
}

internal enum BoxStyle { Sticky, Pastel, Dark }

public sealed class Box : Form
{
    internal readonly State State;
    readonly Boxes owner;
    internal bool Moving;
    /// <summary>The saved spot. Only a deliberate move/resize changes it; monitor changes just borrow space elsewhere.</summary>
    internal Rectangle Home => new(State.X, State.Y, State.Width, State.Height);
    readonly TilePanel items = new() { Padding = new Padding(Theme.S(8), 0, Theme.S(8), Theme.S(10)) };
    readonly ScrollHost scroller;
    /// <summary>Shown instead of the items when this is a photo box.</summary>
    readonly PhotoPanel photo = new() { Dock = DockStyle.Fill, Visible = false };
    readonly Panel header = new() { Dock = DockStyle.Top, Height = Theme.S(48), Cursor = Cursors.SizeAll };
    readonly TextBox title = new() { BorderStyle = BorderStyle.None, ForeColor = Theme.Ink, Font = Theme.Title };
    readonly GhostButton options = new() { Glyph = '', AccessibleName = "Box options" };
    /// <summary>Invisible bottom-right resize handle; shows faint dots on hover.</summary>
    readonly Grip grip = new() { Cursor = Cursors.SizeNWSE, Size = new Size(Theme.S(18), Theme.S(18)) };
    /// <summary>Only shown for transient messages (saving, errors); the box is otherwise label-free.</summary>
    readonly Label status = new() { AutoSize = false, AutoEllipsis = true, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Visible = false };
    readonly ContextMenuStrip menu = Menus.Create(Color.SteelBlue);
    readonly SwatchRow swatches = new();
    readonly OpacityRow opacity = new();
    readonly ToolTip tips = new() { InitialDelay = 400, AutoPopDelay = 6000 };
    /// <summary>Highlights inside the box (darker on light boxes); <see cref="menuAccent"/> suits the dark menus.</summary>
    Color accent, menuAccent;
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
        DoubleBuffered = true; Font = Theme.Body;
        Bounds = Home; // Boxes.FitToScreens relocates it if this spot is off the current monitors.
        scroller = new ScrollHost(items) { Dock = DockStyle.Fill };
        Controls.Add(scroller); Controls.Add(photo); Controls.Add(header); Controls.Add(grip); Controls.Add(status);
        grip.BringToFront(); status.BringToFront();
        header.Controls.AddRange([title, options]);
        title.Text = state.Name; title.AccessibleName = "Box title";
        // Saved as you type (hooked after the initial text), so an edit survives exiting while the title still has focus.
        title.TextChanged += (_, _) => { if (title.Text == State.Name) return; State.Name = title.Text; Text = title.Text; owner.Save(); };
        title.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { items.Focus(); e.SuppressKeyPress = true; } };
        options.Click += (_, _) => menu.Show(options, new Point(options.Width - menu.Width, options.Height + Theme.S(6)));
        tips.SetToolTip(options, "Colours and box options");
        tips.SetToolTip(header, "Drag to move · boxes snap to the grid");
        swatches.Picked += colour => { State.Colour = colour.ToArgb(); Recolour(); owner.Save(); };
        opacity.Changed += value => { State.Opacity = value; ApplyOpacity(); };
        opacity.Committed += _ => owner.Save();
        var changePhoto = Menus.Item("Change photo…", Menus.Icons.Photo, () => _ = owner.ChangePhotoAsync(this));
        var nextPhoto = Menus.Item("Next photo", Menus.Icons.Next, () => _ = reel?.NextAsync());
        var changeFolder = Menus.Item("Change folder…", Menus.Icons.Folder, () => owner.ChangeFolder(this));
        style = Menus.Submenu("Style", Menus.Icons.Style, [.. Looks.Select(l => (l.Label, (Action)(() => { State.Look = l.Style; Recolour(); owner.Save(); })))]);
        interval = Menus.Submenu("Change photo every", Menus.Icons.Timer, [.. Intervals.Select(i => (i.Label, (Action)(() => SetInterval(i.Seconds))))]);
        var titleBar = Menus.Item("Hide title bar", Menus.Icons.Hide, () => SetTitleHidden(!State.HideTitle));
        var rename = Menus.Item("Rename", Menus.Icons.Rename, () => { title.Focus(); title.SelectAll(); });
        var paste = Menus.Item("Paste", Menus.Icons.Paste, Paste); paste.ShortcutKeyDisplayString = "Ctrl+V";
        items.KeyDown += (_, e) => { if (e.KeyData == (Keys.Control | Keys.V)) { Paste(); e.SuppressKeyPress = true; } };
        menu.Opening += (_, _) =>
        {
            swatches.Current = Color.FromArgb(255, Color.FromArgb(State.Colour)); opacity.Value = State.Opacity ?? owner.Settings.BoxOpacity;
            changePhoto.Visible = State.Photo != null; nextPhoto.Visible = changeFolder.Visible = interval.Visible = State.PhotoFolder != null;
            Menus.Tick(interval, Array.FindIndex(Intervals, i => i.Seconds == State.SlideSeconds));
            Menus.Tick(style, Array.FindIndex(Looks, l => l.Style == State.Look));
            titleBar.Text = State.HideTitle ? "Show title bar" : "Hide title bar"; rename.Visible = !State.HideTitle;
            paste.Visible = State.Photo == null && State.PhotoFolder == null && CanPaste;
            titleBar.Image?.Dispose(); titleBar.Image = Menus.Glyph(State.HideTitle ? Menus.Icons.Show : Menus.Icons.Hide, Theme.Muted);
        };
        menu.Items.AddRange([
            Menus.Spacer(), swatches, opacity, style, Menus.Separator(),
            paste, changePhoto, nextPhoto, changeFolder, interval, titleBar,
            Menus.Item("New box", Menus.Icons.Add, () => owner.Add()),
            Menus.Item("New photo box…", Menus.Icons.Photo, () => _ = owner.AddPhotoBoxAsync()),
            Menus.Item("New slideshow box…", Menus.Icons.Slideshow, () => owner.AddSlideshowBox()),
            rename,
            Menus.Item("Open saved items", Menus.Icons.Folder, () => { Directory.CreateDirectory(owner.Library.Root); Process.Start(new ProcessStartInfo(owner.Library.Root) { UseShellExecute = true }); }),
            Menus.Item("Settings…", Menus.Icons.Settings, () => SettingsWindow.Open(owner)),
            Menus.Separator(),
            Menus.Item("Remove box", Menus.Icons.Delete, () =>
            {
                if (owner.Busy) { SetMessage("Let your items finish saving first"); return; }
                owner.Windows.Remove(this); owner.Save(); Close();
            }, danger: true), Menus.Spacer()]);
        foreach (var c in new Control[] { this, header, scroller, items, grip, photo }) c.ContextMenuStrip = menu;
        photo.MouseDoubleClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || (reel?.Current ?? State.Photo) is not { } shown) return;
            try { Process.Start(new ProcessStartInfo(shown) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { SetMessage("Couldn't open this photo."); }
        };
        EnableMove(header, () => true, immediate: true);
        // Without a title bar, the photo or the empty space between tiles moves the box; a small threshold keeps double-click working.
        EnableMove(photo, () => State.HideTitle, immediate: false);
        EnableMove(items, () => State.HideTitle, immediate: false);
        grip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            Moving = true; Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, 17, 0); Snap(); Moving = false;
        };
        foreach (var c in new Control[] { this, scroller, items, header, photo }) EnableDrop(c);
        // Clicking empty space in the box, or anywhere outside it, clears the selection.
        items.MouseDown += (_, _) => { if (SelectedTile != null) Select(null); };
        Deactivate += (_, _) => { if (SelectedTile != null) Select(null); };
        Recolour(); ApplyOpacity(); RefreshItems(); ShowPhoto(); LayoutChrome();
    }

    void EnableMove(Control c, Func<bool> active, bool immediate)
    {
        Point? press = null;
        void Begin(Point at) { Moving = true; drag = PointToClient(c.PointToScreen(at)); c.Capture = true; grid = new Grid(Screen.FromControl(this).WorkingArea); grid.Show(); }
        c.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !active()) return;
            if (immediate) Begin(e.Location); else press = e.Location;
        };
        c.MouseMove += (_, e) =>
        {
            if (press is { } start && e.Button == MouseButtons.Left)
            {
                var size = SystemInformation.DragSize;
                if (Math.Abs(e.X - start.X) < size.Width && Math.Abs(e.Y - start.Y) < size.Height) return;
                press = null; Begin(start);
            }
            if (!Moving || !c.Capture) return;
            var p = c.PointToScreen(e.Location); Location = new Point(p.X - drag.X, p.Y - drag.Y);
        };
        c.MouseUp += (_, _) => { press = null; if (!Moving) return; c.Capture = false; grid?.Close(); grid = null; Snap(); Moving = false; };
    }
    static readonly (int Seconds, string Label)[] Intervals = [(10, "10 seconds"), (20, "20 seconds"), (60, "1 minute"), (300, "5 minutes")];
    readonly ToolStripMenuItem interval;
    static readonly (BoxStyle Style, string Label)[] Looks = [(BoxStyle.Sticky, "Sticky note"), (BoxStyle.Pastel, "Pastel"), (BoxStyle.Dark, "Dark")];
    readonly ToolStripMenuItem style;
    internal void SetInterval(int seconds) { State.SlideSeconds = seconds; reel?.SetInterval(seconds); owner.Save(); }
    internal void SetTitleHidden(bool hidden) { State.HideTitle = hidden; LayoutChrome(); owner.Save(); }
    internal bool TitleShown => header.Visible;
    /// <summary>Renders the box menu with its interval submenu open, highlighting the parent item.</summary>
    internal void RenderMenuPreview(string file)
    {
        Menus.RenderPreview(menu, file, menu.Items.IndexOf(interval));
        Menus.RenderPreview((ContextMenuStrip)interval.DropDown, Path.ChangeExtension(file, null) + "-interval.png", 1);
    }

    /// <summary>The tile being dragged between/within boxes. Kept in-process; the drag data only carries a marker
    /// format, so Explorer can never receive (and move) a saved library file.</summary>
    static (Box From, string Path)? tileDrag;
    const string TileFormat = "DesktopBoxes.Tile";

    void EnableDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) => DragUpdate(e);
        control.DragOver += (_, e) => DragUpdate(e);
        control.DragLeave += (_, _) => EndDragFeedback();
        control.DragDrop += async (_, e) =>
        {
            var index = InsertIndex(new Point(e.X, e.Y)); EndDragFeedback();
            // Deferred: the source tile is still inside its DoDragDrop loop, and the move rebuilds (disposes) tiles.
            if (State.PhotoFolder != null) { if (DroppedFolder(e) is { } folder) owner.ChangeFolder(this, folder); }
            else if (State.Photo != null) { if (DroppedPhoto(e) is { } image) await owner.ChangePhotoAsync(this, image); }
            else if (e.Data?.GetDataPresent(TileFormat) == true && tileDrag is { } drag) BeginInvoke(() => owner.MoveItem(drag.From, drag.Path, this, index));
            else if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) await owner.ImportItemsAsync(this, paths);
        };
    }
    void DragUpdate(DragEventArgs e)
    {
        bool tile = State.Photo == null && State.PhotoFolder == null && e.Data?.GetDataPresent(TileFormat) == true && tileDrag != null;
        // Photo boxes take a single photo (replacing theirs); slideshow boxes take a single folder.
        bool files = !tile && e.Data?.GetDataPresent(DataFormats.FileDrop) == true && (e.AllowedEffect & DragDropEffects.Copy) != 0
            && (State.PhotoFolder != null ? DroppedFolder(e) != null : State.Photo == null || DroppedPhoto(e) != null);
        e.Effect = owner.Busy ? DragDropEffects.None : tile ? DragDropEffects.Move : files ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effect == DragDropEffects.None) { EndDragFeedback(); return; }
        if (!draggingOver) { draggingOver = true; ApplyBorder(); Invalidate(); }
        var screen = new Point(e.X, e.Y);
        items.ShowMarker(InsertIndex(screen));
        // Auto-scroll when hovering near the top or bottom edge of a box with more items than fit.
        var local = scroller.PointToClient(screen); int edge = Theme.S(36);
        if (local.Y < edge) scroller.ScrollTo(scroller.Offset - Theme.S(14));
        else if (local.Y > scroller.ClientSize.Height - edge) scroller.ScrollTo(scroller.Offset + Theme.S(14));
    }
    internal void RenderPhotoPreview(string file) { title.Text = State.Name; PerformLayout(); using var bmp = new Bitmap(Width, Height); DrawToBitmap(bmp, ClientRectangle); bmp.Save(file); }
    internal bool PhotoShown => photo.Visible && photo.HasPhoto && !scroller.Visible;
    static string? DroppedPhoto(DragEventArgs e) => e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths && Boxes.IsPhoto(paths[0]) ? paths[0] : null;
    static string? DroppedFolder(DragEventArgs e) => e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths && Directory.Exists(paths[0]) ? paths[0] : null;
    PhotoReel? reel;
    /// <summary>Switches between the item grid, a single photo, and a folder slideshow, from State.</summary>
    internal void ShowPhoto()
    {
        reel?.Dispose(); reel = null;
        bool on = State.Photo != null || State.PhotoFolder != null;
        photo.Visible = on; scroller.Visible = !on;
        if (State.PhotoFolder != null) { reel = new PhotoReel(photo, State.PhotoFolder, State.SlideSeconds); _ = reel.StartAsync(); }
        else { photo.Placeholder = "Photo unavailable — use Change photo…"; photo.Load(State.Photo); }
        if (on) items.ShowMarker(null);
    }
    internal PhotoReel? Reel => reel;
    void EndDragFeedback() { items.ShowMarker(null); if (!draggingOver) return; draggingOver = false; ApplyBorder(); Invalidate(); }
    /// <summary>Slot a drop at this screen point would take, reading tiles left-to-right, top-to-bottom.</summary>
    int InsertIndex(Point screen)
    {
        var p = items.PointToClient(screen); var tiles = items.Controls.Cast<Control>().ToArray();
        for (int i = 0; i < tiles.Length; i++)
            if (p.Y < tiles[i].Top || (p.Y < tiles[i].Bottom && p.X < tiles[i].Left + tiles[i].Width / 2)) return i;
        return tiles.Length;
    }
    /// <summary>One selected tile across all boxes, like desktop icons.</summary>
    static ItemTile? selected;
    internal static void Select(ItemTile? tile)
    {
        if (selected == tile) return;
        if (selected is { IsDisposed: false } old) { old.Selected = false; old.Invalidate(); }
        selected = tile; if (tile == null) return;
        tile.Selected = true; tile.Invalidate();
        tile.Disposed += (_, _) => { if (selected == tile) selected = null; };
    }
    internal void SelectPath(string path) => Select(items.Controls.OfType<ItemTile>().FirstOrDefault(t => Equals(t.Tag, path)));
    internal ItemTile? SelectedTile => selected is { IsDisposed: false } t && items.Controls.Contains(t) ? t : null;
    /// <summary>Inserts an item already in the library at a slot (used by moves between and within boxes).</summary>
    internal void InsertItem(string path, int index) { State.Items.Insert(Math.Clamp(index, 0, State.Items.Count), path); RefreshItems(); }
    internal void RemoveItem(string path) { State.Items.Remove(path); RefreshItems(); }
    void StartTileDrag(ItemTile tile, string path)
    {
        tileDrag = (this, path); tile.Ghost = true; tile.Invalidate();
        try { tile.DoDragDrop(new DataObject(TileFormat, path), DragDropEffects.Move); }
        finally { tileDrag = null; if (!tile.IsDisposed) { tile.Ghost = false; tile.Invalidate(); } }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (title != null) LayoutChrome();
        Invalidate();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11 draws smooth, anti-aliased rounded corners and a hairline border; older Windows keeps square corners.
        int round = 2; Native.DwmSetWindowAttribute(Handle, 33, ref round, 4); ApplyBorder();
    }
    void ApplyBorder() { if (!IsHandleCreated) return; int colour = ColorTranslator.ToWin32(draggingOver ? accent : Theme.Mix(BackColor, accent, .28f)); Native.DwmSetWindowAttribute(Handle, 34, ref colour, 4); }
    void LayoutChrome()
    {
        int pad = Theme.S(16), button = Theme.S(30);
        header.Visible = !State.HideTitle; photo.TopInset = State.HideTitle ? Theme.S(10) : 0; photo.Invalidate();
        options.Bounds = new Rectangle(header.Width - Theme.S(10) - button, (header.Height - button) / 2 + Theme.S(2), button, button);
        int titleHeight = title.PreferredHeight;
        title.Bounds = new Rectangle(pad, (header.Height - titleHeight) / 2 + Theme.S(2), Math.Max(Theme.S(40), options.Left - pad - Theme.S(8)), titleHeight);
        grip.Location = new Point(ClientSize.Width - grip.Width, ClientSize.Height - grip.Height);
        status.Bounds = new Rectangle(pad, ClientSize.Height - Theme.S(26), Math.Max(10, ClientSize.Width - pad * 2 - grip.Width), Theme.S(20));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (!draggingOver) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(1, 1, Width - 3, Height - 3), Theme.S(8));
        using var pen = new Pen(accent, Theme.S(2)); e.Graphics.DrawPath(pen, path);
    }
    /// <summary>Fades the whole box, contents included; Windows can't make only a WinForms background see-through.</summary>
    internal void ApplyOpacity() => Opacity = Math.Clamp(State.Opacity ?? owner.Settings.BoxOpacity, Slider.Min, Slider.Max) / 100.0;
    void Recolour()
    {
        // Before 1.2.0 new boxes defaulted to SteelBlue, which isn't in the palette; give them Ocean so the swatch menu shows a choice.
        if (State.Colour == Color.SteelBlue.ToArgb()) State.Colour = SwatchRow.Palette[0].Colour.ToArgb();
        var swatch = Color.FromArgb(255, Color.FromArgb(State.Colour)); var (body, strip) = Theme.Surface(swatch, State.Look);
        menuAccent = Theme.Mix(swatch, Color.White, .15f);
        accent = Theme.IsLight(body) ? Theme.Mix(swatch, Color.Black, .4f) : menuAccent;
        BackColor = body;
        scroller.BackColor = items.BackColor = status.BackColor = grip.BackColor = photo.BackColor = body;
        header.BackColor = title.BackColor = options.BackColor = strip;
        title.ForeColor = Theme.InkOn(strip); status.ForeColor = Theme.MutedOn(body); photo.Accent = accent; photo.Invalidate();
        items.Accent = options.Accent = grip.Accent = scroller.Accent = accent; opacity.Accent = menuAccent;
        foreach (var tile in items.Controls.OfType<ItemTile>()) { tile.Accent = accent; if (tile.ContextMenuStrip != null) Menus.SetAccent(tile.ContextMenuStrip, menuAccent); }
        Menus.SetAccent(menu, menuAccent); Menus.SetAccent((ContextMenuStrip)interval.DropDown, menuAccent); Menus.SetAccent((ContextMenuStrip)style.DropDown, menuAccent); ApplyBorder();
        Invalidate(true);
    }
    internal void SetMessage(string? text) { message = text; UpdateStatus(); }
    void UpdateStatus() { status.Text = message ?? ""; status.Visible = message != null; if (message != null) status.BringToFront(); }
    void Snap()
    {
        var area = Screen.FromControl(this).WorkingArea;
        int Round(int v) => (int)Math.Round(v / 48d) * 48;
        var proposed = new Rectangle(area.Left + Round(Left - area.Left), area.Top + Round(Top - area.Top), Math.Max(240, Round(Width)), Math.Max(192, Round(Height)));
        if (!owner.Place(this, proposed)) { Bounds = new Rectangle(State.X, State.Y, State.Width, State.Height); return; }
        Bounds = proposed; State.X = Left; State.Y = Top; State.Width = Width; State.Height = Height; owner.Save();
    }
    /// <summary>Right-click menu for an item: the usual Explorer verbs, acting on the saved copy.</summary>
    ContextMenuStrip ItemMenu(ItemTile tile, string path, Action open)
    {
        var menu = Menus.Create(menuAccent);
        static ToolStripMenuItem Keyed(ToolStripMenuItem item, string keys) { item.ShortcutKeyDisplayString = keys; return item; }
        bool saved = owner.Library.Contains(path), program = Runnable.Contains(Path.GetExtension(path).ToLowerInvariant());
        var openWith = Menus.Item("Open with…", Menus.Icons.OpenWith, () => Run("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + path));
        var admin = Menus.Item("Run as administrator", Menus.Icons.Shield, () => Run(path, verb: "runas"));
        var location = Menus.Item("Open file location", Menus.Icons.Folder, () => { if (ShortcutTarget(path) is { } target) Reveal(target); });
        // Started once the menu has closed, so the menu taking focus back doesn't end the edit straight away.
        var rename = Keyed(Menus.Item("Rename", Menus.Icons.Rename, () => BeginInvoke(() => BeginRename(tile, path))), "F2");
        menu.Opening += (_, _) =>
        {
            openWith.Visible = File.Exists(path) && !program; admin.Visible = program; rename.Visible = saved;
            location.Visible = IsLink(path) && ShortcutTarget(path) != null;
        };
        menu.Items.AddRange([
            Menus.Spacer(), Menus.Item("Open", Menus.Icons.Open, open), openWith, admin, location,
            Menus.Item("Show saved file", Menus.Icons.Reveal, () => Reveal(path)),
            Menus.Separator(),
            Keyed(Menus.Item("Cut", Menus.Icons.Cut, () => CopyItem(path, move: true)), "Ctrl+X"),
            Keyed(Menus.Item("Copy", Menus.Icons.Copy, () => CopyItem(path, move: false)), "Ctrl+C"),
            Keyed(Menus.Item("Copy as path", Menus.Icons.Link, () => CopyPath(path)), "Ctrl+Shift+C"),
            rename,
            Keyed(Menus.Item("Properties", Menus.Icons.Info, () => Native.SHObjectProperties(Handle, 2, path, null)), "Alt+Enter"),
            Menus.Separator(),
            Keyed(Menus.Item("Remove from box", Menus.Icons.Remove, () => RemoveFromBox(path), danger: true), "Del"), Menus.Spacer()]);
        return menu;
    }
    static readonly HashSet<string> Runnable = [".exe", ".lnk", ".bat", ".cmd", ".msi", ".com"];
    static bool IsLink(string path) => Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
    void Run(string file, string arguments = "", string? verb = null)
    {
        try { Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true, Verb = verb ?? "" }); }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223) { } // UAC prompt cancelled
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { SetMessage("Couldn't open this item."); }
    }
    static void Reveal(string path) => Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
    void CopyPath(string path) { try { Clipboard.SetText("\"" + path + "\""); } catch (ExternalException) { SetMessage("The clipboard is busy. Try again."); } }
    /// <summary>Where a .lnk points, if that file or folder still exists.</summary>
    static string? ShortcutTarget(string lnk)
    {
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!; link = ((dynamic)shell).CreateShortcut(lnk);
            string target = ((dynamic)link).TargetPath; return target.Length > 0 && (File.Exists(target) || Directory.Exists(target)) ? target : null;
        }
        catch (COMException) { return null; }
        finally { foreach (var o in new[] { link, shell }) if (o != null) Marshal.ReleaseComObject(o); }
    }
    /// <summary>Explorer's shortcuts on a selected tile. Run after the key event, since some rebuild (dispose) the tiles.</summary>
    bool ItemKey(Keys keys, ItemTile tile, string path)
    {
        Action? action = keys switch
        {
            Keys.F2 => () => { if (owner.Library.Contains(path)) BeginRename(tile, path); },
            Keys.Control | Keys.C => () => CopyItem(path, move: false),
            Keys.Control | Keys.X => () => CopyItem(path, move: true),
            Keys.Control | Keys.Shift | Keys.C => () => CopyPath(path),
            Keys.Control | Keys.V => Paste,
            Keys.Alt | Keys.Enter => () => Native.SHObjectProperties(Handle, 2, path, null),
            Keys.Delete => () => RemoveFromBox(path),
            _ => null,
        };
        if (action != null) BeginInvoke(action);
        return action != null;
    }
    void RemoveFromBox(string path) { if (owner.Busy) return; State.Items.Remove(path); RefreshItems(); owner.Save(); }

    /// <summary>The item last cut, until it's pasted into a box or Explorer moves its file away (see <see cref="SettleCut"/>).</summary>
    static (Box From, string Path)? cut;
    /// <summary>
    /// Puts the saved file on the clipboard like Explorer does, so it pastes into folders as well as boxes.
    /// Cut marks it "move": pasting into another box moves the item; pasting into a folder moves the file out of the box.
    /// </summary>
    void CopyItem(string path, bool move)
    {
        var previous = cut?.From; cut = move ? (this, path) : null;
        var data = new DataObject(); data.SetFileDropList([path]);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(move ? 2 : 1)));
        try { Clipboard.SetDataObject(data, true); } catch (ExternalException) { SetMessage("The clipboard is busy. Try again."); cut = null; }
        if (previous != null && previous != this && !previous.IsDisposed) previous.MarkCut();
        MarkCut();
    }
    /// <summary>Fades the cut tile, as Explorer does, without rebuilding the tiles.</summary>
    void MarkCut()
    {
        foreach (var tile in items.Controls.OfType<ItemTile>())
        {
            bool ghost = cut is { } c && c.From == this && tile.Tag is string p && p.Equals(c.Path, StringComparison.OrdinalIgnoreCase);
            if (tile.Ghost != ghost) { tile.Ghost = ghost; tile.Invalidate(); }
        }
    }
    static bool CanPaste { get { try { return Clipboard.ContainsFileDropList(); } catch (ExternalException) { return false; } } }
    internal void Paste()
    {
        if (owner.Busy || State.Photo != null || State.PhotoFolder != null || !CanPaste) return;
        var paths = Clipboard.GetFileDropList().Cast<string>().ToArray();
        if (cut is { } c && paths.Length == 1 && paths[0].Equals(c.Path, StringComparison.OrdinalIgnoreCase))
        {
            cut = null; Clipboard.Clear();
            if (c.From != this && !c.From.IsDisposed && c.From.State.Items.Contains(c.Path)) owner.MoveItem(c.From, c.Path, this, State.Items.Count);
            else if (!c.From.IsDisposed) c.From.MarkCut();
            return;
        }
        _ = owner.ImportItemsAsync(this, paths);
    }
    /// <summary>Called on the app timer: once Explorer has moved a cut item's file away, take the item out of its box.</summary>
    internal static void SettleCut()
    {
        if (cut is not { } c || File.Exists(c.Path) || Directory.Exists(c.Path)) return;
        cut = null; if (!c.From.IsDisposed && c.From.State.Items.Remove(c.Path)) { c.From.RefreshItems(); c.From.owner.Save(); }
    }

    /// <summary>Edits the name in place over the tile, like the desktop. Shortcuts keep their hidden .lnk/.url extension.</summary>
    void BeginRename(ItemTile tile, string path)
    {
        if (owner.Busy || tile.IsDisposed) return;
        bool hidden = IsLink(path) || Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase);
        var editor = new TextBox { Text = tile.Text, Font = Theme.Label, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.IsLight(BackColor) ? Color.White : Theme.Mix(BackColor, Color.Black, .35f), ForeColor = Theme.InkOn(Theme.IsLight(BackColor) ? Color.White : BackColor), TextAlign = HorizontalAlignment.Center };
        var at = PointToClient(tile.PointToScreen(Point.Empty));
        editor.Bounds = new Rectangle(at.X - Theme.S(6), at.Y + Theme.S(44), tile.Width + Theme.S(12), editor.PreferredHeight);
        Controls.Add(editor); editor.BringToFront(); Activate(); editor.Focus();
        editor.Select(0, hidden || Directory.Exists(path) ? editor.Text.Length : Path.GetFileNameWithoutExtension(editor.Text).Length);
        bool done = false;
        void Finish(bool keep)
        {
            if (done) return; done = true;
            var name = editor.Text.Trim(); BeginInvoke(editor.Dispose);
            if (keep && name.Length > 0 && name != tile.Text) RenameItem(path, hidden ? name + Path.GetExtension(path) : name);
        }
        editor.KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Escape) { Finish(e.KeyCode == Keys.Enter); e.SuppressKeyPress = true; } };
        editor.LostFocus += (_, _) => Finish(true);
    }
    internal void RenameItem(string path, string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Trim('.').Length == 0) { SetMessage("Names can't contain \\ / : * ? \" < > |"); return; }
        var renamed = Path.Combine(Path.GetDirectoryName(path)!, name);
        try { if (Directory.Exists(path)) Directory.Move(path, renamed); else File.Move(path, renamed); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetMessage("Couldn't rename: " + e.Message); return; }
        // The same saved file can sit in several boxes (copy and paste between boxes).
        foreach (var box in owner.Windows)
        {
            bool changed = false;
            for (int i = 0; i < box.State.Items.Count; i++) if (box.State.Items[i].Equals(path, StringComparison.OrdinalIgnoreCase)) { box.State.Items[i] = renamed; changed = true; }
            if (changed) box.RefreshItems();
        }
        if (cut is { } c && c.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) cut = (c.From, renamed);
        owner.Save();
    }

    internal void RefreshItems()
    {
        items.SuspendLayout();
        foreach (Control c in items.Controls.Cast<Control>().ToArray()) c.Dispose();
        foreach (var path in State.Items.ToArray())
        {
            var name = Path.GetFileName(path);
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(path);
            var tile = new ItemTile { Text = name, AccessibleName = name, Accent = accent, Image = ShellImage.Load(path), Tag = path };
            // Like the desktop: press selects, double-click (or Enter) opens, press-and-move drags.
            void Open()
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { SetMessage("Couldn't open this item. Check its saved file."); }
            }
            tile.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) Open(); };
            tile.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && !e.Alt) { Open(); e.SuppressKeyPress = true; } else if (ItemKey(e.KeyData, tile, path)) e.SuppressKeyPress = true; };
            tips.SetToolTip(tile, owner.Library.Contains(path) ? name : name + "\nOriginal unavailable — drop it again to save a copy");
            Point? press = null;
            tile.MouseDown += (_, e) => { if (e.Button != MouseButtons.Middle) Select(tile); press = e.Button == MouseButtons.Left ? e.Location : null; };
            tile.MouseUp += (_, _) => press = null;
            tile.MouseMove += (_, e) =>
            {
                if (press is not { } start || e.Button != MouseButtons.Left || owner.Busy) return;
                var drag = SystemInformation.DragSize;
                if (Math.Abs(e.X - start.X) < drag.Width && Math.Abs(e.Y - start.Y) < drag.Height) return;
                press = null; tile.Capture = false; StartTileDrag(tile, path);
            };
            tile.Ghost = cut is { } c && c.From == this && c.Path.Equals(path, StringComparison.OrdinalIgnoreCase);
            var itemMenu = ItemMenu(tile, path, Open);
            tile.ContextMenuStrip = itemMenu;
            tile.Disposed += (_, _) => { tile.Image?.Dispose(); itemMenu.Dispose(); };
            EnableDrop(tile); items.Controls.Add(tile);
        }
        items.ResumeLayout(); scroller.Relayout(); items.Invalidate(); UpdateStatus();
    }
    /// <summary>Dev check: builds the saved boxes off-screen (without touching the layout) and prints their scroll metrics.</summary>
    internal static void ScrollCheck()
    {
        var states = System.Text.Json.JsonSerializer.Deserialize<List<State>>(File.ReadAllText(Path.Combine(Boxes.folder, "layout.json")))!;
        var app = Boxes.Detached();
        foreach (var s in states)
        {
            var box = new Box(app, s) { StartPosition = FormStartPosition.Manual, Opacity = 0 }; box.Show(); box.Location = new Point(-10000, -10000); Application.DoEvents();
            var tiles = box.items.Controls.Cast<Control>().ToArray();
            Console.WriteLine($"{s.Name}: box={box.Size} host={box.scroller.ClientSize} content={box.items.Bounds} max={box.scroller.Max} tiles={tiles.Length} lastBottom={(tiles.Length == 0 ? 0 : tiles.Max(t => t.Bottom))} tile={(tiles.Length == 0 ? Size.Empty : tiles[0].Size)}");
            box.Dispose();
        }
    }
    internal void RenderPreview()
    {
        // Exercise both populated and compact layouts without touching the user's library.
        State.Name = "Everyday"; title.Text = "Everyday"; Size = new Size(Theme.S(480), Theme.S(288));
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var sample in new[] { ("Project notes", Path.Combine(windows, "notepad.exe")), ("Design files", windows), ("Terminal", Path.Combine(windows, "System32", "cmd.exe")), ("Quick links", Path.Combine(windows, "explorer.exe")), ("Warhammer 40,000 Dawn of War - Definitive Edition", Path.Combine(windows, "System32", "mmc.exe")) })
        {
            var tile = new ItemTile { Text = sample.Item1, Accent = accent, Image = ShellImage.Load(sample.Item2) };
            tile.Disposed += (_, _) => tile.Image?.Dispose(); items.Controls.Add(tile);
        }
        PerformLayout(); scroller.Relayout();
        if (scroller.Max != 0 || scroller.ThumbShown) throw new InvalidOperationException($"Box whose items fit still scrolls ({scroller.Max}px) or shows a scrollbar");
        foreach (var name in new[] { "Notes", "Paint", "Calculator", "Settings", "Photos", "Clock" })
        {
            var tile = new ItemTile { Text = name, Accent = accent, Image = ShellImage.Load(Path.Combine(windows, "System32", "control.exe")) };
            tile.Disposed += (_, _) => tile.Image?.Dispose(); items.Controls.Add(tile);
        }
        PerformLayout(); LayoutChrome(); scroller.Relayout();
        if (scroller.Max <= 0 || !scroller.ThumbShown) throw new InvalidOperationException("Overflowing box did not become scrollable");
        scroller.ScrollTo(int.MaxValue); if (scroller.Offset != scroller.Max || items.Top != -scroller.Max) throw new InvalidOperationException("Scrolling did not move the items");
        scroller.ScrollTo(0);
        var output = Environment.GetEnvironmentVariable("BOXES_PREVIEW_DIR");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            items.Controls.OfType<ItemTile>().First().Selected = true; // show the selected style
            using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, ClientRectangle); bitmap.Save(Path.Combine(output, "boxes-populated.png"));
            swatches.Current = Color.FromArgb(255, Color.FromArgb(State.Colour)); Menus.RenderPreview(menu, Path.Combine(output, "menu-box.png"), 3);
            using (var itemMenu = ItemMenu(new ItemTile(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe"), () => { })) Menus.RenderPreview(itemMenu, Path.Combine(output, "menu-item.png"), 7);
            foreach (Control c in items.Controls.Cast<Control>().ToArray()) c.Dispose();
            Size = new Size(Theme.S(288), Theme.S(240)); PerformLayout(); LayoutChrome();
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
        if (disposing) { reel?.Dispose(); grid?.Dispose(); tips.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class TilePanel : FlowLayoutPanel
{
    internal Color Accent;
    int? marker;
    internal TilePanel() { DoubleBuffered = true; }
    /// <summary>Shows where a dragged tile will land (null hides it).</summary>
    internal void ShowMarker(int? index) { if (marker == index) return; marker = index; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (marker is { } index && Controls.Count > 0)
        {
            // A rounded accent bar in the gap before the target slot (or after the last tile).
            var tiles = Controls.Cast<Control>().ToArray(); var before = index < tiles.Length;
            var t = tiles[before ? index : tiles.Length - 1]; int width = Math.Max(2, Theme.S(3));
            int x = before ? t.Left - t.Margin.Left - width / 2 : t.Right + t.Margin.Right - width / 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var bar = Theme.Round(new RectangleF(x, t.Top + Theme.S(6), width, t.Height - Theme.S(12)), width / 2f);
            using var brush = new SolidBrush(Accent); e.Graphics.FillPath(brush, bar);
        }
        if (Controls.Count != 0) return;
        // Empty state: a soft dashed drop zone with one quiet line of text.
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var zone = new RectangleF(Padding.Left + .5f, Theme.S(2) + .5f, ClientSize.Width - Padding.Horizontal - 1, ClientSize.Height - Padding.Bottom - Theme.S(2) - 1);
        if (zone.Width < 20 || zone.Height < 20) return;
        using var pen = new Pen(Theme.Mix(BackColor, Accent, .35f), Math.Max(1, Theme.Scale)) { DashPattern = [4, 3] };
        using var outline = Theme.Round(zone, Theme.S(10)); e.Graphics.DrawPath(pen, outline);
        TextRenderer.DrawText(e.Graphics, "Drop icons here", Theme.Body, Rectangle.Round(zone), Theme.MutedOn(BackColor), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Borderless icon button: invisible until hovered, like a modern web toolbar.</summary>
internal sealed class GhostButton : Control
{
    internal char Glyph; internal Color Accent = Color.SteelBlue;
    bool hover;
    internal GhostButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); Cursor = Cursors.Hand; TabStop = true; }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(e); base.OnKeyDown(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (hover || Focused)
        {
            using var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(7));
            using var fill = new SolidBrush(Theme.Mix(BackColor, Accent, .22f)); e.Graphics.FillPath(fill, path);
        }
        using var glyph = Menus.Glyph(Glyph, hover ? Theme.InkOn(BackColor) : Theme.MutedOn(BackColor), Theme.S(16));
        e.Graphics.DrawImage(glyph, (Width - glyph.Width) / 2, (Height - glyph.Height) / 2);
    }
}

/// <summary>Resize handle in the bottom-right corner; three faint dots appear on hover.</summary>
internal sealed class Grip : Control
{
    internal Color Accent = Color.SteelBlue;
    bool hover;
    internal Grip() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); if (!hover) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var dot = new SolidBrush(Theme.Mix(BackColor, Theme.InkOn(BackColor), .45f)); float d = Theme.S(2) + 1, step = Theme.S(4);
        foreach (var (x, y) in new[] { (2, 0), (1, 1), (2, 1), (0, 2), (1, 2), (2, 2) })
            e.Graphics.FillEllipse(dot, Width - Theme.S(6) - (2 - x) * step - d / 2, Height - Theme.S(6) - (2 - y) * step - d / 2, d, d);
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
    static readonly int LineHeight = TextRenderer.MeasureText("Ag", Theme.Label, Size.Empty, TextFormatFlags.NoPadding).Height;
    /// <summary>Drawn faded while this tile is being dragged.</summary>
    internal bool Ghost;
    internal bool Selected;
    // Buttons normally suppress double-clicks; tiles open on double-click like desktop icons.
    internal ItemTile() : base() { SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true); Size = new Size(Theme.S(88), Theme.S(18) + Theme.S(32) + Theme.S(8) + LineHeight * 2); Margin = new Padding(Theme.S(2)); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var back = Parent?.BackColor ?? BackColor;
        e.Graphics.Clear(back); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if ((Hover || Selected) && !Ghost)
        {
            using var path = Theme.Round(new RectangleF(.5f, .5f, Width - 2, Height - 2), Theme.S(8));
            using var brush = new SolidBrush(Theme.Mix(back, Accent, Selected ? (Hover ? .3f : .25f) : .14f)); e.Graphics.FillPath(brush, path);
            if (Selected) { using var ring = new Pen(Theme.Mix(back, Accent, .6f), Math.Max(1, Theme.Scale)); e.Graphics.DrawPath(ring, path); }
        }
        int icon = Theme.S(32), top = Theme.S(8);
        if (Image != null)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using var fade = new System.Drawing.Imaging.ImageAttributes(); fade.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = Ghost ? .3f : 1f });
            e.Graphics.DrawImage(Image, new Rectangle((Width - icon) / 2, top, icon, icon), 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, fade);
        }
        // Two lines at most; TextBoxControl drops a partially visible third line and EndEllipsis marks the cut.
        var text = new Rectangle(Theme.S(4), top + icon + Theme.S(6), Width - Theme.S(8), LineHeight * 2);
        TextRenderer.DrawText(e.Graphics, Text, Theme.Label, text, Ghost ? Theme.Mix(back, Theme.InkOn(back), .3f) : Hover || Selected ? Theme.InkOn(back) : Theme.Mix(back, Theme.InkOn(back), .88f),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
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
