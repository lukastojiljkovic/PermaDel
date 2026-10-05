using System.Net;
using System.Net.Sockets;
using System.Text;
using PermaDel.Core.Updates;

namespace PermaDel.Core.Tests.Updates;

/// <summary>A clock the tests can move forward.</summary>
internal sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakePreferences : IUpdatePreferences
{
    public bool CheckForUpdatesAutomatically { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
}

internal sealed class FakeLauncher : IInstallerLauncher
{
    public List<string> Launched { get; } = [];

    public InstallOutcome Outcome { get; set; } = InstallOutcome.Started;

    public Task<InstallOutcome> LaunchAsync(string installerPath, CancellationToken cancellationToken = default)
    {
        Launched.Add(installerPath);
        return Task.FromResult(Outcome);
    }
}

/// <summary>Answers every request from a delegate and records them.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}

/// <summary>An <see cref="IProgress{T}"/> that reports on the calling thread, so assertions are deterministic.</summary>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Values { get; } = [];

    public void Report(T value) => Values.Add(value);
}

internal static class Releases
{
    public const string PageUrl = "https://github.com/lukastojiljkovic/PermaDel/releases/tag/v1.2.0";

    /// <summary>A release payload with the installer and, optionally, the sidecar asset.</summary>
    public static string Json(string tag, bool sidecar = true, string? installerUrl = null)
    {
        var version = tag.TrimStart('v');
        var installer = $"PermaDel-{version}-Setup.exe";
        var url = installerUrl ?? $"https://github.com/lukastojiljkovic/PermaDel/releases/download/{tag}/{installer}";
        var assets =
            $$"""{"name":"{{installer}}","browser_download_url":"{{url}}"}""" +
            (sidecar
                ? $$""",{"name":"{{installer}}.sha256","browser_download_url":"{{url}}.sha256"}"""
                : string.Empty);
        return $$"""{"tag_name":"{{tag}}","body":"* something fixed","html_url":"{{PageUrl}}","assets":[{{assets}}]}""";
    }

    public static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A release whose asset URLs point at a test server.</summary>
    public static ReleaseInfo Local(string baseUrl, string installerName, bool sidecar = true) => new(
        new Version(1, 2, 0),
        "v1.2.0",
        "* something fixed",
        PageUrl,
        installerName,
        $"{baseUrl}/installer",
        sidecar ? installerName + ".sha256" : null,
        sidecar ? $"{baseUrl}/sidecar" : null);
}

/// <summary>
/// A minimal HTTP server on 127.0.0.1 for the download tests. It answers every
/// request with the body registered for its path and closes the connection, so
/// the same transfer code runs against it unchanged.
/// </summary>
internal sealed class LocalHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Dictionary<string, byte[]> _routes;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public LocalHttpServer(Dictionary<string, byte[]> routes)
    {
        _routes = routes;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _loop = Task.Run(LoopAsync);
    }

    public string BaseUrl { get; }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            using (client)
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(_stop.Token);
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 })
                {
                }

                var path = requestLine?.Split(' ') is [_, var target, ..] ? target : "/";
                var body = _routes.TryGetValue(path, out var found) ? found : [];
                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, _stop.Token);
                await stream.WriteAsync(body, _stop.Token);
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _stop.Dispose();
    }
}
