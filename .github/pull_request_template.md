## What this changes

<!-- What does this pull request change, and why? Link the issue it closes, if any. -->

## How it was tested

<!-- Tests you added or ran, and what you checked in the app. Changes to the shredding engine need a unit test. -->

## Checklist

- [ ] `dotnet format PermaDel.sln --verify-no-changes` passes
- [ ] `dotnet test tests/PermaDel.Core.Tests --filter "Category!=Forensics"` passes, and new behavior has a test
- [ ] Changes to how files are overwritten or deleted were checked with the forensic test (elevated)
- [ ] CHANGELOG.md has a line under *Unreleased* for anything users notice
