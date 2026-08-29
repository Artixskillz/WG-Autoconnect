using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace WgAutoconnect;

public static class UpdateChecker
{
    private const string ReleasesUrl = "https://api.github.com/repos/Artixskillz/WG-Autoconnect/releases/latest";

    /// <summary>
    /// onUpdateAvailable(tag, releasePageUrl, setupAssetUrl?) — setupAssetUrl is
    /// the direct download for the installer when the release has one attached,
    /// enabling in-app updates; null means browser-only fallback.
    /// </summary>
    public static async Task CheckForUpdateAsync(
        Action<string, string, string?> onUpdateAvailable, Action? onUpToDate = null)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WG-Autoconnect");
            http.Timeout = TimeSpan.FromSeconds(10);

            var release = await http.GetFromJsonAsync<GitHubRelease>(ReleasesUrl);
            if (release?.TagName == null) return;

            var current = Assembly.GetExecutingAssembly().GetName().Version;
            if (current == null) return;

            // Parse tag like "v1.0.0" or "1.0.0"
            var tag = release.TagName.TrimStart('v');
            if (!Version.TryParse(tag, out var latest)) return;

            if (latest > current)
            {
                var setupUrl = release.Assets?
                    .FirstOrDefault(a => string.Equals(a.Name, "WG-Autoconnect-Setup.exe",
                        StringComparison.OrdinalIgnoreCase))?.DownloadUrl;
                onUpdateAvailable(release.TagName, release.HtmlUrl ?? "", setupUrl);
            }
            else
                onUpToDate?.Invoke();
        }
        catch
        {
            // Silently ignore — update check is best-effort
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? DownloadUrl { get; set; }
    }
}
