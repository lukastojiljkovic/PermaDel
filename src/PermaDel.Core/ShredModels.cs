namespace PermaDel.Core;

/// <summary>Snapshot of a running shred operation.</summary>
public sealed record ShredProgress(string CurrentPath, int Pass, int Passes, long BytesProcessed, long TotalBytes)
{
    public double Fraction => TotalBytes > 0 ? (double)BytesProcessed / TotalBytes : 0;
}

/// <summary>An item that could not be shredded, with the reason reported by the file system.</summary>
public sealed record ShredFailure(string Path, string Reason);

public sealed record ShredResult(int FilesShredded, int DirectoriesRemoved, IReadOnlyList<ShredFailure> Failures)
{
    public bool Succeeded => Failures.Count == 0;
}
