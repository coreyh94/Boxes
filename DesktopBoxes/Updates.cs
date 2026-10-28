using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopBoxes;

/// <summary>
/// Asks GitHub for the latest Boxes release. The request carries no user data; only its version number is compared.
/// Installed copies download and run the new setup (checked against GitHub's SHA-256); portable copies open the release page.
/// </summary>
internal static class Updates
{
    internal sealed record Release(Version Version, string Page, string? Setup, string? Sha256);
    const string Latest = "https://api.github.com/repos/coreyh94/Boxes/releases/latest";
    internal static readonly Version Current = Trim(typeof(Updates).Assembly.GetName().Version!);
    static Version Trim(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
    static readonly HttpClient http = CreateClient();
    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Boxes/" + Current);
        return client;
    }

    /// <summary>The installer puts an Inno Setup uninstaller beside Boxes.exe; the portable exe has none.</summary>
    internal static bool Installed => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    internal static Release? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v'), out var version)) return null;
        string? setup = null, sha = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != "Boxes-Setup.exe") continue;
            setup = asset.GetProperty("browser_download_url").GetString();
            if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:")) sha = d["sha256:".Length..];
        }
        return new(Trim(version), root.GetProperty("html_url").GetString()!, setup, sha);
    }

    /// <summary>The latest published release, or null if GitHub can't be reached.</summary>
    internal static async Task<Release?> LatestAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request); if (!response.IsSuccessStatusCode) return null;
            return Parse(await response.Content.ReadAsStringAsync());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>Runs the new setup silently; it closes Boxes, upgrades in place (layout and saved items untouched) and starts it again.</summary>
    internal static async Task InstallAsync(Release release)
    {
        if (!Installed || release.Setup == null) { Process.Start(new ProcessStartInfo(release.Page) { UseShellExecute = true }); return; }
        var bytes = await http.GetByteArrayAsync(release.Setup);
        if (release.Sha256 != null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The download didn't match GitHub's checksum.");
        var path = Path.Combine(Path.GetTempPath(), $"Boxes-Setup-{release.Version}.exe");
        await File.WriteAllBytesAsync(path, bytes);
        Process.Start(new ProcessStartInfo(path, "/SILENT") { UseShellExecute = true });
    }
}

internal static class UpdateTests
{
    internal static void Run()
    {
        const string json = """
            {"tag_name":"v9.2.3","html_url":"https://github.com/coreyh94/Boxes/releases/tag/v9.2.3","assets":[
             {"name":"Boxes-Portable.exe","browser_download_url":"https://example.com/p.exe","digest":"sha256:aa"},
             {"name":"Boxes-Setup.exe","browser_download_url":"https://example.com/s.exe","digest":"sha256:bb"}]}
            """;
        var release = Updates.Parse(json) ?? throw new Exception("Release not parsed");
        if (release.Version != new Version(9, 2, 3) || release.Setup != "https://example.com/s.exe" || release.Sha256 != "bb") throw new Exception("Wrong release details");
        if (!(release.Version > Updates.Current)) throw new Exception("Newer version not detected");
        var same = Updates.Parse(json.Replace("v9.2.3", "v" + Updates.Current))!;
        if (same.Version > Updates.Current) throw new Exception("Current version treated as an update");
        if (Updates.Parse("""{"tag_name":"nightly","html_url":"x","assets":[]}""") != null) throw new Exception("Bad tag accepted");
    }
}
