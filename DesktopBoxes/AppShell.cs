using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace DesktopBoxes;

/// <summary>App-wide preferences, separate from the box layout. Missing file means defaults (startup on).</summary>
public sealed class AppSettings
{
    public bool StartWithWindows { get; set; } = true;
    /// <summary>Executable path whose tray icon has already been pinned once; later user choices are left alone.</summary>
    public string? TrayPinnedFor { get; set; }
    static string File => Path.Combine(Boxes.folder, "settings.json");
    internal static AppSettings Load() { try { return System.IO.File.Exists(File) ? JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(File)) ?? new() : new(); } catch (JsonException) { return new(); } }
    internal void Save() { Directory.CreateDirectory(Boxes.folder); System.IO.File.WriteAllText(File + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); System.IO.File.Move(File + ".tmp", File, true); }
}

/// <summary>Per-user sign-in launch via the Run key, which also appears in Settings › Apps › Startup.</summary>
internal static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", Name = "DesktopBoxes";
    static string Command => "\"" + Application.ExecutablePath + "\"";
    /// <summary>True when registered and not switched off in Windows Settings › Startup (first byte 3 = disabled).</summary>
    internal static bool Enabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey); if (run?.GetValue(Name) == null) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey); return !(approved?.GetValue(Name) is byte[] b && b.Length > 0 && b[0] == 3);
        }
    }
    /// <summary>Explicit user choice: turning on also clears a Windows Settings "off" for this entry.</summary>
    internal static void Set(bool on)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!on) { run.DeleteValue(Name, false); return; }
        run.SetValue(Name, Command);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true); approved?.DeleteValue(Name, false);
    }
    /// <summary>On launch: keep the registered path pointing at this executable without overriding a Windows Settings "off".</summary>
    internal static void Sync(bool on)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!on) run.DeleteValue(Name, false); else if (run.GetValue(Name) as string != Command) run.SetValue(Name, Command);
    }
}

/// <summary>Windows 11 keeps per-app tray visibility in NotifyIconSettings; IsPromoted=1 shows the icon beside the clock rather than in the ^ overflow.</summary>
internal static class TrayPin
{
    const string Root = @"Control Panel\NotifyIconSettings";
    static RegistryKey? Find(bool write)
    {
        using var root = Registry.CurrentUser.OpenSubKey(Root); if (root == null) return null;
        foreach (var name in root.GetSubKeyNames())
        {
            var key = root.OpenSubKey(name, write);
            if (key?.GetValue("ExecutablePath") is string path && string.Equals(path, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return key;
            key?.Dispose();
        }
        return null;
    }
    /// <summary>Null until Explorer has registered this executable's icon.</summary>
    internal static bool? Pinned { get { using var key = Find(false); return key == null ? null : key.GetValue("IsPromoted") is int i && i == 1; } }
    internal static bool Set(bool on) { using var key = Find(true); if (key == null) return false; key.SetValue("IsPromoted", on ? 1 : 0, RegistryValueKind.DWord); return true; }
}

/// <summary>The Boxes mark: four palette-coloured tiles on a dark rounded square, drawn per size so small icons stay crisp.</summary>
internal static class AppIcon
{
    static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];
    static readonly Color[] Tiles = [.. new[] { 0x739FF4, 0x65C8AD, 0xDFBA78, 0xAC91ED }.Select(c => Color.FromArgb(255, Color.FromArgb(c)))];
    internal static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float s = size, inset = Math.Max(.5f, s * .03f);
        using (var plate = Theme.Round(new RectangleF(inset, inset, s - inset * 2, s - inset * 2), s * .22f))
        using (var fill = new SolidBrush(Color.FromArgb(255, 25, 30, 41))) g.FillPath(fill, plate);
        float pad = s * .2f, gap = Math.Max(1.5f, s * .08f), tile = (s - pad * 2 - gap) / 2;
        for (int i = 0; i < 4; i++)
        {
            var r = new RectangleF(pad + (i % 2) * (tile + gap), pad + (i / 2) * (tile + gap), tile, tile);
            using var path = Theme.Round(r, Math.Max(.8f, tile * .24f)); using var brush = new SolidBrush(Tiles[i]); g.FillPath(brush, path);
        }
        return bmp;
    }
    /// <summary>Multi-resolution .ico with PNG frames (supported since Vista).</summary>
    internal static void WriteIco(Stream output)
    {
        var frames = Sizes.Select(size => { using var bmp = Draw(size); using var ms = new MemoryStream(); bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }).ToArray();
        using var w = new BinaryWriter(output, System.Text.Encoding.UTF8, true);
        w.Write((short)0); w.Write((short)1); w.Write((short)frames.Length);
        int offset = 6 + 16 * frames.Length;
        for (int i = 0; i < frames.Length; i++)
        {
            w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i])); w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i])); w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32); w.Write(frames[i].Length); w.Write(offset); offset += frames[i].Length;
        }
        foreach (var f in frames) w.Write(f);
    }
    internal static Icon Create(Size size) { using var ms = new MemoryStream(); WriteIco(ms); ms.Position = 0; return new Icon(ms, size); }
}

/// <summary>Small dark preferences window, opened from the tray or any box's options menu.</summary>
internal sealed class SettingsWindow : Form
{
    static SettingsWindow? open;
   internal static void Open(Boxes app) { if (open is { IsDisposed: false }) { open.Activate(); return; } open = new SettingsWindow(app); open.Show(); open.Activate(); }

    SettingsWindow(Boxes app)
    {
        Text = "Boxes settings"; Icon = AppIcon.Create(new Size(Theme.S(32), Theme.S(32))); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(Theme.S(420), Theme.S(236));
        BackColor = Color.FromArgb(21, 25, 34); ForeColor = Theme.Ink; Font = Theme.Body; TopMost = true;
        var heading = new Label { Text = "Settings", Font = Theme.Title, ForeColor = Theme.Ink, Location = new Point(Theme.S(22), Theme.S(18)), AutoSize = true };
        var startup = Option("Start Boxes with Windows", "Opens quietly in the tray when you sign in.", Theme.S(62), Startup.Enabled);
        startup.CheckedChanged += (_, _) => { Startup.Set(startup.Checked); app.Settings.StartWithWindows = startup.Checked; app.Settings.Save(); };
        var pinned = TrayPin.Pinned;
        var pin = Option("Show icon next to the clock", pinned == null ? "Available once Windows has registered the tray icon." : "Otherwise it sits under the ^ overflow arrow.", Theme.S(122), pinned == true);
        pin.Enabled = pinned != null; pin.CheckedChanged += (_, _) => TrayPin.Set(pin.Checked);
        var done = new SoftButton { Text = "Done", Size = new Size(Theme.S(92), Theme.S(32)), Location = new Point(ClientSize.Width - Theme.S(114), ClientSize.Height - Theme.S(50)), Accent = Color.FromArgb(255, Color.FromArgb(0x739FF4)) };
        done.Click += (_, _) => Close(); AcceptButton = CancelButton = done;
        Controls.AddRange([heading, done]);
    }
    CheckBox Option(string label, string detail, int top, bool value)
    {
        var box = new CheckBox { Text = label, Checked = value, AutoSize = true, Location = new Point(Theme.S(24), top), ForeColor = Theme.Ink, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        box.FlatAppearance.BorderColor = Theme.Muted; box.FlatAppearance.CheckedBackColor = box.FlatAppearance.MouseOverBackColor = Color.FromArgb(37, 45, 60);
        var note = new Label { Text = detail, AutoSize = true, Location = new Point(Theme.S(44), top + Theme.S(26)), Font = Theme.Small, ForeColor = Theme.Muted };
        Controls.AddRange([box, note]); return box;
    }
    /// <summary>Smoke-test render of the window and icon sizes into BOXES_PREVIEW_DIR; never touches settings.</summary>
    internal static void RenderPreview(Boxes app, string output)
    {
        using (var window = new SettingsWindow(app) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false }) { window.Show(); Application.DoEvents(); using var bmp = new Bitmap(window.Width, window.Height); window.DrawToBitmap(bmp, new Rectangle(Point.Empty, window.Size)); bmp.Save(Path.Combine(output, "settings.png")); }
        using var sheet = new Bitmap(16 + Sizes.Sum(s => Math.Min(s, 64) + 16), 96); using var g = Graphics.FromImage(sheet); g.Clear(Color.FromArgb(243, 243, 243));
        int x = 16; foreach (var size in Sizes) { using var icon = AppIcon.Draw(size); int shown = Math.Min(size, 64); g.DrawImage(icon, x, 16, shown, shown); x += shown + 16; }
        sheet.Save(Path.Combine(output, "icon.png"));
    }
    static readonly int[] Sizes = [16, 20, 24, 32, 48, 64];
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4); }
    protected override void OnFormClosed(FormClosedEventArgs e) { open = null; base.OnFormClosed(e); }
}
