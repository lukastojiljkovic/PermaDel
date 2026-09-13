namespace PermaDel.Core;

/// <summary>Accumulates processed bytes and throttles reports so fast disks do not flood the UI thread.</summary>
internal sealed class ProgressTracker(IProgress<ShredProgress>? progress, long totalBytes, int passes)
{
    private const int ReportIntervalMs = 50;

    private string _currentPath = string.Empty;
    private long _processed;
    private long _lastReport = long.MinValue;

    public void BeginFile(string path)
    {
        _currentPath = path;
        Report(pass: 1);
    }

    public void Advance(int pass, long bytes)
    {
        _processed += bytes;
        Report(pass);
    }

    private void Report(int pass)
    {
        var now = Environment.TickCount64;
        if (progress is null || now - _lastReport < ReportIntervalMs)
            return;

        _lastReport = now;
        progress.Report(new ShredProgress(_currentPath, pass, passes, _processed, totalBytes));
    }
}
