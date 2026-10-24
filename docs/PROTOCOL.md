# Orbit protocol (v1)

Modeled on KDE Connect, with one change: the **phone is always the TLS server** and the PC always dials.
On USB tethering and on the phone's hotspot the phone is the PC's gateway, so the PC can find it without
any discovery, and the PC never needs an inbound firewall rule.

## Transport

| | |
|---|---|
| TCP 47800 (phone) | TLS 1.2/1.3. Both sides present self-signed RSA-2048 certificates (mutual TLS) |
| UDP 47801 (phone) | Discovery responder for a shared Wi-Fi |
| TCP 47810 (PC, localhost) | `adb forward` to the phone's 47800 over the USB cable |

Each connection carries **newline-delimited JSON**: `{"type": "orbit.x", "body": {...}}\n`.
The first line on a connection decides what it is: `orbit.hello` (control), `orbit.pair` (pairing),
or `orbit.data` (file bytes).

## Discovery

On a shared Wi-Fi the PC broadcasts one UDP datagram (the same JSON, no newline needed) to port 47801 on each
interface's broadcast address and listens for 0.9 s:

| Type | Direction | Body |
|---|---|---|
| `orbit.discover` | PC → broadcast | `{id}` (the PC's device id) |
| `orbit.identity` | phone → PC (unicast reply) | `{id, name, model, proto, app, port}` |

The phone answers only a PC it is paired with, or the one whose QR code it has just scanned; anyone else gets
no reply. The PC then dials `port` over TLS as usual, so discovery never carries anything secret.

## Trust

- A device's identity is the SHA-256 fingerprint of its certificate (lowercase hex of the DER bytes).
- The PC's QR code (`orbit://pair?d=<base64url JSON>`) carries `{v, id, name, fp, token}`. `token` is 128 random bits and is never sent over the network.
- Pairing, after TLS:
  1. PC → `orbit.pair {id, name, nonce}`. The phone checks that `id` and the client-cert fingerprint match the QR code.
  2. Phone → `orbit.pair.proof {id, name, model, nonce, proof}`, where `proof = HMAC-SHA256(token, "phone|pcNonce|phoneFp|pcFp")`.
  3. PC checks it, then → `orbit.pair.ok {proof = HMAC(token, "pc|phoneNonce|phoneFp|pcFp")}`.
  4. Both sides store each other's fingerprint. Every later connection must present exactly that certificate.
- Binding both fingerprints into the HMACs stops a man-in-the-middle from relaying a pairing.
- The phone answers a connection it won't accept with one packet, then closes it: `orbit.reject` (an
  `orbit.hello` from a PC that isn't the paired one: wrong id or certificate) or `orbit.pair.reject` (an
  `orbit.pair` with no matching scanned QR code, an expired one, or while another pairing is in progress).

## Control packets

| Type | Direction | Body |
|---|---|---|
| `orbit.hello` | both | `{id, name, model?}` (first packet of a control link) |
| `orbit.ping` / `orbit.pong` | both | `{}`. The PC pings every 20 s; either side drops the link after 45–50 s of silence |
| `orbit.settings` | both | `{autoUsbTethering, usbMode?, budsEnabled?, mediaWanted?, pcName?, clipboardAutoSync?}` |
| `orbit.battery` | phone → PC | `{level, charging, chargeTimeMs}` |
| `orbit.clipboard` | both | `{text}` |
| `orbit.notification` | phone → PC | `{key, pkg, app, title, text, time, silent, icon?}` (`icon` = base64 PNG, sent once per app per connection) |
| `orbit.notification.removed` | phone → PC | `{key}` |
| `orbit.notification.dismiss` | PC → phone | `{key}` |
| `orbit.file.offer` | both | `{id, name, size}` |
| `orbit.file.accept` | both | `{id, offset}` (`offset` = bytes the receiver already has) |
| `orbit.file.done` | receiver → sender | `{id}` |
| `orbit.file.cancel` | both | `{id}` |
| `orbit.unpair` | both | `{}` |
| `orbit.buds.state` | both | Sender's own view of the earbuds (see [Earbuds](#earbuds)): `{enabled, playing, connected, control, driver, name, mac, anc, noiseModes: [{id, label}…], cycle, battery: [L,R,case], charging: [L,R,case], batteryMain, toggles: [{id, title, subtitle, on}…], features: {"<id>": bool}, touch: [[side, gesture, action]…]}`. Sent on connect and on every change (debounced 150 ms) |
| `orbit.buds.cmd` | both | Relay when only the receiver holds the buds' controls: `{op: "anc", mode}` (a `noiseModes` id) · `{op: "feature", id, on}` (a `toggles` id; numeric ids are sent as numbers) · `{op: "cycle", mask}` · `{op: "touch", side, gesture, action}` |
| `orbit.media` | both | `{action: "play"\|"pause"}`, used by "Play here" |
| `orbit.media.state` | both | The sender's own media players (see [Media](#media)): `{players: [{id, app, title, artist, album, playing, pos, len, canPlay, canPause, canNext, canPrev, canSeek, art}], active, controllable}` |
| `orbit.media.art` | both | `{hash, jpeg}`: album art for players whose `art` is `hash`, as base64 JPEG (at most 320 px). Sent once per hash per connection, before the state that uses it |
| `orbit.media.cmd` | both | `{player, op: "play"\|"pause"\|"toggle"\|"next"\|"prev"\|"seek", pos?}` (`pos` in ms, for `seek`) |
| `orbit.usb.state` | phone → PC | `{cable, mode: "charging"\|"tethering"\|"files"\|"other"}`: the phone's own view of its USB port. Sent on connect and on every change |
| `orbit.usb.control` | PC → phone | `{adb, switching, mirroring}`: whether the PC can switch modes (it sees this phone over USB adb), the mode it is switching to (`""` when idle), and whether USB mirroring blocks switching |
| `orbit.usb.set` | phone → PC | `{mode}`: the user picked a USB mode on the phone; the PC switches it over adb |
| `orbit.mirror.state` | PC → phone | `{active, viewOnly, transport: "USB"\|"Wi-Fi"}`. Sent when mirroring starts, stops or changes, and on connect. The phone shows its LIVE notification and tile from it |
| `orbit.mirror.cmd` | phone → PC | `{op: "stop"}` · `{op: "viewOnly", on}` (the PC stops sending input) · `{op: "start"}` (the Mirror tile asks the PC to open its window) |
| `orbit.mirror.prepare` | PC → phone | `{}`. Get wireless debugging ready: the phone turns it on if it may (`WRITE_SECURE_SETTINGS`) and answers with `orbit.mirror.info` |
| `orbit.mirror.setup` | PC → phone | `{}`. "Open on phone": show the setup screen (or a notification that opens it). Answered with `orbit.mirror.info` |
| `orbit.mirror.info` | phone → PC | `{sdk, devOptions, wirelessDebugging, wifi, usbDebugging, canToggle}` |
| `orbit.url` | both | `{url}`: open this web link on the receiver (see [Phone remote](#phone-remote)). Only absolute `http`/`https` URLs, at most 4096 characters; anything else is ignored |
| `orbit.remote.caps` | PC → phone | `{volume, input, slides, power: ["lock"\|"sleep"\|"shutdown"\|"restart"…], commands: [{id, name, ask}…], linksAsk}`: what the phone may do on the PC. `input` = touchpad and keyboard, `slides` = slides remote, `linksAsk` = the PC asks before opening a link from the phone. Sent on connect and whenever Settings › Phone remote changes |
| `orbit.remote.run` | phone → PC | `{op: "lock"\|"sleep"\|"shutdown"\|"restart"}` · `{op: "command", id}` |
| `orbit.remote.result` | PC → phone | `{op, id?, ok, error?}`: the answer to `orbit.remote.run`. `error` is a sentence to show |
| `orbit.volume.state` | PC → phone | `{level, muted}`: the default playback device's volume, 0–100 (`-1` = no playback device). Sent on connect and on every change (debounced 80 ms) while volume control is allowed |
| `orbit.volume.set` | phone → PC | `{level?, muted?}`. Raising `level` above 0 without `muted` also unmutes |
| `orbit.input` | phone → PC | Touchpad and keyboard, when `input` is allowed: `{op: "move", dx, dy}` (pixels) · `{op: "button", button: "left"\|"right"\|"middle", action: "click"\|"down"\|"up"}` · `{op: "scroll", dy, dx}` (wheel units, 120 = one notch; positive = up / right) · `{op: "text", text}` (at most 1000 characters; `\n` presses Enter) · `{op: "key", key?, mods: ["ctrl"\|"alt"\|"shift"\|"win"…]}` (`key`: `esc`, `tab`, `enter`, `backspace`, `delete`, `space`, arrows `left`/`up`/`right`/`down`, `home`, `end`, `pageup`, `pagedown`, `insert`, `f1`–`f12`, a letter or a digit; no `key` = the modifiers alone) |
| `orbit.slides` | phone → PC | `{op: "next"\|"prev"\|"start"\|"end"\|"black"}`, when `slides` is allowed. The PC presses Right, Left, F5 (Ctrl+F5 when a browser is in front, where F5 would reload), Esc or B in the window in front |
| `orbit.focus.watch` | phone → PC | `{on}`: while a touchpad, keyboard or slides screen is open, the phone asks for the PC's foreground app |
| `orbit.focus` | PC → phone | `{app}`: the foreground app's name ("Google Chrome"), sent when it changes while watched. Never the window title |

`orbit.settings` also carries `budsEnabled`, so the Earbuds switch stays the same on both devices, and
`usbMode` (`"charging"`, `"tethering"` or `"files"`), the mode the PC applies when the cable is plugged in.
`autoUsbTethering` is still sent (true when `usbMode` is `"tethering"`) for older apps; a peer that sends only
`autoUsbTethering` means `"tethering"` (true) or `"charging"` (false). `mediaWanted` is not synced: it says
whether the sender wants the other side's `orbit.media.state` at all (its "show media" switch). Missing means true.

## Media

Each side publishes the media sessions it can see (Android `MediaSessionManager`, Windows
`GlobalSystemMediaTransportControlsSessionManager`), leaving out the one Orbit itself creates to show the
peer's player. `orbit.media.state` is sent on connect and on changes (debounced 250 ms), not on a timer.

- `id` is stable per app (Android package name, Windows app user model ID); `app` is a display name.
- `pos` is the position in ms when the packet was sent and `len` the length in ms (0 = unknown or live).
  While `playing`, the receiver advances `pos` itself from the moment the packet arrived.
- `art` is a short hash (`""` = no art); the bytes travel once in `orbit.media.art`.
- `active` is the player the sender's system considers current. `controllable` is false when the sender's
  user turned off remote control; the receiver then shows the player without buttons.
- Track titles are user content: neither side logs them.

## Phone remote

The PC decides what the phone may do; the phone only draws what `orbit.remote.caps` lists. The PC checks every
`orbit.remote.run` and `orbit.volume.set` against its own settings again, so a phone can't do more by sending
packets for buttons it wasn't given. Every power button and command shows a notification on the PC.

- **Commands** are set up on the PC (a name, a program, script or command line, and whether to ask first). The
  phone only ever sees `{id, name, ask}` and sends back the `id`; the command text never leaves the PC.
- `ask` (commands) and `"shutdown"` / `"restart"` (always) mean the phone asks "Are you sure?" before sending.
- **Links:** the phone opens a link from the PC as a notification with Open, Copy and Share (Android won't let
  an app open a browser from the background). The PC opens a link from the phone straight away, or after a
  notification if its user chose "Ask first". Both sides accept only `http` and `https`, so a link can't start a
  program or open a local file.
- Links, typed text and command names are user content: neither side logs them. Input can't reach elevated (administrator) windows or the lock screen, by Windows' design.
- Touchpad and keyboard (`input`) are off until switched on on the PC; when the phone starts using input or the slides remote after five idle minutes, the PC shows a notification.

## USB modes

Android runs one of USB tethering (`rndis`), file transfer (`mtp`) or charging only at a time. The PC switches
with `adb shell svc usb setFunctions rndis|mtp|` (empty = charging) and reads `svc usb getFunctions`. It applies
`usbMode` once per plug-in: the phone counts as plugged in again only after it has been gone from adb for 15 s,
so the reconnect that a mode switch causes doesn't count. A switch made from Android's own USB menu is shown,
not undone.

## Earbuds

Each side reports its own view in `orbit.buds.state`, and the UI shows whichever side holds the controls
(else whichever has the buds connected):

- `connected`: the OS has the buds connected to the sender. `batteryMain` (`-1` = unknown) is the one
  level the OS reports for them (the headset's standard battery reports), so any buds get these two.
- `driver`: the brand protocol that matches the buds (`"heymelody"`: realme / OnePlus / OPPO), or `null`
  when Orbit has none for them (battery and playback only). `control`: that driver has the buds'
  control channel open on the sender, so commands can run there.
- The controls describe themselves, so the receiver can show them without knowing the brand:
  `noiseModes` lists only the modes the buds answered for (`anc` is the current id) and `toggles` only
  the settings they reported. `battery`/`charging` (left, right, case) come from the driver. `cycle` and
  `touch` are HeyMelody's (noise-control cycle bitmask; side, gesture and action codes).
- Compatibility with versions before drivers: `features` repeats `toggles` as `{id: on}`. A packet
  without `control` comes from an older version, where `connected` meant the control channel and the
  buds were always HeyMelody.

A brand's driver implements `BudsDriver` (Android `buds/BudsDriver.kt`) and `IBudsDriver` (Windows
`Core/Buds/IBudsDriver.cs`). A driver on only one side still works on both, because the other side
relays its commands.

## Screen mirroring

The video doesn't use the Orbit link. The PC runs scrcpy's device server through adb (USB, or wireless
debugging) and speaks scrcpy's own protocol to it: an H.264 socket, a raw-PCM audio socket and a control
socket, all through one `adb forward tcp:<port> localabstract:scrcpy_<scid>`. See scrcpy's
`doc/develop.md`; Orbit's client is `windows/Orbit.Windows/Core/Mirror/`. The `orbit.mirror.*` packets
above only keep the phone informed and let it stop the stream.

## File bytes

The PC opens a **new** TLS connection to the phone and sends `orbit.data {id, dir, offset, pc}`, then raw bytes:

- `dir: "push"`: PC → phone. The PC writes the file from `offset` to the end.
- `dir: "pull"`: phone → PC. The phone writes the file from `offset` to the end.

If a transfer is interrupted, it stays *Paused*. When the link comes back, the sender offers it again,
the receiver accepts with its current partial size, and the transfer continues from there.
