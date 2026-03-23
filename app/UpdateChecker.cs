using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace WgAutoconnect;

public static class UpdateChecker
{
    private const string ReleasesUrl = "https://api.github.com/repos/Artixskillz/WG-Autoconnect/releases/latest";

    public static async Task CheckForUpdateAsync(Action<string, string> onUpdateAvailable, Action? onUpToDate = null)
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
                onUpdateAvailable(release.TagName, release.HtmlUrl ?? "");
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
    }
}
