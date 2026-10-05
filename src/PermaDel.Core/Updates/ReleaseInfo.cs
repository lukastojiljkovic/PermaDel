using System.Text.Json;
using System.Text.Json.Serialization;

namespace PermaDel.Core.Updates;

/// <summary>
/// One release from GitHub's <c>/releases/latest</c>, with the installer and
/// its checksum sidecar already matched by exact asset name. A
/// <see langword="null"/> <see cref="InstallerUrl"/> or
/// <see cref="SidecarUrl"/> means that asset is not published (or its URL is
/// not an acceptable GitHub download URL), and the UI offers the release page.
/// </summary>
public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string Body,
    string PageUrl,
    string? InstallerName,
    string? InstallerUrl,
    string? SidecarName,
    string? SidecarUrl);

/// <summary>Reads the release JSON. Internal: the checker owns the network.</summary>
internal static class ReleaseReader
{
    internal const string Owner = "lukastojiljkovic";
    internal const string Repository = "PermaDel";
    internal const string RepositoryUrl = "https://github.com/" + Owner + "/" + Repository;
    internal const string ReleasePageUrl = RepositoryUrl + "/releases/latest";
    internal const string AssetPrefix = "PermaDel";
    internal const string ApiLatestUrl =
        "https://api.github.com/repos/" + Owner + "/" + Repository + "/releases/latest";

    private const string DownloadPathPrefix = "/" + Owner + "/" + Repository + "/releases/download/";

    /// <summary>
    /// Parses <paramref name="json"/> into a release. Returns
    /// <see langword="false"/> with a short reason when the payload or its tag
    /// is unusable.
    /// </summary>
    public static bool TryParse(string json, out ReleaseInfo? release, out string? failure)
    {
        release = null;
        failure = null;

        ReleaseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ReleaseDto>(json);
        }
        catch (JsonException)
        {
            failure = "the release data was not valid JSON";
            return false;
        }

        if (string.IsNullOrEmpty(dto?.TagName))
        {
            failure = "the release data did not contain a tag";
            return false;
        }
        if (!UpdateVersion.TryParseTag(dto.TagName, out var version))
        {
            failure = "the release tag is not a version";
            return false;
        }

        var installerName = $"{AssetPrefix}-{version.ToString(3)}-Setup.exe";
        string? installerUrl = null;
        string? sidecarName = null;
        string? sidecarUrl = null;
        foreach (var asset in dto.Assets ?? [])
        {
            if (asset.Name == installerName && IsAcceptableAssetUrl(asset.BrowserDownloadUrl))
                installerUrl = asset.BrowserDownloadUrl;
            else if (asset.Name == installerName + ".sha256" && IsAcceptableAssetUrl(asset.BrowserDownloadUrl))
            {
                sidecarName = asset.Name;
                sidecarUrl = asset.BrowserDownloadUrl;
            }
        }

        release = new ReleaseInfo(
            version,
            dto.TagName,
            dto.Body ?? string.Empty,
            string.IsNullOrEmpty(dto.HtmlUrl) ? ReleasePageUrl : dto.HtmlUrl,
            installerUrl is null ? null : installerName,
            installerUrl,
            sidecarName,
            sidecarUrl);
        return true;
    }

    /// <summary>
    /// Only <c>https</c> URLs on <c>github.com</c> under this repository's
    /// release download path are followed. GitHub redirects those to its asset
    /// CDN; the checksum covers what is actually downloaded.
    /// </summary>
    internal static bool IsAcceptableAssetUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(DownloadPathPrefix, StringComparison.Ordinal);
    }

    private sealed record ReleaseDto(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] AssetDto[]? Assets);

    private sealed record AssetDto(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);
}
