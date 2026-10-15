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
            string ReleaseUrl,
            string ReleaseNotes = "");

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

                if (!TryParseReleaseVersion(latestVersionStr, out _, out _) ||
                    !TryParseReleaseVersion(currentVersion, out _, out _))
                    return null;

                // Get the HTML URL for the release page
                var htmlUrl = doc.RootElement.TryGetProperty("html_url", out var urlProp)
                    ? urlProp.GetString() ?? ReleasesPageUrl
                    : ReleasesPageUrl;

                // Get the release notes (body field)
                var bodyNotes = doc.RootElement.TryGetProperty("body", out var bodyProp)
                    ? bodyProp.GetString() ?? ""
                    : "";

                return new UpdateCheckResult(
                    IsUpdateAvailable: IsNewerRelease(latestVersionStr, currentVersion),
                    CurrentVersion: currentVersion,
                    LatestVersion: latestVersionStr,
                    ReleaseUrl: htmlUrl,
                    ReleaseNotes: bodyNotes);
            }
            catch
            {
                return null; // Network failure or parse error
            }
        }

        /// <summary>
        /// Turns the Markdown release notes from GitHub into plain text for a text box:
        /// headings without '#', bullets as '•', no emphasis markers, Windows line breaks.
        /// </summary>
        public static string FormatReleaseNotes(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

            var lines = new System.Collections.Generic.List<string>();
            foreach (string rawLine in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = rawLine.TrimEnd().Replace("**", "").Replace("`", "");
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith('#'))
                {
                    line = trimmed.TrimStart('#').Trim();
                }
                else if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    line = "• " + trimmed.Substring(2).Trim();
                }

                // Collapse runs of empty lines
                if (line.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0)) continue;
                lines.Add(line);
            }

            return string.Join("\r\n", lines).Trim();
        }

        /// <summary>
        /// True when <paramref name="latest"/> (from /releases/latest, never a pre-release) should be
        /// offered to a user running <paramref name="current"/>. A pre-release of the same version
        /// (1.8.0-beta1 while 1.8.0 is out) counts as older, so beta testers get the final release.
        /// </summary>
        internal static bool IsNewerRelease(string latest, string current)
        {
            if (!TryParseReleaseVersion(latest, out var latestVersion, out bool latestIsPrerelease) ||
                !TryParseReleaseVersion(current, out var currentVersion, out bool currentIsPrerelease))
            {
                return false;
            }

            if (latestVersion != currentVersion)
            {
                return latestVersion > currentVersion;
            }
            return currentIsPrerelease && !latestIsPrerelease;
        }

        /// <summary>
        /// Parses "v1.8.0", "1.8.0-beta1" or "1.8.0+abc123" into the numeric version and a pre-release flag.
        /// </summary>
        private static bool TryParseReleaseVersion(string text, out Version version, out bool isPrerelease)
        {
            string s = text.Trim().TrimStart('v', 'V');
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            int dash = s.IndexOf('-');
            isPrerelease = dash >= 0;
            if (isPrerelease) s = s.Substring(0, dash);

            if (Version.TryParse(s, out var parsed))
            {
                // Compare 1.8 and 1.8.0 as equal
                version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
                return true;
            }
            version = new Version(0, 0, 0);
            return false;
        }

        /// <summary>
        /// Gets the current application version, including a pre-release suffix such as "-beta1"
        /// (from the informational version that CI sets from the tag). Build metadata after '+' is dropped.
        /// Falls back to the assembly version, then "0.0.0".
        /// </summary>
        public static string GetCurrentVersion()
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                int plus = informational.IndexOf('+');
                string withoutMetadata = plus >= 0 ? informational.Substring(0, plus) : informational;
                if (TryParseReleaseVersion(withoutMetadata, out _, out _))
                {
                    return withoutMetadata;
                }
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null) return "0.0.0";

            // Return Major.Minor.Build (skip Revision)
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
