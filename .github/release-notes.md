PermaDel securely shreds files and folders on Windows, so they can't be brought back with undelete or file-recovery tools.

## What's new

{{CHANGES}}

## Download

**{{FILE}}** for 64-bit Windows 10 version 1809 or later. The File Explorer context menu requires Windows 11.

SHA-256: `{{SHA256}}`

- **SmartScreen.** The installer isn't code-signed yet, so Windows may warn you. Check the hash with `Get-FileHash .\{{FILE}}`, then select **More info** > **Run anyway**.
- **Administrator approval.** Setup registers the File Explorer extension package, which requires administrator rights.
- **Provenance.** GitHub attests that this installer was built by this repository's release workflow: `gh attestation verify {{FILE}} --repo {{REPOSITORY}}`.

## Verification

The [release build]({{RUN_URL}}) passed all {{TESTS}} unit tests before it built this installer. The forensic disk test needs administrator rights and runs separately; the README describes [how PermaDel is verified]({{SERVER}}/{{REPOSITORY}}#verification).

## Limitations

No file-level shredder can guarantee destruction on SSDs or of copies in backups, shadow copies or cloud-synced folders. See [Limitations]({{SERVER}}/{{REPOSITORY}}#limitations) in the README.

[Terms of Use]({{SERVER}}/{{REPOSITORY}}/blob/{{TAG}}/TERMS.md) · [Privacy Statement]({{SERVER}}/{{REPOSITORY}}/blob/{{TAG}}/PRIVACY.md) · [Third-Party Notices]({{SERVER}}/{{REPOSITORY}}/blob/{{TAG}}/THIRD-PARTY-NOTICES.md)
