# Security policy

## Reporting a vulnerability

Please **don't open a public issue** for security problems. Use GitHub's private reporting instead:
**Security → Report a vulnerability** on this repository. Include what you found, how to reproduce it,
and the Orbit versions (Settings → About on both apps).

You'll get an acknowledgement within a week. Fixes for confirmed issues ship in a new release, and you'll
be credited in the release notes unless you prefer not to be.

Only the latest release is supported with security fixes.

## Security model

What Orbit is designed to guarantee, so you can tell whether something is a vulnerability:

- **Only the paired PC can talk to the phone, and vice versa.** Both sides use mutual TLS with
  self-signed certificates; each pins the other's SHA-256 fingerprint at pairing. Any other device that
  connects to the phone's port (47800) is rejected after the TLS handshake.
- **Pairing can't be relayed.** The QR code (or the USB hand-over) carries a single-use 128-bit token
  that never goes over the network. Each side proves it holds the token with an HMAC over both
  certificate fingerprints, so a man-in-the-middle with its own certificate fails.
- **Discovery leaks little.** The UDP responder (47801) only answers PCs it is paired with (or is
  currently pairing with), by device ID.
- **The PC never listens.** It only makes outgoing connections, so Windows needs no inbound firewall rule.
- **Mirroring is never silent.** The PC won't start a stream unless the Orbit link is up, so the phone
  always shows a LIVE notice with Stop.

Known trade-offs (by design, not vulnerabilities):

- **USB debugging** (needed for USB pairing, auto tethering and mirroring) gives any computer you
  authorize full control of the phone. Only authorize your own PC, and turn it off if you don't need it.
- `WRITE_SECURE_SETTINGS` is granted to Orbit over adb for wireless mirroring.
- Anyone using your unlocked PC can see mirrored notifications. Use *Hide notification text* or
  *Show while this PC is locked* in Settings to limit this.
- A device on the same network can see that something listens on port 47800, and can attempt
  (failing) TLS handshakes.
