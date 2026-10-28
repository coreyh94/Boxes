using System.Runtime.InteropServices;
using System.Text.Json;

namespace DesktopBoxes;

internal static class LibraryTests
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "Boxes-LibraryTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var library = new ItemLibrary(Path.Combine(root, "Library"));
            var desktop = Path.Combine(root, "Desktop"); Directory.CreateDirectory(desktop);
            var document = Path.Combine(desktop, "notes.txt"); File.WriteAllText(document, "independent copy");
            var saved = library.Import(document); File.Delete(document);
            Require(File.ReadAllText(saved) == "independent copy", "File did not survive deletion");
            Require(library.Import(document) == saved && library.Import(saved) == saved, "Import not idempotent");
            var restored = JsonSerializer.Deserialize<State>(JsonSerializer.Serialize(new State { Items = [saved] }))!;
            Require(File.ReadAllText(restored.Items[0]) == "independent copy", "Saved item lost after layout reload");
            var other = Path.Combine(root, "Other"); Directory.CreateDirectory(other);
            var duplicateName = Path.Combine(other, "notes.txt"); File.WriteAllText(duplicateName, "second document");
            var second = library.Import(duplicateName);
            Require(second != saved && File.ReadAllText(saved) == "independent copy", "Same-name collision");
            var metadataName = Path.Combine(desktop, "source.json"); File.WriteAllText(metadataName, "actual user data");
            Require(File.ReadAllText(library.Import(metadataName)) == "actual user data", "Metadata clobbered a file");
            var folder = Path.Combine(desktop, "Work"); Directory.CreateDirectory(Path.Combine(folder, "Nested"));
            File.WriteAllText(Path.Combine(folder, "Nested", "data.txt"), "folder data");
            var savedFolder = library.Import(folder); Directory.Delete(folder, true);
            Require(File.ReadAllText(Path.Combine(savedFolder, "Nested", "data.txt")) == "folder data", "Folder did not survive deletion");
            var url = Path.Combine(desktop, "Website.url"); File.WriteAllText(url, "[InternetShortcut]\r\nURL=https://example.com/\r\n");
            var savedUrl = library.Import(url); File.Delete(url);
            Require(File.ReadAllText(savedUrl).Contains("URL=https://example.com/"), "URL shortcut lost");
            TestShortcut(root, desktop, library);
            var failed = false;
            try { library.Import(Path.Combine(desktop, "missing.txt")); } catch (FileNotFoundException) { failed = true; }
            Require(failed, "Missing original silently accepted");
            failed = false;
            try { library.Import(root); } catch (IOException) { failed = true; }
            Require(failed, "Recursive self-import allowed");
            failed = false; try { library.Import(library.Root); } catch (IOException) { failed = true; }
            Require(failed, "Library root imported into itself");
            var locked = Path.Combine(desktop, "locked.txt"); File.WriteAllText(locked, "locked");
            using (var stream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                failed = false; try { library.Import(locked); } catch (IOException) { failed = true; }
                Require(failed, "Locked file silently accepted");
            }
            Require(!Directory.EnumerateDirectories(library.Root, ".import-*").Any(), "Partial import was published or leaked");
        }
        finally
        {
            // root is a fresh, test-owned directory; never derived from a dropped item.
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    static void TestShortcut(string root, string desktop, ItemLibrary library)
    {
        object? shell = null, shortcut = null, copied = null;
        try
        {
            var target = Path.Combine(root, "target.txt"); File.WriteAllText(target, "shortcut target");
            var path = Path.Combine(desktop, "My app.lnk");
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            shortcut = ((dynamic)shell).CreateShortcut(path);
            ((dynamic)shortcut).TargetPath = target; ((dynamic)shortcut).Arguments = "--test";
            ((dynamic)shortcut).WorkingDirectory = root; ((dynamic)shortcut).Description = "Preserve me";
            ((dynamic)shortcut).Save();
            var saved = library.Import(path); File.Delete(path);
            copied = ((dynamic)shell).CreateShortcut(saved);
            Require(((dynamic)copied).TargetPath == target && ((dynamic)copied).Arguments == "--test"
                && ((dynamic)copied).WorkingDirectory == root && ((dynamic)copied).Description == "Preserve me", "Shortcut metadata lost");
            Require(File.Exists(((dynamic)copied).TargetPath), "Saved shortcut target missing");
        }
        finally { foreach (var obj in new[] { copied, shortcut, shell }) if (obj != null) Marshal.ReleaseComObject(obj); }
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
