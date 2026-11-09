# Colour review - 1.0.7

Reviewed the nine bundled themes, shared controls, calendar transparency, plot tints, sticky-note paper, menus, input states and validation messages. The existing paper curl and drag interaction are preserved.

## Rules used

| Role | Rule |
| --- | --- |
| Accent fill | Keep each theme's original accent hue for primary actions and today markers. |
| Accent text | A related, contrast-adjusted foreground; do not assume an attractive button fill works as small text. |
| Main / muted / dim text | Keep a clear hierarchy and check composited surfaces, including hover, pressed and selected backgrounds. |
| Error / warning | Dedicated red / amber roles, independent of the selected accent. |
| Paper | Warm neutral ink on a light paper palette; app themes must not turn paper buttons white on hover. |
| Transparent calendar | Preserve the theme's accent hue and use readable foregrounds for dark desktop surfaces. |
| Custom plot tint | Adapt default text to its surface. Explicit user-selected text colours remain user-selected. |

## Fixed

- Replaced note buttons' inherited app-theme template with paper-specific ink, hover, pressed and keyboard-focus colours. This fixes white hover labels on white paper under dark themes.
- Strengthened muted and dim colours across all nine themes while retaining their warm, cool or green tone.
- Added `Brush.AccentText`, `Brush.Danger` and `Brush.Warning` to every theme. Foreground accents and focus outlines now use the readable accent; filled accents keep their original colours.
- Error and warning messages no longer inherit arbitrary green, blue or pink accents.
- Custom accent fills keep the requested colour; their foreground variant is derived against theme surfaces.
- Transparent calendars preserve rose, blue, amber and other accent families instead of forcing all themes to green.
- Default plot text adapts to custom dark tints and low-opacity presentation; plot popup surfaces use a matching readable foreground.

## Verification

`python scripts/check-theme-colors.py` performs 1,089 checks over nine themes: main, muted, dim, accent, error and warning text against five composited surfaces in normal, hover, pressed and selected states, plus text on accent fills. Required text contrast is at least 4.5:1. Theme brush-key parity is also checked.

The local WPF regression harness passes 1,211 checks covering the existing functionality plus paper-button hover under every theme, all five paper colours, custom white/black/pink/blue accents, transparent accent hue, and plot default-text adaptation.

The build succeeds. NuGet vulnerability metadata was unavailable in this environment; that warning is separate from colour verification.

## Practical limits

The numeric contrast checks apply to bundled opaque theme surfaces and their overlays. Fully transparent windows depend on the wallpaper behind them. Arbitrary custom background tints and explicit custom text colours can also produce poor combinations; the existing tint warning remains relevant. This review does not claim fixed contrast on every possible wallpaper or user-created palette. Interactive appearance still benefits from a visual pass on the target monitor.
