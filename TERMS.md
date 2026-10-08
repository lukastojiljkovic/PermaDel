8142

# PermaDel Terms of Use

Last updated: 8 October 2026

These terms apply to the PermaDel application and installer published at
https://github.com/lukastojiljkovic/PermaDel. PermaDel's source code is licensed under the MIT License (LICENSE).
Nothing in these terms limits the rights the MIT License gives you for the source code.

By installing or using PermaDel, you agree to these terms. If you don't agree, don't install or use it.

## 1. What PermaDel does

PermaDel overwrites the files and folders you select with random data and then deletes them. This is irreversible by
design. Shredded data can't be restored by PermaDel, by its author or by any recovery service.

PermaDel can also wipe a drive's free space, writing random data over the space that files deleted the ordinary way used
to occupy, and remove metadata from the photos and Office files you select, either writing a cleaned copy next to the
original or replacing it. Removing metadata does not destroy the original data by itself.

## 2. Your responsibility

- You decide what to shred. Check your selection before you confirm, and keep backups of anything you might need.
- Only shred data you're entitled to destroy. Don't use PermaDel to destroy data that belongs to someone else without
  their permission, or data you're required to keep, such as evidence, records under a legal hold or records with a
  legal retention period.
- You're responsible for complying with the laws that apply to you.

## 3. No guarantee of complete destruction

Shredding works at the file level, and wiping free space reaches only a drive's free space. Copies of data can survive
outside the file or the space PermaDel overwrites, for example:

- on SSDs and flash storage (wear leveling, TRIM, over-provisioning)
- in Volume Shadow Copies, System Restore points and backups
- in cloud-synced folders
- in file system journals
- in the page file and the hibernation file

PermaDel isn't certified against any data sanitization standard. The Limitations section of the README describes
these cases.

## 4. No warranty

PermaDel is provided free of charge, "as is" and "as available", without warranty of any kind, express or implied,
including the warranties of merchantability, fitness for a particular purpose and non-infringement. You bear the entire
risk of using PermaDel.

## 5. Limitation of liability

To the fullest extent permitted by applicable law, the author and contributors aren't liable for any damages arising
from the use of, or inability to use, PermaDel. This includes loss of data, loss of profits, business interruption and
any direct, indirect, incidental, special or consequential damages, even if they were advised of their possibility.
Some jurisdictions don't allow certain liability to be excluded or limited, for example liability for intent or gross
negligence. Where that's the case, liability is limited to the extent the law allows.

## 6. Third-party components

The installer includes the Microsoft .NET runtime and the Microsoft Windows App SDK, which are licensed under their own
terms. Their license files are installed in the `licenses` folder next to PermaDel.exe and listed in
THIRD-PARTY-NOTICES.md. By using PermaDel, you also agree to those terms. Microsoft and the other licensors provide their
components "as is" and have no liability to you in connection with PermaDel.

PermaDel isn't affiliated with or endorsed by Microsoft. Windows is a trademark of the Microsoft group of companies.

## 7. Privacy

PermaDel doesn't collect, store or send personal data. The only network request it makes itself is the update check
against GitHub; see PRIVACY.md.

## 8. Changes

These terms may change in future releases. The terms included with a release apply to that release.

## Contact

Open an issue at https://github.com/lukastojiljkovic/PermaDel/issues.
