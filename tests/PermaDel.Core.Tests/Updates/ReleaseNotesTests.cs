using System.Globalization;
using PermaDel.Core.Updates;

namespace PermaDel.Core.Tests.Updates;

public sealed class ReleaseNotesTests
{
    /// <summary>The body of the v1.1.1 release, exactly as GitHub publishes it.</summary>
    private const string RealReleaseBody = """
        PermaDel securely shreds files and folders on Windows, so they can't be brought back with undelete or file-recovery tools.

        ## Download

        **PermaDel-1.1.1-Setup.exe** for 64-bit Windows 10 version 1809 or later. The File Explorer context menu requires Windows 11.

        SHA-256: `39A054DD91C5232127C1368789DF6293C4E3E1940C06E130AAD469F3034FBA33`

        - **SmartScreen.** The installer isn't code-signed yet, so Windows may warn you. Check the hash with `Get-FileHash .\PermaDel-1.1.1-Setup.exe`, then select **More info** > **Run anyway**.
        - **Administrator approval.** Setup registers the File Explorer extension package, which requires administrator rights.
        - **Provenance.** GitHub attests that this installer was built by this repository's release workflow: `gh attestation verify PermaDel-1.1.1-Setup.exe --repo lukastojiljkovic/PermaDel`.

        ## What's new

        ### Fixed

        - Uninstalling PermaDel removes `%LOCALAPPDATA%\PermaDel\Updates`, where an update installer that was downloaded but never run used to stay behind.

        ## Verification

        The [release build](https://github.com/lukastojiljkovic/PermaDel/actions/runs/37702773384) passed all 78 unit tests before it built this installer. The forensic disk test needs administrator rights and runs separately; the README describes [how PermaDel is verified](https://github.com/lukastojiljkovic/PermaDel#verification).

        ## Limitations

        No file-level shredder can guarantee destruction on SSDs or of copies in backups, shadow copies or cloud-synced folders. See [Limitations](https://github.com/lukastojiljkovic/PermaDel#limitations) in the README.

        [Terms of Use](https://github.com/lukastojiljkovic/PermaDel/blob/v1.1.1/TERMS.md) · [Privacy Statement](https://github.com/lukastojiljkovic/PermaDel/blob/v1.1.1/PRIVACY.md) · [Third-Party Notices](https://github.com/lukastojiljkovic/PermaDel/blob/v1.1.1/THIRD-PARTY-NOTICES.md)
        """;

    private static readonly string[] TechnicalWords =
        ["SHA-256", "SmartScreen", "attestation", "unit tests", "Terms of Use"];

    [Fact]
    public void The_real_release_body_keeps_only_the_whats_new_part()
    {
        var notes = ReleaseNotes.FromReleaseBody(RealReleaseBody);

        var section = Assert.Single(notes.Sections);
        Assert.Equal("Fixed", section.Title);
        Assert.Equal(
            "Uninstalling PermaDel removes `%LOCALAPPDATA%\\PermaDel\\Updates`, where an update installer that was downloaded but never run used to stay behind.",
            Assert.Single(section.Items));
    }

    [Fact]
    public void No_item_of_the_real_release_body_is_technical()
    {
        var notes = ReleaseNotes.FromReleaseBody(RealReleaseBody);
        var text = string.Join("\n", notes.Sections.SelectMany(section => section.Items));

        Assert.All(TechnicalWords, word => Assert.DoesNotContain(word, text));
    }

    [Fact]
    public void A_body_without_the_heading_is_parsed_whole()
    {
        var notes = ReleaseNotes.FromReleaseBody(
            """
            Intro line.

            ## Download

            - Download the installer.

            ### Added

            - A new thing.
            """);

        Assert.Equal(2, notes.Sections.Count);
        Assert.Equal(string.Empty, notes.Sections[0].Title);
        Assert.Equal(new[] { "Intro line.", "Download the installer." }, notes.Sections[0].Items);
        Assert.Equal("New", notes.Sections[1].Title);
    }

    [Fact]
    public void A_wrapped_bullet_is_one_item()
    {
        var notes = ReleaseNotes.FromReleaseBody("- first line\n  continues here\n- second\n");

        var section = Assert.Single(notes.Sections);
        Assert.Equal(string.Empty, section.Title);
        Assert.Equal(new[] { "first line continues here", "second" }, section.Items);
    }

    [Theory]
    [InlineData("Added", "New")]
    [InlineData("Changed", "Improved")]
    [InlineData("Fixed", "Fixed")]
    [InlineData("Removed", "Removed")]
    [InlineData("Deprecated", "Deprecated")]
    [InlineData("Security", "Security")]
    public void Keep_a_changelog_titles_map_to_user_words(string title, string expected)
    {
        var notes = ReleaseNotes.FromReleaseBody($"### {title}\n\n- something\n");

        Assert.Equal(expected, Assert.Single(notes.Sections).Title);
    }

    [Fact]
    public void An_unknown_section_title_is_kept()
    {
        var notes = ReleaseNotes.FromReleaseBody("### Notes\n\n- something\n");

        Assert.Equal("Notes", Assert.Single(notes.Sections).Title);
    }

    [Fact]
    public void Comments_and_headings_inside_the_block_are_ignored()
    {
        var notes = ReleaseNotes.FromReleaseBody(
            """
            ## What's new

            <!-- keep this hidden -->
            # Not a section

            ### Fixed

            - Visible. <!-- and this too -->
            """);

        var section = Assert.Single(notes.Sections);
        Assert.Equal("Fixed", section.Title);
        Assert.Equal(new[] { "Visible." }, section.Items);
    }

    [Fact]
    public void Crlf_gives_the_same_notes_as_lf()
    {
        const string lf = "## What's new\n\n### Fixed\n\n- one\n  joined\n";

        var lfNotes = ReleaseNotes.FromReleaseBody(lf);
        var crlfNotes = ReleaseNotes.FromReleaseBody(lf.Replace("\n", "\r\n"));

        Assert.Equal(lfNotes.Sections.Count, crlfNotes.Sections.Count);
        Assert.Equal(lfNotes.Sections[0].Title, crlfNotes.Sections[0].Title);
        Assert.Equal(lfNotes.Sections[0].Items, crlfNotes.Sections[0].Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_input_gives_empty_notes(string? body)
    {
        var notes = ReleaseNotes.FromReleaseBody(body);

        Assert.True(notes.IsEmpty);
        Assert.Empty(notes.Sections);
    }

    [Fact]
    public void The_changelog_entry_stops_before_the_next_version_and_links()
    {
        var notes = ReleaseNotes.FromChangelog(Changelog, new Version(1, 1, 1));

        Assert.NotNull(notes);
        var section = Assert.Single(notes!.Sections);
        Assert.Equal("Fixed", section.Title);
        Assert.Equal(
            "Uninstalling PermaDel removes `%LOCALAPPDATA%\\PermaDel\\Updates`, where an update installer that was downloaded but never run used to stay behind.",
            Assert.Single(section.Items));
    }

    [Fact]
    public void A_version_that_is_not_in_the_changelog_has_no_notes() =>
        Assert.Null(ReleaseNotes.FromChangelog(Changelog, new Version(9, 9, 9)));

    [Fact]
    public void The_unreleased_section_is_never_returned() =>
        Assert.Null(ReleaseNotes.FromChangelog("## [Unreleased]\n\n### Added\n\n- Not shipped yet.\n", new Version(1, 0, 0)));

    private static string Changelog => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md"));
}

public sealed class ReleaseReaderPublishedAtTests
{
    [Fact]
    public void Published_at_is_parsed_when_present()
    {
        Assert.True(ReleaseReader.TryParse(Releases.Json("v1.2.0", publishedAt: "2026-10-07T20:15:00Z"), out var release, out _));

        Assert.NotNull(release!.PublishedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-07T20:15:00Z", CultureInfo.InvariantCulture), release.PublishedAt.Value);
    }

    [Fact]
    public void A_missing_published_at_is_null_and_the_release_still_parses()
    {
        Assert.True(ReleaseReader.TryParse(Releases.Json("v1.2.0"), out var release, out _));

        Assert.Null(release!.PublishedAt);
        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("v1.2.0", release.Tag);
    }
}
