# Contributing to PermaDel

Thanks for helping. Bug reports, fixes and improvements are welcome.

## Before you start

- For a bug, open an issue with the steps to reproduce it. Say whether it is in the app, the File Explorer context
  menu or the installer, and **redact file paths and file names** you would not publish.
- For a bigger change, open an issue first, so we can agree on the approach before you write code.
- For a security problem, follow [SECURITY.md](SECURITY.md) instead of opening an issue.
- Remember what the code does: it destroys data on purpose. Changes to what gets destroyed, to the protected
  locations, or to identity verification need to be argued carefully.

## Building

You need:

- Windows 10 version 1809 or later (the File Explorer context menu needs Windows 11).
- A .NET SDK that can target `net8.0`: the app targets `net8.0-windows10.0.26100.0`, the core library and its tests
  target `net8.0-windows`, and the CI workflow installs `10.0.x`.
- Visual Studio or Build Tools with the **Desktop development with C++** workload, for the File Explorer extension.
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer (`winget install JRSoftware.InnoSetup`).

```powershell
# Run from source (the context menu is only available in installed builds)
dotnet run --project src/PermaDel -p:Platform=x64

# Unit tests (they never touch anything outside their own temp folder)
dotnet test tests/PermaDel.Core.Tests --filter "Category!=Forensics"

# The forensic test, from an elevated terminal: it creates and detaches a 64 MB NTFS virtual disk
dotnet test tests/PermaDel.Core.Tests --filter Category=Forensics --logger "console;verbosity=detailed"

dotnet format PermaDel.sln --verify-no-changes    # the CI format check
.\build.ps1                                       # tests, publish, licenses, extension, sparse package and installer
```

`build.ps1` writes the installer to `artifacts\installer\PermaDel-<version>-Setup.exe`.

## Tests never shred real user data

The suite is designed so no test can destroy something you care about:

- Every unit test creates its own temporary folder with `Directory.CreateTempSubdirectory` and works only inside it.
  `ShredderTests` cleans that folder up with a verbatim path and does not enumerate through junctions.
- The forensic test does not use your files at all: it creates a fresh 64 MB NTFS virtual disk, writes marker data to
  it, shreds that data, detaches the disk and scans the raw image for the markers. It runs under
  `Category=Forensics`, needs administrator rights, and is kept out of the default and CI test runs.

If you add a test, keep it inside a temp folder or a virtual disk. Do not point a test at a real path.

## Rules the code follows

- **English only**, in code, UI text, docs and commits.
- **Nothing is modified until the whole selection has been scanned.** Refused locations are rejected before any file
  is touched.
- **Links are not followed.** Symbolic links and junctions are removed without touching their targets; files with other
  hard links are left alone.
- **Exact paths only.** Every file system operation uses a `\\?\` path, so `report.`, `.. ` and short names are never
  normalized into something else.
- **Refusals are broad.** Drive roots, Windows, Program Files, ProgramData, the user profile and its main folders, and
  PermaDel's own folder are refused even when reached through a junction, a mapped drive or a device path.
- **Tests first.** Behaviour changes to scanning, refusal or shredding come with a test that failed before the change.

## Project layout

```text
src/PermaDel.Core            Shredding engine (UI-independent, unit and forensically tested)
src/PermaDel                 WinUI 3 desktop app
src/PermaDel.ShellExtension  Native IExplorerCommand for the Windows 11 context menu + sparse package manifest
tests/PermaDel.Core.Tests    xUnit unit tests and the forensic virtual disk test
installer/PermaDel.iss       Inno Setup installer script
build.ps1                    Test, publish and package pipeline
```

## Pull requests

- Keep a pull request to one change, and describe what it changes and how you tested it.
- The format check, the tests and the build must pass. CI runs them on every pull request.
- Add a line to the *Unreleased* section of [CHANGELOG.md](CHANGELOG.md) for anything users notice. PermaDel shows
  these lines in its update dialogs, so write each one as the user would describe the change, not as the code does:
  "Uninstalling PermaDel also removes updates it downloaded but never installed", not the folder path.

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
