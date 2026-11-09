using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CnCNew.Launcher;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>One release of the project on GitHub, reduced to what the launcher reads.</summary>
public sealed partial record ReleaseInfo(string Tag, SemanticVersion Version, string Name, string Notes, bool PreRelease, DateTimeOffset? PublishedAt, string PageUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary><c>CnCNew-Client-&lt;version&gt;-win-x64.zip</c>: the client, the patcher and the two scripts.</summary>
    public ReleaseAsset? ClientPackage => Assets.FirstOrDefault(asset => ClientName().IsMatch(asset.Name));

    /// <summary>The checksum file of the client package: <c>&lt;zip&gt;.sha256</c> (or the name without <c>.zip</c>).</summary>
    public ReleaseAsset? ClientHash => HashOf(ClientPackage);

    /// <summary><c>CnCNew-Launcher-&lt;version&gt;.exe</c>: the launcher itself.</summary>
    public ReleaseAsset? Launcher => Assets.FirstOrDefault(asset => LauncherName().IsMatch(asset.Name));

    public ReleaseAsset? LauncherHash => HashOf(Launcher);

    private ReleaseAsset? HashOf(ReleaseAsset? target)
    {
        if (target is null) return null;
        var stem = Path.GetFileNameWithoutExtension(target.Name);
        return Assets.FirstOrDefault(asset => asset.Name.Equals(target.Name + ".sha256", StringComparison.OrdinalIgnoreCase)) ??
               Assets.FirstOrDefault(asset => asset.Name.Equals(stem + ".sha256", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"^CnCNew-Client-.+-win-x64\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex ClientName();

    [GeneratedRegex(@"^CnCNew-Launcher-.+\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex LauncherName();
}

public enum ReleaseCheckStatus { Found, NoRelease, RateLimited, Offline, Failed }

/// <summary>The answer to "what is the newest release?": either the release or, in plain words, why there is none.</summary>
public sealed record ReleaseCheck(ReleaseCheckStatus Status, ReleaseInfo? Release, string Message, DateTimeOffset? RetryAfter = null)
{
    public bool Succeeded => Status == ReleaseCheckStatus.Found;
}

/// <summary>
/// Reads the releases of the project through the public GitHub REST API (no token, no account; 60 requests an hour per address). Every failure
/// is an answer, never an exception, so the launcher can say what to do: offline, rate limited, no release published yet.
/// </summary>
public sealed class ReleaseClient(HttpClient http, string repository = ReleaseClient.DefaultRepository, string apiBase = "https://api.github.com")
{
    public const string DefaultRepository = "cpfaffinger/CnCNew";

    public Task<ReleaseCheck> GetLatestAsync(CancellationToken cancellation = default) => GetAsync($"{apiBase}/repos/{repository}/releases/latest", cancellation);

    public Task<ReleaseCheck> GetByTagAsync(string tag, CancellationToken cancellation = default) =>
        GetAsync($"{apiBase}/repos/{repository}/releases/tags/{Uri.EscapeDataString(tag)}", cancellation);

    private async Task<ReleaseCheck> GetAsync(string url, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new ReleaseCheck(ReleaseCheckStatus.NoRelease, null, "No release has been published yet. Look again later.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var retry = ResetTime(response);
                var when = retry is { } at ? $" It will work again at {at.ToLocalTime():HH:mm}." : " Try again in a while.";
                return new ReleaseCheck(ReleaseCheckStatus.RateLimited, null, "GitHub is answering too many requests from this internet connection." + when, retry);
            }
            if (!response.IsSuccessStatusCode)
                return new ReleaseCheck(ReleaseCheckStatus.Failed, null, $"GitHub answered with an error ({(int)response.StatusCode}). Try again in a while.");
            var text = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            return Parse(text);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return new ReleaseCheck(ReleaseCheckStatus.Offline, null, "GitHub cannot be reached. Check the internet connection and try again.");
        }
    }

    /// <summary>Reads the JSON of one release. Anything that is not the expected shape is a failure with a message, not a crash.</summary>
    public static ReleaseCheck Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var version = SemanticVersion.TryParse(tag);
            if (version is null) return new ReleaseCheck(ReleaseCheckStatus.Failed, null, $"The newest release is named '{tag}', which is not a version number.");
            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var asset in list.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    var url = asset.GetProperty("browser_download_url").GetString();
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                    assets.Add(new ReleaseAsset(name, url, asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0));
                }
            DateTimeOffset? published = root.TryGetProperty("published_at", out var date) && date.ValueKind == JsonValueKind.String &&
                                        DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
            var release = new ReleaseInfo(tag, version, Text(root, "name"), Text(root, "body"), root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True,
                published, Text(root, "html_url"), assets);
            return new ReleaseCheck(ReleaseCheckStatus.Found, release, $"Version {version} is the newest release.");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new ReleaseCheck(ReleaseCheckStatus.Failed, null, "GitHub's answer could not be understood. Try again later.");
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static DateTimeOffset? ResetTime(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values) && long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (response.Headers.RetryAfter?.Delta is { } delta) return DateTimeOffset.UtcNow + delta;
        return null;
    }
}
