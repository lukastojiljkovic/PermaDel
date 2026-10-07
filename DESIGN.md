---
name: PermaDel
description: "A native WinUI 3 interface built from Fluent controls and theme resources on a Mica backdrop: one card for the shred list, one folder-yellow accent on a single icon, and the Windows type ramp, with a separate black-and-white brochure palette for the product site."

colors:
  accent: AccentTextFillColorPrimaryBrush
  card: CardBackgroundFillColorDefaultBrush
  cardStroke: CardStrokeColorDefaultBrush
  secondaryText: TextFillColorSecondaryBrush
  tertiaryText: TextFillColorTertiaryBrush
  folderFill: "#F2B93B"
  siteAccent: "#b3202f"
  siteAccentDark: "#f2666e"
  siteBand: "#000"
  siteOnBand: "#fff"
  siteOnBandMuted: "#c4c8c0"
  sitePaper: "#fff"
  siteInk: "#0d0e0c"
  siteInkMuted: "#4a4f47"
  siteHairline: "#d5d9d1"
  siteDarkPaper: "#141613"
  siteDarkInk: "#eef0ea"
  siteDarkInkMuted: "#a9aea4"
  siteDarkHairline: "#30352e"

typography:
  title: TitleTextBlockStyle
  bodyStrong: BodyStrongTextBlockStyle
  body: BodyTextBlockStyle
  caption: CaptionTextBlockStyle
  monoFamily: Consolas
  siteFamily: Archivo
  siteBodySize: 1.0625rem

rounded:
  control: ControlCornerRadius
  overlay: OverlayCornerRadius
  siteButton: 2px

spacing:
  cardPadding: 16,12
  cardColumnSpacing: 16
  settingsCardMinHeight: 68
  sectionHeaderMargin: 1,28,0,6
  pagePadding: 32,20,32,32
  pageSpacing: 4
  paneLength: 240
  browserRowHeight: 36
  queuePanelWidth: 380
  siteGutter: 24px
  siteMargin: clamp(20px, 3.4vw, 48px)

components:
  settings-card:
    backgroundColor: "{colors.card}"
    rounded: "{rounded.control}"
    padding: "{spacing.cardPadding}"
    height: 68
  shred-panel:
    backgroundColor: "{colors.card}"
    rounded: "{rounded.overlay}"
    width: 380
  icon-button:
    width: 36
    height: 32
  page:
    padding: "{spacing.pagePadding}"
    width: 1000
---

# Design System: PermaDel

## Overview

**Creative North Star: "A Windows settings page with one warning yellow"**

PermaDel is stock WinUI 3: a `MicaBackdrop`, the WinUI `TitleBar` and a `NavigationView`, with colours, radii and type
brought in from Fluent theme resources. The app adds exactly one colour of its own — a folder-yellow
(`{colors.folderFill}`) used for the folder glyph in the browser and the shred list, so a folder reads as a folder at a
glance. Everything else follows Windows, including the accent colour the user chose.

The product site under `site/` is a black-band brochure on white paper with a 12-column grid and one publication
colour, `{colors.siteAccent}`, set in Archivo. It is a separate surface from the app.

**Key Characteristics:**

- Fluent resources for every colour and radius; light, dark and high-contrast follow Windows.
- One app colour: `{colors.folderFill}` on the folder glyph only.
- One panel shape for the shred list: `{rounded.overlay}` corners, `{colors.card}` background and a
  `{colors.cardStroke}` hairline.
- The browser is the app's own: a breadcrumb address bar and a list with File Explorer's shortcuts.

## Colors

The app's brushes are defined in `src/PermaDel/App.xaml`:

| Token | Value or Fluent resource | Used for |
| --- | --- | --- |
| `{colors.folderFill}` | `#F2B93B` | the folder glyph in the browser and the shred list |
| `{colors.accent}` | `AccentTextFillColorPrimaryBrush` | the step glyph in the welcome dialog |
| `{colors.card}` | `CardBackgroundFillColorDefaultBrush` | the shred-list panel and settings rows |
| `{colors.cardStroke}` | `CardStrokeColorDefaultBrush` | the 1px panel and row border |
| `{colors.secondaryText}` | `TextFillColorSecondaryBrush` | descriptions and secondary lines |
| `{colors.tertiaryText}` | `TextFillColorTertiaryBrush` | the empty-list glyph |

The accent on buttons is `AccentButtonStyle` and the Fluent accent resources, so it follows the user's Windows
personalization; the app defines no accent hex. The product site sets its publication colour in `site/index.html` —
`--app: #b3202f` for light and `--app-dark: #f2666e` for dark — over the black-and-white palette in `site/site.css`
(`{colors.siteBand}`, `{colors.sitePaper}`, `{colors.siteInk}`, `{colors.siteHairline}`, with `{colors.siteDarkPaper}`,
`{colors.siteDarkInk}` and `{colors.siteDarkHairline}` for dark).

**The One-Colour Rule.** The app adds only the folder yellow; every other colour is a Fluent theme resource.

**The Theme-Resource Rule.** Colour is named by its Fluent resource, never by a hex value, so all Windows themes work.

## Typography

Type is the WinUI ramp used through named styles, from `src/PermaDel/App.xaml` and the views:

| Token | WinUI style | Used for |
| --- | --- | --- |
| `{typography.title}` | `TitleTextBlockStyle` | the page title, such as **Settings** |
| `{typography.bodyStrong}` | `BodyStrongTextBlockStyle` | section headers and panel titles |
| `{typography.body}` | `BodyTextBlockStyle` | body text |
| `{typography.caption}` | `CaptionTextBlockStyle` | descriptions, file details and column headers |

`SecondaryCaptionStyle` keeps its Fluent base style and only changes the foreground to `{colors.secondaryText}`, with
`TextWrapping="NoWrap"` and `CharacterEllipsis`. The app names one family of its own, `{typography.monoFamily}`, for
code spans inside the generated help and error text (`Dialogs/MarkdownText.cs`); everything else is a named Fluent
style. The site is set in `{typography.siteFamily}` at `{typography.siteBodySize}`.

**The Ramp Rule.** Text uses a named Fluent style; the only family the app names itself is the monospace stack for
inline code.

## Layout

The window is a `NavigationView` with a 240px pane (`{spacing.paneLength}`). The file browser takes the flexible left
column; the shred list is a fixed 380px right column (`{spacing.queuePanelWidth}`). Browser rows are
`{spacing.browserRowHeight}` tall with fixed 140px and 150px columns for the modified date and size and
`{spacing.cardColumnSpacing}` gaps. Settings is a single `MaxWidth="1000"` column with `{spacing.pagePadding}` and
`{spacing.pageSpacing}`.

**The Two-Column Rule.** The browser flexes and the shred list stays at `{spacing.queuePanelWidth}`; the width that
changes is the browser's.

## Elevation & Depth

Depth comes from Windows. The window sets `MicaBackdrop` as its `SystemBackdrop`, and the shred-list panel is separated
from the browser by a `{colors.cardStroke}` hairline and `{rounded.overlay}` corners rather than a shadow. The
confirmation, the welcome dialog and flyouts use Fluent's own elevation; the app defines none.

**The Mica Rule.** Mica is the only background the app sets, and the hairline, not a shadow, separates the panels.

## Shapes

Two Fluent radii are used, both by name: settings rows and controls use `{rounded.control}` (`ControlCornerRadius`) and
the shred-list panel uses `{rounded.overlay}` (`OverlayCornerRadius`), from `src/PermaDel/App.xaml`. Icon buttons are
`width: 36` by `height: 32` (`{components.icon-button}`) with the control radius. On the site, buttons are
`{rounded.siteButton}` and everything else is square.

**The Named-Radius Rule.** Corners use `ControlCornerRadius` or `OverlayCornerRadius`; no other radius is written.

## Components

- **Settings row** (`controls:SettingsCard`) — a glyph, a header and a description with the setting's control on the
  right. `{components.settings-card}`: `{colors.card}` background, `{colors.cardStroke}` border, `{rounded.control}`
  corners, `{spacing.cardPadding}` padding and a `{spacing.settingsCardMinHeight}` minimum height. Section headers use
  `{spacing.sectionHeaderMargin}`.
- **Shred-list panel** (`{components.shred-panel}`) — a 380px column with the header, the list, the empty state and the
  pass count and **Shred permanently** button pinned to the bottom. `{colors.card}` background, `{colors.cardStroke}`
  border, `{rounded.overlay}` corners. It accepts files dragged from File Explorer.
- **Browser row** — a 36px row with a 20px folder or file glyph, the name, the modified date and the size; selected rows
  use Fluent's selection colours. Double-click opens, and the toolbar reuses File Explorer's shortcuts.
- **Icon button** (`IconButtonStyle`) — `{components.icon-button}` with a transparent background and border, for the
  browser toolbar and list controls.
- **Page** — `MaxWidth="1000"`, `{spacing.pagePadding}` and `{spacing.pageSpacing}`, holding a `TitleTextBlockStyle`
  title, section headers and settings rows.
- **Destructive actions** — **Shred permanently** is `AccentButtonStyle`. Permanence is carried by wording and by the
  confirmation dialog, and a non-closable warning `InfoBar` sits at the bottom of Settings.

**The Panel Rule.** The shred list is one panel with one shape; the browser supplies everything to its left.

## Do's and Don'ts

- Do reference Fluent theme resources (`{colors.card}`, `{colors.secondaryText}`, `{rounded.overlay}`) so light, dark
  and high-contrast follow Windows.
- Do keep `{colors.folderFill}` for the folder glyph only; it marks folders, not decoration.
- Do use the WinUI type styles (`{typography.title}` … `{typography.caption}`) and the two named Fluent radii.
- Do make destructive actions plain in words and require confirmation.
- Don't hard-code a colour, radius or font size in the app.
- Don't add a second panel shape or a drop shadow.
- Don't soften the warning: the interface must not read as if shredding can be undone.
