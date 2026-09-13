using System.Text;
using PermaDel.Core;

namespace PermaDel.Models;

/// <summary>A shred started from the File Explorer context menu.</summary>
public sealed record ShredRequest(int Passes, IReadOnlyList<string> Paths)
{
    public const string Argument = "--shred";

    /// <summary>
    /// Parses <c>--shred &lt;passes&gt;</c>. The File Explorer extension streams the selected paths over stdin as
    /// UTF-16, one per line, so large selections aren't limited by the maximum command-line length.
    /// </summary>
    public static ShredRequest? FromCommandLine(string[] arguments)
    {
        if (arguments is not [Argument, var passesText] || !int.TryParse(passesText, out var passes))
            return null;

        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.Unicode, detectEncodingFromByteOrderMarks: false);
        var paths = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return new ShredRequest(Math.Clamp(passes, Shredder.MinPasses, Shredder.MaxPasses), paths);
    }
}
