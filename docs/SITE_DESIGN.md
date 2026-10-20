# Website design notes

Design decisions for the landing page in `site/` (published to GitHub Pages by `.github/workflows/pages.yml`).
Read this before changing the page so later edits stay consistent.

## Brief

- **Subject:** Orbit, a free, open-source (AGPL-3.0) app pair that links an Android phone and a Windows PC.
- **Audience:** people with a Windows PC and an Android phone who want Phone Link or KDE Connect-style features
  without an account or a cloud.
- **Job:** show what the apps look like, get both installed, and earn trust (open source, no server).
- **Constraints:** a static `site/` folder deployed as-is (no build step), the apps' accent `#4B4FE6` and Manrope,
  light and dark themes. No third-party scripts or fonts. The only outside request is the GitHub API call for the
  latest release links, and the links still work without it.

## Direction: visual first

The page shows the real app screens and keeps text to a title and one line per idea. Details live on GitHub
(README, PRIVACY, TROUBLESHOOTING), linked from the page; don't bring long copy back onto it.

One signature moment carries the product's real difference: the **route race**. The PC dials every route to the
phone at once (cable, tethering/hotspot, Wi-Fi broadcast, typed address) and keeps the first that answers. The
diagram lets visitors pick their setup and watch that lane lock, then packets travel it. The 404 page reuses it
with no lane answering, and the privacy band draws the same line language (one direct encrypted line, the cloud
crossed out).

## Screens

- Rendered from the owner's design canvas (the Design artifact the apps are built from), at 1.5x (Windows) or 2x
  (Android), saved as WebP in `site/img/`. The hero images also have smaller variants (`-960`, `-390`) for `srcset`.
- Sample data only. Before rendering, personal or real-world details in the canvas samples are replaced in a local
  copy (the owner's name, places, phone numbers, OTPs, real banks or restaurants). Check every new screen for these
  before adding it. The page says the screens come from the design files with sample data.
- Every screen has a light and a dark render, paired with `.th-l` / `.th-d`; the dark copy is `loading="lazy"` so
  only the visible one loads. Keep it that way: a screen that doesn't follow the theme looks broken. When the canvas
  has no dark artboard, make the dark copy from the canvas's own palettes (WinDashboard's `light`/`dark` tables for
  Windows screens, AndHome's for Android), mapping each light color to its dark role.

## Film

A 12-second muted loop right after the hero shows four features in motion (clipboard, notifications, files,
mirroring). It's rendered from the owner's Remotion project (`OrbitIntro`, outside this repo): compositions
`OrbitLoop` (dark) and `OrbitLoopLight` (light), at 1280x720, as MP4 (H.264, plays everywhere) and a poster JPEG
each, saved in `site/media/` (`npm run render:web` in that project, then copy from `out/web/`). It plays only while on screen, swaps with the theme, loads nothing until
it's near, has a Pause button (WCAG 2.2.2), and starts paused on its poster under reduced motion. Re-render it
when a feature it shows changes.

## System

- **Palette (light / dark):** ground `#F5F5F8` / `#0C0C12`, ink `#121219` / `#F1F1F6`, muted `#55566A` / `#A6A7BA`,
  accent `#4B4FE6` / `#8C8FFF`, signal green only for "connected", band `#13132A` for the privacy statement.
  No gradients.
- **Type:** Manrope (variable, self-hosted) for everything; JetBrains Mono only for route names in the diagram.
- **Shape:** 24 px tiles, 14 px controls, devices drawn as a black frame around the Android screens.
- **Motion:** one ease (`cubic-bezier(.16,1,.3,1)`). Buttons fill left to right (a line being dialled), tiles rise in
  as they arrive and lift their screen on hover, the race runs on one `requestAnimationFrame` clock that pauses off
  screen. Everything is still under reduced motion; the HTML paints the finished, locked race first.
- **Idle:** the hero is still at rest by decision (a product page); the race and the privacy line keep moving while
  on screen.

## Sections

```
hero      badge, h1, one line, download buttons / Windows dashboard with the Android home screen in front
film      the 12 s feature loop, framed 16:9, with a Pause button
features  bento of screens: clipboard, files (share sheet), mirroring (full width), notifications,
          earbuds, tray, phone shortcuts
connect   one line / the route race
privacy   "No account. No cloud. No tracking." / the direct-line drawing
install   two cards (your platform marked), links to releases and troubleshooting. For now only Windows has a
          download: Play Protect blocks browser installs of the APK in some countries, so the Android card says the
          Windows app installs it over USB (the hero's Android button and link are commented out, ready to restore)
footer    wordmark, credits, project links
404       "No route to this page." / the race with no lane answering
```
