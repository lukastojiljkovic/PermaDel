# Changelog

All notable changes to PermaDel are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/),
and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [2.0.0] - 2026-10-08

PermaDel can now do more than shred: wipe a drive's free space so files you deleted the ordinary way can't be read back, and remove the hidden details that photos and Office files carry, such as where a photo was taken, the camera, the author and the company. It also tells you what changed after it updates, in plain words.

### Added

- **Wipe free space** on a drive in This PC: PermaDel writes random data over all of the drive's free space and then
  removes it, so files you deleted the ordinary way can't be read back. Your existing files are never touched. On NTFS
  it can also fill the file table's free entries, where small files live. The wipe files are removed even when the wipe
  is cancelled or fails, and any that a crash left behind are removed the next time PermaDel starts.
- **Remove metadata** from a JPEG, PNG or WebP photo, or a Word, Excel or PowerPoint file: PermaDel shows what the file
  holds, then writes a cleaned copy next to it or replaces the original. Pictures inside Office files are cleaned too.
  A file is only written once the cleaned version passes a check, so one that can't be cleaned safely is left as it
  was.
- PermaDel shows what changed when it updates: the first time you open a new version, it shows that version's notes,
  once. Opening PermaDel from the File Explorer menu never interrupts you with them.

### Changed

- The update notice and the release notes are now written for the people who use the app. **What's new** shows just
  the changes, grouped into New, Improved and Fixed, with the release date and the full notes a click away, and offers
  **Update now**.

## [1.1.1] - 2026-10-07

### Fixed

- Uninstalling PermaDel removes `%LOCALAPPDATA%\PermaDel\Updates`, where an update installer that was
  downloaded but never run used to stay behind.

## [1.1.0] - 2026-10-05

### Added

- PermaDel checks GitHub for a newer release when it starts (at most once a day) and from Settings, shows a banner with
  the release notes when one exists, and installs it after verifying the installer against the SHA-256 checksum
  published with the release. The automatic check can be turned off.

## [1.0.0] - 2026-09-13

The first release.

### Added

- Shredding that overwrites every file, including its alternate data streams, with 1 to 35 passes of cryptographically
  secure random data, flushes each pass to disk, then truncates, renames and deletes the file.
- A built-in file browser with File Explorer-style back, forward and up navigation, drag and drop, and a shred list.
- **Shred with PermaDel** in the Windows 11 File Explorer context menu, with a submenu for the number of passes.
- Optional Windows Hello or Windows account password verification before shredding. Setup asks whether to turn it on,
  and changing it in Settings requires verification.
- Safety checks: drives, system folders, user folders and PermaDel itself are refused; links and junctions are removed
  without touching their targets; hard-linked files and online-only cloud files are left untouched; names that Windows
  would normalize are never confused with other items.
- A welcome screen, light and dark themes, and settings for the defaults.

[Unreleased]: https://github.com/lukastojiljkovic/PermaDel/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/lukastojiljkovic/PermaDel/compare/v1.1.1...v2.0.0
[1.1.1]: https://github.com/lukastojiljkovic/PermaDel/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/lukastojiljkovic/PermaDel/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/lukastojiljkovic/PermaDel/releases/tag/v1.0.0
