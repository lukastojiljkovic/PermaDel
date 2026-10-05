using System.Security.Cryptography;

namespace PermaDel.Core.Updates;

/// <summary>Download progress: bytes written so far, and the total when the server sent one.</summary>
public sealed record UpdateProgress(long BytesReceived, long? TotalBytes);

/// <summary>
/// A download attempt's outcome. On failure <see cref="Error"/> is the message
/// the UI shows and <see cref="ReleasePageUrl"/> is set so it can offer the
/// release page; the partial or bad file has already been deleted.
/// </summary>
public sealed record UpdateDownloadResult(bool Success, string? Path, string? Error, string? ReleasePageUrl);

/// <summary>
/// Downloads the installer into <c>%LOCALAPPDATA%\PermaDel\Updates\</c>,
/// hashing while it streams and verifying the sidecar before reporting success.
/// </summary>
public sealed class UpdateDownloader
{
    internal const string MissingInstallerMessage =
        "This release has no installer asset. Open the release page to update by hand.";
    internal const string MissingChecksumMessage =
        "This release has no checksum file, so the installer can't be verified. Open the release page to update by hand.";
    internal const string MalformedChecksumMessage =
        "The checksum file for this release is malformed. Open the release page to update by hand.";
    internal const string MismatchMessage =
        "The downloaded installer did not match its checksum. The file was deleted. Open the release page to update by hand.";

    /// <summary>Per-user, so a non-admin process cannot swap the file between the two hash checks.</summary>
    internal static string DefaultUpdatesDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PermaDel", "Updates");

    private static readonly TimeSpan SidecarTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly string _directory;

    /// <param name="updatesDirectory">Overridable so tests never touch the real per-user folder.</param>
    public UpdateDownloader(HttpClient http, string? updatesDirectory = null)
    {
        _http = http;
        _directory = updatesDirectory ?? DefaultUpdatesDirectory;
    }

    public async Task<UpdateDownloadResult> DownloadAsync(
        ReleaseInfo release,
        CancellationToken cancellationToken = default,
        IProgress<UpdateProgress>? progress = null)
    {
        Directory.CreateDirectory(_directory);
        var fileName = release.InstallerName ?? $"PermaDel-{release.Version.ToString(3)}-Setup.exe";
        var target = Path.Combine(_directory, fileName);
        RemoveOtherFiles(target);

        if (release.InstallerUrl is null)
        {
            TryDelete(target);
            return Failure(release, MissingInstallerMessage);
        }
        // A null sidecar URL means the checksum was not published; without it
        // the download cannot be verified, so it is refused before anything is
        // fetched.
        if (release.SidecarUrl is null)
        {
            TryDelete(target);
            return Failure(release, MissingChecksumMessage);
        }

        string expectedHash;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SidecarTimeout);
            var sidecar = await _http.GetStringAsync(release.SidecarUrl, timeout.Token);
            if (!SidecarParser.TryParse(sidecar, fileName, out expectedHash))
            {
                TryDelete(target);
                return Failure(release, MalformedChecksumMessage);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryDelete(target);
            return Failure(release, "The installer could not be downloaded: the request for its checksum timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            TryDelete(target);
            return Failure(release, $"The installer could not be downloaded: {ex.Message}");
        }

        string actualHash;
        try
        {
            using var response = await _http.GetAsync(
                release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                TryDelete(target);
                return Failure(
                    release,
                    $"The installer could not be downloaded: GitHub returned HTTP {(int)response.StatusCode}.");
            }

            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var destination = new FileStream(
                target, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long received = 0;
                progress?.Report(new UpdateProgress(0, total));
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hasher.AppendData(buffer, 0, read);
                    received += read;
                    progress?.Report(new UpdateProgress(received, total));
                }
                await destination.FlushAsync(cancellationToken);
                actualHash = Convert.ToHexString(hasher.GetHashAndReset());
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(target);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            TryDelete(target);
            return Failure(release, $"The installer could not be downloaded: {ex.Message}");
        }

        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(target);
            return Failure(release, MismatchMessage);
        }

        return new UpdateDownloadResult(true, target, null, null);
    }

    private static UpdateDownloadResult Failure(ReleaseInfo release, string message) =>
        new(false, null, message, release.PageUrl);

    /// <summary>Deletes every file in the updates folder except the one being downloaded.</summary>
    private void RemoveOtherFiles(string keep)
    {
        foreach (var file in Directory.EnumerateFiles(_directory))
        {
            if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
                TryDelete(file);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The file is likely still held open by another process; a stale
            // download is harmless because the hash check runs again on launch.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: leave it for the next attempt.
        }
    }
}
