# Security Policy

## Supported versions

Security fixes go into the latest release. Update to it before reporting.

## Reporting a vulnerability

Please don't open a public issue. Report it privately through
[GitHub's private vulnerability reporting](https://github.com/lukastojiljkovic/PermaDel/security/advisories/new),
with the steps to reproduce it, the PermaDel and Windows versions, and the impact you expect.

You'll get a reply in the advisory. Once a fix is released, the advisory is published with credit to you, unless you
prefer otherwise.

## What's in scope

PermaDel destroys data on purpose, so these parts matter most:

- **What gets destroyed.** Shredding anything outside the selection is a vulnerability: following a symbolic link or
  junction to its target, overwriting a file that has other hard links, or reaching a refused location (drive roots,
  Windows, Program Files, ProgramData, the user profile and its main folders, PermaDel's own folder) through a
  junction, a mapped drive, a short name, a device path or a name that Windows normalizes.
- **Identity verification.** When verification is on, shredding, or turning verification off, without passing Windows
  Hello or the account password is a vulnerability. This includes shreds started from the File Explorer context menu
  (`PermaDel.exe --shred <passes>`).
- **The File Explorer extension.** The native extension that File Explorer loads, and the paths it passes to PermaDel.
- **The installer.** PermaDel installs to Program Files and registers its File Explorer package as administrator.

Out of scope:

- Data that survives because of the limitations described in the
  [README](https://github.com/lukastojiljkovic/PermaDel#limitations), such as SSD wear leveling, shadow copies, backups
  or file system journals.
- Software already running as your user account. It can change PermaDel's settings and delete your files without
  PermaDel, so verification protects against someone using an unlocked PC, not against malware.
