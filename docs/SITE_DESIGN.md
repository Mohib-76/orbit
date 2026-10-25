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

## Direction: a product page

The page reads like a professional software product: a confident headline, the real app screens, and short,
precise copy in a product voice (second person, active voice, Title Case headings and buttons, curly quotes,
numerals). Each claim must be true of the shipped apps; details live on GitHub (README, PRIVACY, PROTOCOL,
TROUBLESHOOTING) and are linked rather than repeated.

The page follows Vercel's Web Interface Guidelines (`vercel-labs/web-interface-guidelines`, used through the
`web-design-guidelines` skill): visible focus rings, skip link, `scroll-margin-top` on anchors, explicit image
sizes, `color-scheme` and `theme-color`, `translate="no"` on the brand, no `transition: all`, transform and
opacity animations only, and every motion either finite, user-driven or pausable. Re-run that review after
changing the page.

The product's real difference gets one interactive moment: the **route diagram**. The PC dials every route to the
phone at once (cable, tethering/hotspot, Wi-Fi broadcast, manual address) and keeps the first that answers.
Visitors pick a route from the list; the diagram races the lanes, locks the winner and sends one pass of packets.

## Screens

- Rendered from the owner's design canvas (the Design artifact the apps are built from), at 1.5x (Windows) or 2x
  (Android), saved as WebP in `site/img/`. The hero images also have smaller variants (`-960`, `-390`) for `srcset`.
- Sample data only. Before rendering, personal or real-world details in the canvas samples are replaced in a local
  copy (the owner's name, places, phone numbers, OTPs, real banks or restaurants). Check every new screen for these
  before adding it. The page says the screens come from the design files with sample data.
- Every screen has a light and a dark render, paired with `.th-l` / `.th-d`; the dark copy is `loading="lazy"` so
  only the visible one loads (except the hero, whose dark copies load eagerly because they are the largest
  image above the fold for dark-theme visitors). Keep it that way: a screen that doesn't follow the theme looks broken. When the canvas
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

- **Palette (light / dark):** ground `#FAFAFC` / `#07070C`, surface `#FFFFFF` / `#0E0E17`, ink `#0D0D17` /
  `#F2F2F8`, muted `#5A5B6F` / `#A2A3B8`, accent `#4B4FE6` / `#8C8FFF` (buttons `#4B4FE6` / `#5257F0`), a
  violet `#8B5CF6` / `#B794FF` only in the headline gradient, green only for "connected". The security band and
  the closing card are dark in both themes. All colors are tokens on `:root` in `site.css`.
- **Type:** Manrope (variable, self-hosted) for everything, 800 weight with tight tracking for headings;
  JetBrains Mono only for route numbers and lane labels.
- **Shape:** 22 px cards and tiles, pill buttons, devices drawn as a black frame around the Android screens.
- **Texture:** a faint grid and an accent glow behind the hero and the security band, thin orbit rings in the
  hero, the closing card and the 404.
- **Motion:** one ease (`cubic-bezier(.16,1,.3,1)`).
  - Hero: the headline lines slide up, the rest rises in, the rings draw once and the two status chips pop in.
    While scrolling, the product frame straightens from a 3D tilt, the phone settles and the rings turn
    (scroll-linked, so the visitor drives it).
  - Sections rise in as they arrive (`data-reveal`, staggered with `--i`). Tiles get a spotlight that follows the
    pointer and lift their screen on hover. The primary button has a light sweep on hover.
  - The route diagram plays once when it first comes into view, and again on a route click or Replay; one pass is
    under 5 seconds. The security band's encrypted line draws once.
  - Under reduced motion everything is still and visible, and the HTML paints the finished, locked diagram.

## Sections

```
hero      release badge, h1, one paragraph, Download for Windows + View Source, platform line /
          the Windows dashboard in a tilted frame with the Android home screen in front and two status chips
specs     4 figures: routes, servers, TLS, open source
film      the 12 s feature loop, framed 16:9, with a Pause button
features  bento of screens (`.bento` > `article.tile t5|t6|t7|t12`, each row adding up to 12): clipboard, files,
          mirroring (full width), phone remote and its PC settings, notifications, earbuds, tray, quick settings; then a
          grid of nine smaller capabilities without screens
connect   heading and route list (the controls) / the route diagram with a status line and Replay
security  dark band: heading, the direct-line drawing, four cards, links to PRIVACY and PROTOCOL
install   three steps / download cards (your platform marked). For now only Windows has a download: Play Protect
          blocks browser installs of the APK in some countries, so the Android card says the Windows app installs
          it over USB (its download button is commented out, ready to restore)
faq       six questions in an exclusive accordion (`<details name="faq">`)
cta       dark closing card with the download button
footer    brand line, three link columns, credits, non-affiliation notice
404       "This Page Is Out of Orbit" over the hero rings
```

The hero badge names the latest headline feature (its text is static, not the version number); update it at
release time along with the bento.
