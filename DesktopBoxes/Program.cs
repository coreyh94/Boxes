using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
namespace DesktopBoxes;

static class Program
{
    [STAThread] static void Main(string[] args) { if (args.Contains("--desktop-check")) { ApplicationConfiguration.Initialize(); using var icons = new DesktopIcons(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "desktop-check.json"), JsonSerializer.Serialize(new { icons.Positions, icons.Spacing, icons.Origin, icons.AnchorInset })); return; } if (args.Contains("--verify-layout")) { ApplicationConfiguration.Initialize(); using var desktop = new DesktopIcons(); var states = JsonSerializer.Deserialize<List<State>>(File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopBoxes", "layout.json")))!; var bounds = states.Select(s => new Rectangle(s.X, s.Y, s.Width, s.Height)).ToArray(); if (bounds.Any(b => desktop.Positions.Any(p => b.IntersectsWith(Placement.Cell(p, desktop.Spacing, desktop.AnchorInset))))) throw new Exception("Desktop icon overlaps a saved box"); if (bounds.Where((b, i) => bounds.Where((_, j) => j != i).Any(b.IntersectsWith)).Any()) throw new Exception("Boxes overlap"); return; } if (args.Contains("--scroll-check")) { ApplicationConfiguration.Initialize(); Box.ScrollCheck(); return; } if (args.Contains("--library-tests")) { LibraryTests.Run(); return; } if (args.Contains("--placement-tests")) { PlacementTests.Run(); return; } if (args.Contains("--update-tests")) { UpdateTests.Run(); return; } if (args.Length == 2 && args[0] == "--write-icon") { using var ico = File.Create(args[1]); AppIcon.WriteIco(ico); return; } using var mutex = new Mutex(true, args.Contains("--smoke-test") ? "Local\\DesktopBoxes.SmokeTest" : "Local\\DesktopBoxes", out var first); if (!first) return; ApplicationConfiguration.Initialize(); Application.Run(new Boxes(args.Contains("--smoke-test"))); }
}
public class State
{
    public string Name { get; set; } = "My box";
    public int X { get; set; } = 48; public int Y { get; set; } = 48;
    public int Width { get; set; } = 384; public int Height { get; set; } = 288;
    public int Colour { get; set; } = SwatchRow.Palette[0].Colour.ToArgb();
    /// <summary>Text of a note box; null for every other kind of box.</summary>
    public string? Note { get; set; }
    /// <summary>Sticky, Pastel or Dark (see <see cref="BoxStyle"/>); null follows the default style in Settings.</summary>
    public string? Style { get; set; }
    internal BoxStyle? Look { get => Enum.TryParse<BoxStyle>(Style, out var s) ? s : null; set => Style = value?.ToString(); }
    /// <summary>Window opacity in percent (30–100); null follows the default in Settings.</summary>
    public int? Opacity { get; set; }
    public List<string> Items { get; set; } = [];
    /// <summary>Saved copy of the photo for a photo box; null for an ordinary item box.</summary>
    public string? Photo { get; set; }
    /// <summary>Folder a slideshow box cycles through (read live, not copied); null otherwise.</summary>
    public string? PhotoFolder { get; set; }
    public int SlideSeconds { get; set; } = 20;
    /// <summary>Hides the header (title and ⋯ button); the box then moves by dragging its body, and right-click opens the menu.</summary>
    public bool HideTitle { get; set; }
}
public sealed class Boxes : ApplicationContext
{
    internal readonly List<Box> Windows = [];
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 750 };
    internal static readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopBoxes");
    bool ready; string lastStatus = ""; internal bool Busy; bool exitPending; readonly bool smokeMode;
    internal readonly ItemLibrary Library;
    internal AppSettings Settings = new(); int pinAttempts;
    // Windows sends several display notifications while monitors come and go; act once things settle.
    readonly System.Windows.Forms.Timer displaysSettled = new() { Interval = 1500 };
    bool userHidden;
    // First check a minute after start (the network may still be coming up at sign-in), then daily.
    readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 60_000 };
    Updates.Release? update; bool installing;
    readonly string file = Path.Combine(folder, "layout.json");
    /// <summary>Owner for dev checks: no tray, no timers, never saves.</summary>
    Boxes(string libraryRoot) { smokeMode = true; Library = new ItemLibrary(libraryRoot); tray = new NotifyIcon(); }
    internal static Boxes Detached() => new(Path.Combine(folder, "Library"));
    public Boxes(bool smoke = false)
    {
        smokeMode = smoke; Library = new ItemLibrary(smoke ? Path.Combine(Path.GetTempPath(), "Boxes-Smoke-" + Guid.NewGuid().ToString("N")) : Path.Combine(folder, "Library"));
        if (smoke)
        {
            tray = new NotifyIcon();
            var test = new Box(this, new State()); Windows.Add(test);
            test.Shown += (_, _) => test.BeginInvoke(async () =>
            {
                var source = Path.Combine(Path.GetTempPath(), "Boxes-SmokeSource-" + Guid.NewGuid().ToString("N") + ".url");
                try
                {
                    File.WriteAllText(source, "[InternetShortcut]\r\nURL=https://example.com/\r\n");
                    await ImportItemsAsync(test, [source]);
                    File.Delete(source);
                    if (test.State.Items.Count != 1 || !File.ReadAllText(test.State.Items[0]).Contains("URL=https://example.com/"))
                        throw new InvalidOperationException("Imported tile did not survive desktop deletion");
                    if (test.Controls.Count < 3) throw new InvalidOperationException("Layout missing");
                    test.State.Opacity = 60; test.ApplyOpacity();
                    if (Math.Abs(test.Opacity - .6) > .01) throw new InvalidOperationException("Box opacity not applied");
                    if (Environment.GetEnvironmentVariable("BOXES_PREVIEW_DIR") is { } shots)
                    {
                        test.Activate(); await Task.Delay(400);
                        using var shot = new Bitmap(test.Width, test.Height); using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(test.Location, Point.Empty, test.Size);
                        shot.Save(Path.Combine(shots, "box-opacity.png"));
                    }
                    test.State.Opacity = null; test.ApplyOpacity();
                    if (!Theme.IsLight(test.BackColor)) throw new InvalidOperationException("Default style isn't Sticky note");
                    Settings.DefaultLook = BoxStyle.Dark; test.Recolour();
                    if (Theme.IsLight(test.BackColor)) throw new InvalidOperationException("Box didn't follow the default style");
                    test.State.Look = BoxStyle.Pastel; test.State.Opacity = 70; test.Recolour();
                    if (!Theme.IsLight(test.BackColor)) throw new InvalidOperationException("Box's own style ignored");
                    ResetBoxesToDefaults();
                    if (test.State.Style != null || test.State.Opacity != null || Theme.IsLight(test.BackColor) || test.Opacity != 1) throw new InvalidOperationException("Reset didn't restore the defaults");
                    Settings.DefaultLook = BoxStyle.Sticky; test.Recolour();
                    var noteState = System.Text.Json.JsonSerializer.Deserialize<State>(System.Text.Json.JsonSerializer.Serialize(new State { Name = "Shopping", Note = "Milk\r\nBread", Colour = SwatchRow.Palette[4].Colour.ToArgb(), X = test.Right + 48, Y = test.Top }))!;
                    var noteBox = new Box(this, noteState); Windows.Add(noteBox); noteBox.Show();
                    if (noteBox.NoteText != "Milk\r\nBread" || !noteBox.NoteShown) throw new InvalidOperationException("Note box lost its text");
                    noteBox.NoteText = "Milk\r\nBread\r\nEggs"; if (noteBox.State.Note != "Milk\r\nBread\r\nEggs") throw new InvalidOperationException("Note edits not kept");
                    if (Environment.GetEnvironmentVariable("BOXES_PREVIEW_DIR") is { } notes) { using var bmp = new Bitmap(noteBox.Width, noteBox.Height); noteBox.DrawToBitmap(bmp, new Rectangle(Point.Empty, noteBox.Size)); bmp.Save(Path.Combine(notes, "box-note.png")); }
                    Windows.Remove(noteBox); noteBox.Dispose();
                    var item = test.State.Items[0]; test.RenameItem(item, "Renamed site.url");
                    if (test.State.Items[0] != Path.Combine(Path.GetDirectoryName(item)!, "Renamed site.url") || !File.Exists(test.State.Items[0]) || File.Exists(item))
                        throw new InvalidOperationException("Item rename failed");
                    test.RenameItem(test.State.Items[0], "bad:name.url");
                    if (Path.GetFileName(test.State.Items[0]) != "Renamed site.url") throw new InvalidOperationException("Invalid name accepted");
                    Add();
                    if (Windows.Count != 2 || Windows[0].Bounds.IntersectsWith(Windows[1].Bounds)) throw new InvalidOperationException("New box failed");
                    // Moving between boxes and reordering within one keeps the same saved file.
                    var saved = test.State.Items[0]; var other = Windows[1];
                    MoveItem(test, saved, other, 0);
                    if (test.State.Items.Count != 0 || other.State.Items.SingleOrDefault() != saved || !File.Exists(saved)) throw new InvalidOperationException("Move between boxes failed");
                    if (other.SelectedTile?.Tag as string != saved || !other.SelectedTile.Selected) throw new InvalidOperationException("Moved item not selected");
                    Box.Select(null); if (other.SelectedTile != null) throw new InvalidOperationException("Selection did not clear");
                    other.State.Items.AddRange(["b", "c"]); MoveItem(other, saved, other, 3);
                    if (!other.State.Items.SequenceEqual(["b", "c", saved])) throw new InvalidOperationException("Reorder within a box failed: " + string.Join(",", other.State.Items));
                    other.State.Items.RemoveAll(i => i is "b" or "c"); MoveItem(other, saved, test, 0);
                    // Photo box: saved as an independent copy, sized to the photo's shape, and shown instead of items.
                    var wallpaper = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Wallpaper", "Windows", "img0.jpg");
                    var photoSource = Path.Combine(Path.GetTempPath(), "Boxes-SmokePhoto-" + Guid.NewGuid().ToString("N") + (File.Exists(wallpaper) ? ".jpg" : ".png"));
                    if (File.Exists(wallpaper)) File.Copy(wallpaper, photoSource);
                    else { using var generated = new Bitmap(640, 400); using (var g = Graphics.FromImage(generated)) g.Clear(Color.Teal); generated.Save(photoSource); }
                    try
                    {
                        await AddPhotoBoxAsync(photoSource);
                        var photoBox = Windows.Last(); File.Delete(photoSource);
                        if (photoBox.State.Photo is not { } kept || !Library.Contains(kept) || !File.Exists(kept) || !photoBox.PhotoShown) throw new InvalidOperationException("Photo box was not created from a saved copy");
                        if (photoBox.Width <= photoBox.Height || photoBox.Width % 48 != 0 || photoBox.Height % 48 != 0) throw new InvalidOperationException($"Landscape photo box badly sized: {photoBox.Size}");
                        if (Environment.GetEnvironmentVariable("BOXES_PREVIEW_DIR") is { } photoPreview) { photoBox.State.Name = "Weekend"; photoBox.RenderPhotoPreview(Path.Combine(photoPreview, "box-photo.png")); }
                    }
                    finally { File.Delete(photoSource); }
                    // Slideshow box: reads a folder live (no library copies), shows a photo, and advances to a different one.
                    var reelFolder = Path.Combine(Path.GetTempPath(), "Boxes-SmokeReel-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(Path.Combine(reelFolder, "nested"));
                    try
                    {
                        if (File.Exists(wallpaper)) File.Copy(wallpaper, Path.Combine(reelFolder, "wallpaper.jpg"));
                        foreach (var (name, colour) in new[] { ("one.png", Color.Teal), (Path.Combine("nested", "two.png"), Color.Coral) })
                        { using var generated = new Bitmap(400, 300); using (var g = Graphics.FromImage(generated)) g.Clear(colour); generated.Save(Path.Combine(reelFolder, name)); }
                        File.WriteAllText(Path.Combine(reelFolder, "notes.txt"), "not a photo");
                        int libraryEntries = Directory.Exists(Library.Root) ? Directory.GetDirectories(Library.Root).Length : 0;
                        AddSlideshowBox(reelFolder);
                        var reelBox = Windows.Last();
                        for (int i = 0; i < 100 && reelBox.Reel?.Current == null; i++) await Task.Delay(100);
                        if (reelBox.State.PhotoFolder != reelFolder || reelBox.Reel?.Current is not { } first || !reelBox.PhotoShown) throw new InvalidOperationException("Slideshow box did not show a photo from its folder");
                        await reelBox.Reel.NextAsync();
                        if (reelBox.Reel.Current == first || !Boxes.IsPhoto(reelBox.Reel.Current!)) throw new InvalidOperationException("Slideshow did not advance to another photo");
                        if ((Directory.Exists(Library.Root) ? Directory.GetDirectories(Library.Root).Length : 0) != libraryEntries) throw new InvalidOperationException("Slideshow copied photos into the library");
                        reelBox.SetInterval(10);
                        if (reelBox.Reel.IntervalSeconds != 10 || reelBox.State.SlideSeconds != 10) throw new InvalidOperationException("Slideshow interval did not change");
                        reelBox.SetTitleHidden(true);
                        if (reelBox.TitleShown || !reelBox.PhotoShown) throw new InvalidOperationException("Hiding the title bar failed");
                        if (Environment.GetEnvironmentVariable("BOXES_PREVIEW_DIR") is { } bare) { await Task.Delay(700); reelBox.RenderPhotoPreview(Path.Combine(bare, "box-no-title.png")); reelBox.RenderMenuPreview(Path.Combine(bare, "menu-slideshow.png")); }
                        reelBox.SetTitleHidden(false);
                        if (!reelBox.TitleShown) throw new InvalidOperationException("Showing the title bar failed");
                        if (Environment.GetEnvironmentVariable("BOXES_PREVIEW_DIR") is { } reelPreview && File.Exists(wallpaper))
                        {
                            while (Path.GetFileName(reelBox.Reel.Current) != "wallpaper.jpg") await reelBox.Reel.NextAsync();
                            await Task.Delay(700); reelBox.State.Name = "Holidays"; reelBox.RenderPhotoPreview(Path.Combine(reelPreview, "box-slideshow.png"));
                            await reelBox.Reel.NextAsync(); await Task.Delay(250); reelBox.RenderPhotoPreview(Path.Combine(reelPreview, "box-slideshow-fade.png"));
                        }
                    }
                    finally { Directory.Delete(reelFolder, true); }
                    test.State.Items.Clear(); test.RefreshItems(); test.RenderPreview();
                    if (Environment.GetEnvironmentVariable("BOXES_PREVIEW_DIR") is { } previews) { SettingsWindow.RenderPreview(this, previews); using var trayMenu = TrayMenu(); Menus.RenderPreview(trayMenu, Path.Combine(previews, "menu-tray.png"), 3); }
                    Console.WriteLine("Smoke test passed");
                }
                catch (Exception e)
                {
                    // Without this the exception is lost when the message loop exits, and the run reports success.
                    Console.Error.WriteLine("Smoke test failed: " + e); Environment.ExitCode = 1;
                }
                finally
                {
                    File.Delete(source);
                    foreach (var w in Windows.ToArray()) w.Dispose(); Windows.Clear();
                    // Smoke mode alone creates this unique temporary library.
                    if (Directory.Exists(Library.Root)) Directory.Delete(Library.Root, true);
                    ExitThread();
                }
            });
            test.Show(); return;
        }
        Directory.CreateDirectory(folder);
        Settings = AppSettings.Load(); Startup.Sync(Settings.StartWithWindows);
        tray = new NotifyIcon { Icon = AppIcon.Create(SystemInformation.SmallIconSize), Text = "Boxes", Visible = true, ContextMenuStrip = TrayMenu() };
        tray.DoubleClick += (_, _) => Add();
        tray.BalloonTipClicked += (_, _) => InstallUpdate();
        updateTimer.Tick += async (_, _) => { updateTimer.Interval = 24 * 60 * 60 * 1000; await CheckForUpdateAsync(); }; updateTimer.Start();
        if (File.Exists(file)) try { foreach (var s in JsonSerializer.Deserialize<List<State>>(File.ReadAllText(file)) ?? []) { if (!Settings.StylesFollowDefault && s.Style == nameof(BoxStyle.Sticky)) s.Style = null; Add(s); } } catch (JsonException) { File.Copy(file, file + ".invalid-" + DateTime.Now.Ticks); }
        if (Windows.Count == 0) Add();
        if (!Settings.StylesFollowDefault) { Settings.StylesFollowDefault = true; Settings.Save(); }
        FitToScreens(); ready = true; Save(); ProtectIcons();
        displaysSettled.Tick += (_, _) => { displaysSettled.Stop(); FitToScreens(); ProtectIcons(); };
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        if (Windows.Count > 0) Windows[0].BeginInvoke(async () => await MigrateItemsAsync());
        timer.Tick += (_, _) => { Native.KeepOnDesktop(Windows); ProtectIcons(); PinTrayOnce(); Box.SettleCut(); }; timer.Start();
    }
    void DisplaysChanged(object? sender, EventArgs e) { displaysSettled.Stop(); displaysSettled.Start(); }
    /// <summary>Places every box for the current monitors (see Placement.Fit). Saved positions are never changed here.</summary>
    internal void FitToScreens()
    {
        if (Windows.Any(b => b.Moving)) return;
        var fit = Placement.Fit([.. Windows.Select(b => b.Home)], [.. Screen.AllScreens.Select(s => s.WorkingArea)]);
        for (int i = 0; i < Windows.Count; i++)
        {
            var box = Windows[i];
            if (fit[i] is not { } spot) { box.Hide(); continue; }
            if (box.Bounds != spot) box.Bounds = spot;
            if (!userHidden && !box.Visible) box.Show();
        }
        var hidden = fit.Count(r => r == null); if (hidden > 0) Status($"{hidden} box{(hidden == 1 ? "" : "es")} hidden: not enough screen space");
    }
    /// <summary>Explorer registers the tray icon shortly after it first appears; pin it beside the clock once per executable, then respect the user's choice.</summary>
    void PinTrayOnce()
    {
        if (pinAttempts > 80 || string.Equals(Settings.TrayPinnedFor, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return;
        pinAttempts++; if (TrayPin.Set(true)) { Settings.TrayPinnedFor = Application.ExecutablePath; Settings.Save(); }
    }
    /// <summary>Scheduled checks stay quiet unless there's something new; a manual check always reports back.</summary>
    internal async Task CheckForUpdateAsync(bool manual = false)
    {
        if (!manual && !Settings.CheckForUpdates) return;
        var found = await Updates.LatestAsync();
        if (found == null || found.Version <= Updates.Current)
        {
            if (manual) tray.ShowBalloonTip(5000, found == null ? "Couldn't check for updates" : "Boxes is up to date",
                found == null ? "GitHub couldn't be reached. Try again later." : $"You have the latest version, {Updates.Current}.", ToolTipIcon.Info);
            return;
        }
        if (!manual && found.Version == update?.Version) return;
        update = found;
        tray.ShowBalloonTip(15000, $"Boxes {found.Version} is available", Updates.Installed ? "Click to install it. Your boxes and saved items are kept." : "Click to download it from GitHub.", ToolTipIcon.Info);
    }
    void InstallUpdate() { if (update is { } release && !installing) _ = InstallUpdateAsync(release); }
    async Task InstallUpdateAsync(Updates.Release release)
    {
        installing = true; if (Updates.Installed) Status($"Downloading Boxes {release.Version}…");
        try { await Updates.InstallAsync(release); }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Status("Update failed: " + e.Message); }
        finally { installing = false; }
    }
    /// <summary>Drops every box's own style and opacity so they all follow the defaults in Settings.</summary>
    internal void ResetBoxesToDefaults()
    {
        foreach (var box in Windows) { box.State.Style = null; box.State.Opacity = null; box.Recolour(); box.ApplyOpacity(); }
        Save();
    }
    internal ContextMenuStrip TrayMenu()
    {
        var menu = Menus.Create(Color.FromArgb(255, Color.FromArgb(0x739FF4)));
        var toggle = Menus.Item("Hide boxes", Menus.Icons.Hide, () =>
        {
            userHidden = Windows.Any(b => b.Visible);
            if (userHidden) foreach (var b in Windows) b.Hide();
            else { FitToScreens(); ProtectIcons(); }
        });
        var updates = Menus.Item("Check for updates", Menus.Icons.Update, () => { if (update != null) InstallUpdate(); else _ = CheckForUpdateAsync(manual: true); });
        menu.Opening += (_, _) =>
        {
            updates.Text = update == null ? "Check for updates" : Updates.Installed ? $"Install Boxes {update.Version}" : $"Get Boxes {update.Version}…";
            bool shown = Windows.Any(b => b.Visible);
            toggle.Text = shown ? "Hide boxes" : "Show boxes"; toggle.Image?.Dispose(); toggle.Image = Menus.Glyph(shown ? Menus.Icons.Hide : Menus.Icons.Show, Theme.Muted);
        };
        menu.Items.AddRange([
            Menus.Spacer(), new MenuHeader(() => { int items = Windows.Sum(b => b.State.Items.Count); return $"{Windows.Count} box{(Windows.Count == 1 ? "" : "es")} · {items} item{(items == 1 ? "" : "s")}"; }),
            Menus.Separator(),
            Menus.Item("New box", Menus.Icons.Add, () => Add()),
            Menus.Item("New note box", Menus.Icons.Note, () => Add(note: true)),
            Menus.Item("New photo box…", Menus.Icons.Photo, () => _ = AddPhotoBoxAsync()),
            Menus.Item("New slideshow box…", Menus.Icons.Slideshow, () => AddSlideshowBox()), toggle,
            Menus.Item("Saved items", Menus.Icons.Folder, () => { Directory.CreateDirectory(Library.Root); Process.Start(new ProcessStartInfo(Library.Root) { UseShellExecute = true }); }),
            Menus.Item("Settings…", Menus.Icons.Settings, () => SettingsWindow.Open(this)), updates,
            Menus.Separator(),
            Menus.Item("Exit", Menus.Icons.Exit, RequestExit), Menus.Spacer()]);
        return menu;
    }
    internal void Add(State? state = null, bool note = false)
    {
        if (state == null)
        {
            var screens = Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
            var slot = Placement.FindBox(new Size(384, 288), Windows.Select(b => b.Bounds).ToArray(), screens);
            if (slot == null) { Status("No free space for a new box"); return; }
            state = new State { X = slot.Value.X, Y = slot.Value.Y };
            if (note) { state.Name = "Note"; state.Note = ""; state.Colour = SwatchRow.Palette[4].Colour.ToArgb(); }
        }
        var b = new Box(this, state); Windows.Add(b);
        if (ready && !ClearIcons(Windows.Select(w => w.Bounds).ToArray())) { Windows.Remove(b); b.Dispose(); return; }
        b.Show(); if (ready) Save();
    }
    internal void Status(string message) { if (message != lastStatus) { lastStatus = message; Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "status.log"), DateTime.Now + " " + message + Environment.NewLine); } if (tray != null) tray.Text = message.Length > 63 ? message[..63] : message; }
    bool ClearIcons(Rectangle[] bounds)
    {
        if (bounds.Length == 0) return true;
        try
        {
            using var desktop = new DesktopIcons();
            var plan = Placement.Arrange(desktop.Positions, desktop.Spacing, bounds, Screen.AllScreens.Select(s => s.WorkingArea).ToArray(), desktop.AnchorInset);
            if (plan == null) { Status("Not enough desktop space; boxes hidden. Free space, then Show."); return false; }
            desktop.Apply(plan); Status("Desktop Boxes — double-click for a new box"); return true;
        }
        catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { Status("Explorer unavailable: " + e.Message); return false; }
    }
    internal void ProtectIcons() { if (!ready || Windows.Any(b => b.Moving)) return; if (!ClearIcons(Windows.Where(b => b.Visible).Select(b => b.Bounds).ToArray())) foreach (var b in Windows) b.Hide(); }
    internal bool Place(Box box, Rectangle proposed)
    {
        var area = Screen.FromRectangle(proposed).WorkingArea;
        if (!area.Contains(proposed) || Windows.Any(b => b != box && b.Bounds.IntersectsWith(proposed))) { Status("Box placement blocked: keep boxes inside free desktop space"); return false; }
        return ClearIcons(Windows.Select(b => b == box ? proposed : b.Bounds).ToArray());
    }
    /// <summary>Moves a saved item between boxes, or to a new slot in the same box. The library file is untouched.</summary>
    internal void MoveItem(Box from, string path, Box to, int index)
    {
        if (Busy || !from.State.Items.Contains(path)) return;
        if (from == to)
        {
            int old = from.State.Items.IndexOf(path); if (index > old) index--;
            if (index == old) return;
            from.State.Items.RemoveAt(old); to.InsertItem(path, index);
        }
        else
        {
            from.RemoveItem(path);
            // Already in the target box: the move just merges the two.
            if (!to.State.Items.Contains(path, StringComparer.OrdinalIgnoreCase)) to.InsertItem(path, index);
        }
        to.SelectPath(path); Save();
    }
    internal void Save() { if (smokeMode) return; File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(Windows.Select(b => b.State), new JsonSerializerOptions { WriteIndented = true })); File.Move(file + ".tmp", file, true); }
    internal void RequestExit() { if (Busy) { exitPending = true; Status("Finishing saving items before exit"); } else ExitThread(); }
    internal static readonly string[] PhotoExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];
    internal static bool IsPhoto(string path) => PhotoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    static string? PickPhoto()
    {
        using var dialog = new OpenFileDialog { Title = "Choose a photo", Filter = "Photos|" + string.Join(";", PhotoExtensions.Select(e => "*" + e)), InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
    /// <summary>Copies a photo into the library (same independent-copy rule as items). Null if it couldn't be saved.</summary>
    async Task<string?> SavePhotoAsync(string source, Box? box)
    {
        if (Busy) return null;
        Busy = true; box?.SetMessage("Saving your photo…");
        try { var stored = await Library.ImportAsync(source); box?.SetMessage(null); return stored; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status("Could not save photo: " + e.Message); box?.SetMessage("Couldn't save that photo — original kept"); return null; }
        finally { Busy = false; if (exitPending) ExitThread(); }
    }
    /// <summary>New box showing one photo, sized to the photo's shape and placed in free grid space.</summary>
    internal async Task AddPhotoBoxAsync(string? source = null)
    {
        source ??= PickPhoto(); if (source == null) return;
        if (!IsPhoto(source)) { Status("Choose a JPG, PNG, BMP, GIF or TIFF photo"); return; }
        var stored = await SavePhotoAsync(source, null); if (stored == null || exitPending) return;
        var size = PhotoPanel.BoxSizeFor(stored, Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(1920, 1032));
        var slot = Placement.FindBox(size, [.. Windows.Select(b => b.Bounds)], [.. Screen.AllScreens.Select(s => s.WorkingArea)]);
        if (slot == null) { Status("No free space for a new photo box"); return; }
        Add(new State { Name = Path.GetFileNameWithoutExtension(source), X = slot.Value.X, Y = slot.Value.Y, Width = size.Width, Height = size.Height, Photo = stored, Colour = SwatchRow.Palette[5].Colour.ToArgb() });
    }
    static string? PickFolder(string? current)
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose a folder of photos for the slideshow", UseDescriptionForTitle = true, SelectedPath = current ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }
    /// <summary>New box cycling through a folder's photos every 20 s. The folder is read live, so nothing is copied.</summary>
    internal void AddSlideshowBox(string? folder = null)
    {
        folder ??= PickFolder(null); if (folder == null) return;
        var size = PhotoPanel.BoxSizeFor(null, Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(1920, 1032));
        var slot = Placement.FindBox(size, [.. Windows.Select(b => b.Bounds)], [.. Screen.AllScreens.Select(s => s.WorkingArea)]);
        if (slot == null) { Status("No free space for a new slideshow box"); return; }
        Add(new State { Name = new DirectoryInfo(folder).Name, X = slot.Value.X, Y = slot.Value.Y, Width = size.Width, Height = size.Height, PhotoFolder = folder, Colour = SwatchRow.Palette[5].Colour.ToArgb() });
    }
    internal void ChangeFolder(Box box, string? folder = null)
    {
        folder ??= PickFolder(box.State.PhotoFolder); if (folder == null) return;
        box.State.PhotoFolder = folder; box.ShowPhoto(); Save();
    }
    internal async Task ChangePhotoAsync(Box box, string? source = null)
    {
        source ??= PickPhoto(); if (source == null) return;
        if (!IsPhoto(source)) { box.SetMessage("That isn't a photo Boxes can show"); return; }
        if (await SavePhotoAsync(source, box) is not { } stored) return;
        box.State.Photo = stored; box.ShowPhoto(); Save();
    }
    internal async Task ImportItemsAsync(Box box, string[] paths)
    {
        if (Busy) return;
        Busy = true; box.SetMessage("Saving your items…");
        int failed = 0;
        try
        {
            foreach (var source in paths)
            {
                try { var stored = await Library.ImportAsync(source); if (!box.State.Items.Contains(stored, StringComparer.OrdinalIgnoreCase)) { box.State.Items.Add(stored); Save(); } }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed++; Status("Could not save item: " + e.Message); }
            }
            box.RefreshItems(); box.SetMessage(failed == 0 ? null : $"{failed} item(s) couldn't be saved — original kept");
        }
        finally { Busy = false; if (exitPending) ExitThread(); }
    }
    async Task MigrateItemsAsync()
    {
        var legacy = Windows.Where(b => b.State.Items.Any(p => !Library.Contains(p))).ToArray();
        if (legacy.Length == 0) return;
        Busy = true;
        try
        {
            File.Copy(file, file + ".before-library-" + DateTime.Now.Ticks, true);
            foreach (var box in legacy)
            {
                int failed = 0; box.SetMessage("Keeping your items safe…");
                for (int i = 0; i < box.State.Items.Count; i++)
                {
                    var source = box.State.Items[i]; if (Library.Contains(source)) continue;
                    try { box.State.Items[i] = await Library.ImportAsync(source); Save(); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed++; Status("Could not preserve existing item: " + e.Message); }
                }
                box.RefreshItems(); box.SetMessage(failed == 0 ? null : $"{failed} original(s) missing or unavailable — drop again");
            }
        }
        finally { Busy = false; if (exitPending) ExitThread(); }
    }
    protected override void ExitThreadCore() { SystemEvents.DisplaySettingsChanged -= DisplaysChanged; displaysSettled.Dispose(); updateTimer.Dispose(); if (Windows.Count > 0) Save(); timer.Dispose(); tray.Dispose(); foreach (var b in Windows.ToArray()) b.Dispose(); base.ExitThreadCore(); }
}
