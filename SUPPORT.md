# Support

## Where to ask

| You want to | Go to |
| --- | --- |
| Report something that is broken | **Issues** — use the bug report form |
| Ask for a feature | **Issues** — use the feature request form |
| Report a security problem | **Not an issue.** See [SECURITY.md](SECURITY.md) |
| Read the licence or the terms | [LICENSE](LICENSE), [TERMS.md](TERMS.md) |
| Check what the app stores or sends | [PRIVACY.md](PRIVACY.md) |
| Build or contribute | [CONTRIBUTING.md](CONTRIBUTING.md) |

This project has no Discussions. Issues and the issue forms are the only channel.

## Before opening an issue

- Say which PermaDel version you are running (**Settings** > **About**) and your Windows edition, version and build,
  for example `Windows 11 Pro, version 25H2, build 26200`.
- Say what you did, what you expected, and what happened instead.
- If the report includes file paths or file names, **replace them with `[redacted]`**. PermaDel's failure report and
  screenshots can show what you were shredding and where it lived.
- Say whether the problem is in the app, the File Explorer context menu, or the installer.

## What is in scope

The desktop application, the shredding engine, the built-in file browser, the File Explorer extension and the
installer, plus the source in this repository. The README's **Limitations** section describes what file-level
shredding cannot guarantee.

## What is not in scope

- Recovering data that was shredded. The design is that it cannot be recovered, by PermaDel or by anyone else.
- Copies that survived outside the file PermaDel overwrote — SSDs, shadow copies, backups, cloud sync, the page file
  and file system journals — as described in the README's **Limitations** section.
- Support for a modified build, or for a build from a fork.
- Response-time guarantees. This is a project maintained by one person, with no support contract and no paid tier.
