using System.Security.Cryptography;
using System.Text;

namespace DesktopBoxes;

/// <summary>Owns independent, durable copies. Layout paths only change after an import completes.</summary>
internal sealed class ItemLibrary(string root)
{
    internal string Root { get; } = Path.GetFullPath(root);

    internal bool Contains(string path) => Path.GetFullPath(path).StartsWith(
        Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    internal Task<string> ImportAsync(string source) => Task.Run(() => Import(source));

    internal string Import(string source)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        if (Contains(source))
        {
            if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("Saved item is missing", source);
            return source;
        }
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToUpperInvariant())));
        var directory = Path.Combine(Root, id);
        var name = Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(name)) throw new IOException("Drop a file or folder rather than an entire drive.");
        var destination = Path.Combine(directory, "content", name);
        if (File.Exists(destination) || Directory.Exists(destination)) return destination;
        if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("Original item is missing", source);
        // The earlier copy of this source was renamed inside a box; keep it and save a fresh copy beside it.
        if (Directory.Exists(directory)) { directory += "-" + Guid.NewGuid().ToString("N")[..8]; destination = Path.Combine(directory, "content", name); }
        // Never copy the library into itself, or follow junctions/symlinks outside a dropped folder.
        if (Root.Equals(source, StringComparison.OrdinalIgnoreCase) || Root.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("This folder contains the Boxes library.");
        Directory.CreateDirectory(Root);
        var staging = Path.Combine(Root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "content"));
            Copy(source, Path.Combine(staging, "content", name));
            File.WriteAllText(Path.Combine(staging, "source.json"), System.Text.Json.JsonSerializer.Serialize(new { OriginalPath = source }));
            try { Directory.Move(staging, directory); }
            catch (IOException) when (File.Exists(destination) || Directory.Exists(destination)) { /* Another import won. */ }
            return destination;
        }
        finally
        {
            // This unique staging directory is created by this operation, never a user-selected path.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    static void Copy(string source, string destination)
    {
        var attributes = File.GetAttributes(source);
        if ((attributes & FileAttributes.ReparsePoint) != 0 && Unsupported(attributes, Native.ReparseTag(source)) is { } problem)
            throw new IOException(problem);
        if ((attributes & FileAttributes.Directory) == 0)
        {
            File.Copy(source, destination);
            return;
        }
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            Copy(entry, Path.Combine(destination, Path.GetFileName(entry)));
    }

    const uint CloudTag = 0x9000001A, CloudTagMask = 0xFFFF0FFF;
    const FileAttributes RecallOnOpen = (FileAttributes)0x40000, RecallOnDataAccess = (FileAttributes)0x400000;

    /// <summary>
    /// Why a reparse point can't be copied, or null if it can. OneDrive (and other cloud-files providers) mark every
    /// synced item as a reparse point, so those are fine once downloaded; junctions, symlinks and online-only
    /// placeholders are not.
    /// </summary>
    internal static string? Unsupported(FileAttributes attributes, uint tag)
    {
        if ((tag & CloudTagMask) != CloudTag)
            return "This item is a junction or symbolic link. Drop the original file or folder instead.";
        var offline = FileAttributes.Offline | ((attributes & FileAttributes.Directory) == 0 ? RecallOnOpen | RecallOnDataAccess : 0);
        if ((attributes & offline) != 0)
            return "This item is only stored online. Right-click it, choose \"Always keep on this device\", then try again.";
        return null;
    }
}
