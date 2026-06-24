using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClipTyper
{
    /// <summary>
    /// Checks for updates by comparing the current assembly version against
    /// the latest GitHub release tag for the ClipTyper repository.
    /// </summary>
    public static class UpdateChecker
    {
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/unpaved028/ClipTyper/releases/latest";

        private const string ReleasesPageUrl =
            "https://github.com/unpaved028/ClipTyper/releases/latest";

        /// <summary>
        /// Result of an update check.
        /// </summary>
        public record UpdateCheckResult(
            bool IsUpdateAvailable,
            string CurrentVersion,
            string LatestVersion,
            string ReleaseUrl);

        /// <summary>
        /// Queries the GitHub API for the latest release and compares it
        /// against the currently running assembly version.
        /// </summary>
        /// <returns>An <see cref="UpdateCheckResult"/>, or null if the check failed.</returns>
        public static async Task<UpdateCheckResult?> CheckAsync()
        {
            try
            {
                var currentVersion = GetCurrentVersion();

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.Add("User-Agent", $"ClipTyper/{currentVersion}");

                var response = await client.GetAsync(ReleasesApiUrl);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var tagName = doc.RootElement.GetProperty("tag_name").GetString();
                if (string.IsNullOrEmpty(tagName)) return null;

                // Strip leading 'v' from tag (e.g., "v1.3.0" → "1.3.0")
                var latestVersionStr = tagName.TrimStart('v', 'V');

                if (!Version.TryParse(latestVersionStr, out var latestVersion))
                    return null;

                if (!Version.TryParse(currentVersion, out var current))
                    return null;

                // Get the HTML URL for the release page
                var htmlUrl = doc.RootElement.TryGetProperty("html_url", out var urlProp)
                    ? urlProp.GetString() ?? ReleasesPageUrl
                    : ReleasesPageUrl;

                return new UpdateCheckResult(
                    IsUpdateAvailable: latestVersion > current,
                    CurrentVersion: currentVersion,
                    LatestVersion: latestVersionStr,
                    ReleaseUrl: htmlUrl);
            }
            catch
            {
                return null; // Network failure or parse error
            }
        }

        /// <summary>
        /// Gets the current application version from the assembly metadata.
        /// Falls back to "0.0.0" if the version cannot be determined.
        /// </summary>
        public static string GetCurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null) return "0.0.0";

            // Return Major.Minor.Build (skip Revision)
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
