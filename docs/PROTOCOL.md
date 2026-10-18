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

## Trust

- A device's identity is the SHA-256 fingerprint of its certificate (lowercase hex of the DER bytes).
- The PC's QR code (`orbit://pair?d=<base64url JSON>`) carries `{v, id, name, fp, token}`. `token` is 128 random bits and is never sent over the network.
- Pairing, after TLS:
  1. PC → `orbit.pair {id, name, nonce}`. The phone checks that `id` and the client-cert fingerprint match the QR code.
  2. Phone → `orbit.pair.proof {id, name, model, nonce, proof}`, where `proof = HMAC-SHA256(token, "phone|pcNonce|phoneFp|pcFp")`.
  3. PC checks it, then → `orbit.pair.ok {proof = HMAC(token, "pc|phoneNonce|phoneFp|pcFp")}`.
  4. Both sides store each other's fingerprint. Every later connection must present exactly that certificate.
- Binding both fingerprints into the HMACs stops a man-in-the-middle from relaying a pairing.

## Control packets

| Type | Direction | Body |
|---|---|---|
| `orbit.hello` | both | `{id, name, model?}` (first packet of a control link) |
| `orbit.ping` / `orbit.pong` | both | `{}`. The PC pings every 20 s; either side drops the link after 45–50 s of silence |
| `orbit.settings` | both | `{autoUsbTethering, pcName?, clipboardAutoSync?}` |
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
| `orbit.buds.state` | both | Sender's own link to the earbuds: `{enabled, playing, connected, name, mac, anc: "OFF"\|"TRANSPARENCY"\|"ANC"\|null, cycle, battery: [L,R,case], charging: [L,R,case], features: {"<id>": bool}, touch: [[side, gesture, action]…]}`. Sent on connect and on every change (debounced 150 ms) |
| `orbit.buds.cmd` | both | Relay when only the receiver holds the buds: `{op: "anc", mode}` · `{op: "feature", id, on}` · `{op: "cycle", mask}` · `{op: "touch", side, gesture, action}` |
| `orbit.media` | both | `{action: "play"\|"pause"}`, used by "Play here" |

| `orbit.mirror.state` | PC → phone | `{active, viewOnly, transport: "USB"\|"Wi-Fi"}`. Sent when mirroring starts, stops or changes, and on connect. The phone shows its LIVE notification and tile from it |
| `orbit.mirror.cmd` | phone → PC | `{op: "stop"}` · `{op: "viewOnly", on}` (the PC stops sending input) · `{op: "start"}` (the Mirror tile asks the PC to open its window) |
| `orbit.mirror.prepare` | PC → phone | `{}`. Get wireless debugging ready: the phone turns it on if it may (`WRITE_SECURE_SETTINGS`) and answers with `orbit.mirror.info` |
| `orbit.mirror.setup` | PC → phone | `{}`. "Open on phone": show the setup screen (or a notification that opens it). Answered with `orbit.mirror.info` |
| `orbit.mirror.info` | phone → PC | `{sdk, devOptions, wirelessDebugging, wifi, usbDebugging, canToggle}` |

`orbit.settings` also carries `budsEnabled`, so the Earbuds switch stays the same on both devices.

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
