using System.Net;
using System.Net.Http.Headers;

namespace PermaDel.Core.Updates;

/// <summary>The outcome of one check against GitHub's latest release.</summary>
public enum UpdateCheckStatus
{
    /// <summary>The running version is the latest, or nothing is published yet.</summary>
    UpToDate,

    /// <summary>A newer release is published.</summary>
    UpdateAvailable,

    /// <summary>The repository has no releases yet (HTTP 404).</summary>
    NoReleases,

    /// <summary>GitHub's API rate limit was reached (HTTP 403 or 429).</summary>
    RateLimited,

    /// <summary>The check could not be completed; <see cref="UpdateCheckResult.Detail"/> says why.</summary>
    Failed,
}

/// <summary>
/// One check's result. <paramref name="Release"/> is the parsed release when
/// the payload was readable, even for <see cref="UpdateCheckStatus.UpToDate"/>.
/// </summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release, string? Detail);

/// <summary>
/// Reads <c>GET /repos/lukastojiljkovic/PermaDel/releases/latest</c> and
/// compares the tag with the running version. HTTP and parse failures come
/// back as results, never as exceptions (caller cancellation still throws).
/// </summary>
public sealed class UpdateChecker
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly Version _currentVersion;

    public UpdateChecker(HttpClient http, Version currentVersion)
    {
        _http = http;
        _currentVersion = currentVersion;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseReader.ApiLatestUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", $"PermaDel/{_currentVersion.ToString(3)}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return new UpdateCheckResult(UpdateCheckStatus.NoReleases, null, null);
                case HttpStatusCode.Forbidden:
                case HttpStatusCode.TooManyRequests:
                    return new UpdateCheckResult(UpdateCheckStatus.RateLimited, null, "GitHub's rate limit was reached.");
            }
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(UpdateCheckStatus.Failed, null, $"GitHub returned HTTP {(int)response.StatusCode}.");

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!ReleaseReader.TryParse(json, out var release, out var failure) || release is null)
                return new UpdateCheckResult(UpdateCheckStatus.Failed, null, failure ?? "the release data could not be read");

            return release.Version.CompareTo(_currentVersion) > 0
                ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release, null)
                : new UpdateCheckResult(UpdateCheckStatus.UpToDate, release, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null, "the request timed out");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null, ex.Message);
        }
    }
}
