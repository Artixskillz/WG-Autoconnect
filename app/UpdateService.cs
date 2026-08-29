using System.Net.Http;

namespace WgAutoconnect;

public static class UpdateService
{
    /// <summary>
    /// Downloads the installer to destPath. Returns false on any failure —
    /// the caller falls back to opening the release page in a browser.
    /// </summary>
    public static async Task<bool> DownloadInstallerAsync(string url, string destPath)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WG-Autoconnect");
            http.Timeout = TimeSpan.FromMinutes(5);

            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            long? expected = resp.Content.Headers.ContentLength;

            await using (var file = File.Create(destPath))
                await resp.Content.CopyToAsync(file);

            var actual = new FileInfo(destPath).Length;
            if (expected.HasValue && actual != expected.Value)
            {
                Logger.Error($"Update download incomplete ({actual}/{expected} bytes).");
                return false;
            }
            if (actual < 1_000_000)   // the self-contained installer is tens of MB
            {
                Logger.Error($"Update download suspiciously small ({actual} bytes) — refusing to run it.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Update download failed: {ex.Message}");
            return false;
        }
    }
}
