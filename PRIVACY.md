# PermaDel Privacy Statement

Last updated: 5 October 2026

PermaDel doesn't collect, store or send personal data. It has no accounts, telemetry, analytics, crash reporting or
ads. The only request PermaDel makes on its own is the update check described below.

- **Your files stay on your PC.** PermaDel only works with the files and folders you select. Their names and contents
  are never sent anywhere.
- **Settings** are stored locally in the Windows registry under `HKEY_CURRENT_USER\Software\PermaDel`. Uninstalling
  PermaDel removes them.
- **Identity verification** is carried out by Windows:
  - With Windows Hello, PermaDel only learns whether verification succeeded.
  - With the password prompt, the password goes to Windows for checking and is then erased from PermaDel's memory. It's
    never stored or sent anywhere.
- **Links** to GitHub open in your web browser, where GitHub's privacy statement applies.
- **Microsoft components.** The Microsoft Windows App SDK included with PermaDel may collect diagnostic information as
  described in its license terms (in the `licenses` folder) and the Microsoft Privacy Statement at
  https://aka.ms/privacy. Any such data goes to Microsoft, not to PermaDel's author.

## Update check

When PermaDel starts, and when you press **Check now** in Settings, it asks GitHub for the latest release over HTTPS at
`api.github.com/repos/lukastojiljkovic/PermaDel/releases/latest`. The request carries PermaDel's version in its
User-Agent header; GitHub sees your IP address and the usual connection metadata, and GitHub's privacy statement
applies. No other data is sent, and nothing about your files or your PC is included. When you choose to update, the
installer is downloaded from GitHub's release servers, and the SHA-256 checksum published with the release is verified
before the installer is started. The automatic check runs at most once a day and can be turned off in Settings.

## Contact

Open an issue at https://github.com/lukastojiljkovic/PermaDel/issues.
