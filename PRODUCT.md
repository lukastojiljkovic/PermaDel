# Product

<!-- impeccable:product-schema 1 -->

## Platform
windows

## Users

People who need to destroy specific files or folders on their own Windows PC so they cannot be brought back with
undelete or file-recovery tools. They want to choose exactly what is destroyed, see what is about to happen, and
confirm it. They may also want a second person at an unlocked PC to be unable to use the app. The interface is in
English.

## Product Purpose

PermaDel overwrites the files and folders you select with cryptographically secure random data and then deletes them.
It is the step an ordinary delete does not take: the file's contents, name and timestamps are overwritten before the
entry is removed.

## Positioning

PermaDel is free and open source (MIT) and local: it only works with the items you select, and none of their names or
contents leave the PC. It is a file-level shredder, not a certified sanitizer, and it says so. Its distinguishing
choices are a built-in browser and drag-and-drop shred list, a native Windows 11 File Explorer command, optional
Windows Hello or account-password verification, and a safety design that scans the whole selection and refuses
dangerous locations before anything is modified.

## Operating Context

- Windows 11 is the tested target (build 26200); the File Explorer context menu needs Windows 11, and needs the
  package identity that the sparse package provides. The app runs on 64-bit Windows 10 version 1809 or later.
- The shipped installer is x64.
- Verification, when turned on, uses Windows Hello through `UserConsentVerifier`, or the Windows Security credential
  prompt checked with `LogonUser`.
- Shredding is irreversible by design. The README's **Limitations** section says what file-level shredding cannot
  guarantee.

## Capabilities and Constraints

- **Built-in file browser.** Browse drives and known folders with File Explorer-style back, forward, up and refresh
  buttons and shortcuts, select several items, and add them to the shred list, or drag items in from File Explorer.
- **File Explorer context menu.** Right-click files or folders and choose **Shred with PermaDel**; a submenu picks the
  number of passes.
- **Configurable passes.** 1–35 overwrite passes of random data.
- **Identity verification.** Optionally require Windows Hello or the account password before shredding, and before
  changing the setting.
- **Settings.** Default pass count, confirmation prompt, identity verification, context menu integration, theme and
  welcome screen.
- **Thorough destruction.** Every pass is flushed to disk; NTFS alternate data streams are overwritten; files are
  truncated, renamed to random names and their timestamps scrubbed before deletion.
- **Safety by design.** Nothing is modified until the whole selection has been scanned; symbolic links and junctions
  are removed without following them; files with other hard links and online-only cloud files are skipped; exact
  `\\?\` paths are used; and drive roots, Windows, Program Files, ProgramData, the user profile and its main folders,
  and PermaDel's own folder are refused.
- **Transparent.** Live progress with cancellation, and a report of every item that could not be shredded and why.

Constraints and limitations: the installer and the File Explorer extension are not code-signed; overwriting is
effective on traditional hard drives but cannot guarantee destruction on SSDs, NVMe or flash (wear leveling, TRIM,
over-provisioning); copies can survive in shadow copies, backups, cloud sync, the page file and file system journals;
and locked or privileged files are skipped and reported.

## Brand Commitments

- **Irreversible by design.** The app says so in the welcome screen, the confirmation and the README, and warns before
  anything is destroyed.
- **Scan before acting.** The whole selection is scanned and refused locations are rejected before any file is
  modified.
- **Local only.** No accounts, telemetry, analytics, crash reporting or ads. File names and contents are never sent
  anywhere; the only request the app makes itself is the update check against GitHub.
- **Plain language.** Failures are listed with the reason, and the app explains what shredding can and cannot
  guarantee.

## Evidence on Hand

- Screenshots in `docs/images/`: `main-light.png` and `main-dark.png`, `confirm-light.png` and `confirm-dark.png`,
  `settings-light.png` and `settings-dark.png`.
- The product site under `site/`: `index.html`, `site.css`, `sitemap.xml`, `llms.txt`, `icon.png` and the Archivo font
  files, published through the Pages workflow.
- CI gates in `.github/workflows/ci.yml`: a formatting check, the unit tests, the self-contained publish, the C++ File
  Explorer extension, the sparse package, the installer, and an install and uninstall smoke test that also checks the
  verification setting.
- Releases from `.github/workflows/release.yml`: the installer, a `.sha256` checksum file, build provenance
  attestation, and release notes taken from `CHANGELOG.md`.
- Tests in `tests/PermaDel.Core.Tests`: unit tests plus a forensic test that shreds marker data on a fresh 64 MB NTFS
  virtual disk and scans the raw image for it.
- `CHANGELOG.md`, `TERMS.md`, `PRIVACY.md`, `THIRD-PARTY-NOTICES.md` and `SECURITY.md`.

Not on hand: no user metrics, no download counts, no testimonials, no benchmarks, and no independent security audit or
data-sanitization certification. None should be invented.

## Product Principles

- Decide what is destroyed before overwriting anything.
- Refuse the locations where a mistake would be unrecoverable, even when they are reached indirectly.
- Skip what cannot be destroyed safely rather than doing it badly, and report it.
- Make the user confirm, and verify it is them when asked to.
- State the limits of file-level shredding plainly; never imply a guarantee the app cannot make.

## Accessibility & Inclusion

PermaDel uses standard WinUI 3 / Fluent controls and theme resources, so it follows the Windows theme, contrast and
text-scaling settings, and offers light, dark and "use system setting". The file browser uses the same keyboard
shortcuts and mouse back/forward buttons as File Explorer, and its controls carry `AutomationProperties` names. The
interface is English only; that is a decision, not an oversight.
