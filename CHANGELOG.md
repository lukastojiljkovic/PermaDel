# Changelog

All notable changes to PermaDel are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/),
and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

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

[Unreleased]: https://github.com/lukastojiljkovic/PermaDel/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/lukastojiljkovic/PermaDel/releases/tag/v1.0.0
