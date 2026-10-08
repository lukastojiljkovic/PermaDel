using System.Text;

namespace PermaDel.Core.Updates;

/// <summary>One group of release note items, such as "Fixed".</summary>
public sealed record ReleaseNotesSection(string Title, IReadOnlyList<string> Items);

/// <summary>
/// The part of a release's notes an end user needs: a short list of items
/// grouped into sections. Built from a GitHub release body or from the
/// changelog embedded in the app.
/// </summary>
public sealed record ReleaseNotes(IReadOnlyList<ReleaseNotesSection> Sections)
{
    private const string WhatsNewHeading = "## What's new";

    /// <summary>True when there is nothing to show.</summary>
    public bool IsEmpty => Sections.All(section => section.Items.Count == 0);

    /// <summary>
    /// Reads the <c>## What's new</c> part of <paramref name="body"/>, or the
    /// whole body when that heading is absent. Never throws.
    /// </summary>
    public static ReleaseNotes FromReleaseBody(string? body)
    {
        var lines = SplitLines(body);
        var heading = Array.FindIndex(lines, line => line.Trim().Equals(WhatsNewHeading, StringComparison.OrdinalIgnoreCase));
        return heading < 0
            ? Parse(lines, 0, lines.Length)
            : Parse(lines, heading + 1, NextSection(lines, heading + 1));
    }

    /// <summary>
    /// Reads the changelog entry for <paramref name="version"/>, or
    /// <see langword="null"/> when the changelog has no section for it.
    /// </summary>
    public static ReleaseNotes? FromChangelog(string changelog, Version version)
    {
        ArgumentNullException.ThrowIfNull(changelog);
        ArgumentNullException.ThrowIfNull(version);

        var lines = SplitLines(changelog);
        var prefix = $"## [{version.ToString(3)}]";
        for (var index = 0; index < lines.Length; index++)
        {
            if (IsVersionHeading(lines[index], prefix))
                return Parse(lines, index + 1, EntryEnd(lines, index + 1));
        }
        return null;
    }

    private static string[] SplitLines(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>The next <c>##</c> heading, which ends a release section.</summary>
    private static int NextSection(string[] lines, int from)
    {
        for (var index = from; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("## ", StringComparison.Ordinal))
                return index;
        }
        return lines.Length;
    }

    /// <summary>The next version or link reference, which ends a changelog entry.</summary>
    private static int EntryEnd(string[] lines, int from)
    {
        for (var index = from; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("## ", StringComparison.Ordinal) || IsLinkReference(line))
                return index;
        }
        return lines.Length;
    }

    private static bool IsVersionHeading(string line, string prefix)
    {
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var rest = line.AsSpan(prefix.Length);
        return rest.IsEmpty || rest.StartsWith(" - ") || rest.StartsWith(" \u2014 ");
    }

    /// <summary>A markdown link reference such as <c>[1.1.1]: https://…</c> at column zero.</summary>
    private static bool IsLinkReference(string line) =>
        line.StartsWith('[') && line.Contains("]: http", StringComparison.OrdinalIgnoreCase);

    private static ReleaseNotes Parse(string[] lines, int start, int end)
    {
        var sections = new List<ReleaseNotesSection>();
        var items = new List<string>();
        var title = string.Empty;
        var item = new StringBuilder();
        var paragraph = false;
        var inComment = false;

        void EndItem()
        {
            var text = item.ToString().Trim();
            if (text.Length > 0)
                items.Add(text);
            item.Clear();
            paragraph = false;
        }

        void EndSection()
        {
            EndItem();
            if (items.Count > 0)
                sections.Add(new ReleaseNotesSection(MapTitle(title), items.ToArray()));
            items.Clear();
        }

        for (var index = start; index < end; index++)
        {
            var line = StripComments(lines[index], ref inComment);
            if (line.Trim().Length == 0)
            {
                EndItem();
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                EndSection();
                title = line[4..].Trim();
            }
            else if (line.StartsWith('#'))
            {
                // A heading inside the block is not part of the notes.
                EndItem();
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                EndItem();
                item.Append(line[2..].Trim());
            }
            else if (item.Length > 0 && line.StartsWith("  ", StringComparison.Ordinal))
            {
                // An indented line continues the open bullet.
                item.Append(' ').Append(line.Trim());
            }
            else if (paragraph && item.Length > 0)
            {
                item.Append(' ').Append(line.Trim());
            }
            else
            {
                EndItem();
                item.Append(line.Trim());
                paragraph = true;
            }
        }

        EndSection();
        return new ReleaseNotes(sections);
    }

    /// <summary>Removes <c>&lt;!-- … --&gt;</c> spans, which may wrap across lines.</summary>
    private static string StripComments(string line, ref bool inComment)
    {
        var text = new StringBuilder();
        var index = 0;
        while (index < line.Length)
        {
            if (inComment)
            {
                var close = line.IndexOf("-->", index, StringComparison.Ordinal);
                if (close < 0)
                    return text.ToString();
                inComment = false;
                index = close + 3;
                continue;
            }

            var open = line.IndexOf("<!--", index, StringComparison.Ordinal);
            if (open < 0)
            {
                text.Append(line, index, line.Length - index);
                break;
            }
            text.Append(line, index, open - index);
            inComment = true;
            index = open + 4;
        }
        return text.ToString();
    }

    /// <summary>Maps the changelog headings to the words end users understand.</summary>
    private static string MapTitle(string title)
    {
        if (title.Equals("Added", StringComparison.OrdinalIgnoreCase))
            return "New";
        if (title.Equals("Changed", StringComparison.OrdinalIgnoreCase))
            return "Improved";
        return title;
    }
}
