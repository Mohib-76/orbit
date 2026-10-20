# Troubleshooting

## The PC can't find my phone

Orbit connects fastest when the phone is the laptop's gateway (USB tethering or the phone's hotspot),
or over a USB cable with USB debugging on. On a shared Wi-Fi it searches the network.

1. Make sure Orbit is open on the phone at least once after installing, and that it isn't being stopped in the background: on the phone, *Settings → Stay connected in the background*, and see [dontkillmyapp.com](https://dontkillmyapp.com) for your brand.
2. **Same Wi-Fi but still not found?** Many routers (guest networks, offices, hotels, some mesh systems) block devices from seeing each other, and VPNs on either device can hide the network. Try USB tethering or the hotspot, or:
3. **Enter the address manually:** on the phone, *Settings → This phone's address*; on the PC, *Settings → Connection → Phone address*.
4. Still stuck? Export diagnostics on both devices (*Settings*) and open an issue.

## Turn on USB debugging

Needed for Pair over USB, USB modes (charging, tethering, file sharing) and screen mirroring.

1. On the phone: *Settings → About phone → Software information* (varies by brand) → tap **Build number** seven times.
2. *Settings → Developer options* → turn on **USB debugging**.
3. Plug into the PC and tap **Allow** on the phone (tick *Always allow from this computer*).

Orbit's PC Settings → Connection → USB shows whether the phone is authorized.

## "App blocked to protect your device"

Android shows this when you install the Orbit APK from a browser, messaging app or file manager on phones where
Google Play Protect's fraud protection is on (India, Brazil, Singapore, Thailand and other countries). It blocks
any app that can read notifications, which Orbit needs for notification mirroring. The APK itself is fine.

- **Install over USB instead:** turn on [USB debugging](#turn-on-usb-debugging), plug the phone into the PC, open
  Orbit on the PC and click **Install on phone** on the pairing screen. Installs from the PC aren't blocked, and
  Orbit pairs right after.
- If the phone asks to allow an install from USB, tap Allow. On Xiaomi, Redmi and POCO also turn on
  **Install via USB** in Developer options.

## Notification access is greyed out ("Restricted setting")

Android 13 and newer restrict notification access for apps installed from a browser or file manager. Open
**Settings → Apps → Orbit**, tap **⋮** (top right) → **Allow restricted settings**, then turn notification access
on. Apps installed over USB with **Install on phone** aren't restricted.

## Notifications don't show on the PC

- Phone: *Settings → Notification access* must say **Granted**, and *Mirror notifications* must be on. Check *Mirrored apps* too.
- PC: *Settings → Notifications → Show as Windows notifications* on, *Do not disturb* off. Windows' own Focus/Do not disturb also hides them.
- Toasts while the PC is locked depend on *Show while this PC is locked*.

## Clipboard doesn't sync

- PC → phone needs *Auto-sync* on the PC and *Auto-receive* on the phone.
- Phone → PC always needs a tap (Android rule): the **Send clipboard** button, the Quick Settings tile, or the notification action.
- Copies from password managers are skipped on purpose (*Skip private copies*).

## A keyboard shortcut does nothing

Another app may already use it. *Settings → Keyboard shortcuts* says so; pick different keys.

## Screen mirroring

- **Over USB:** USB debugging must be on and authorized.
- **Over Wi-Fi:** run the one-time wireless setup in Orbit; phone and PC must be on the same Wi-Fi. Android doesn't offer wireless debugging over the phone's own hotspot.
- Black areas are screens apps mark as secure (banking, passwords). That's Android protecting them.

## Media from the other device doesn't show

- PC: Settings → Media → *Show phone media on this PC*. Phone: Settings → Media → *Show PC media on this phone*.
- The phone reads its players through notification access: make sure it's on for Orbit (see above).
- Phone music in the Windows media controls (and media keys) needs *Use keyboard media keys for the phone* on the PC.
- If the phone can't be controlled from the PC, check *Let the PC control media here* on the phone.

## The USB mode won't switch

- The PC switches the phone's USB mode over adb, so USB debugging must be on and this PC authorized ([how](#turn-on-usb-debugging)).
- Switching is blocked while the phone is mirrored over USB: a switch makes USB reconnect for a moment. Stop mirroring first.
- Orbit never switches when it can't read the phone's current mode. Unplug and plug in again.
- Turning tethering off cuts the laptop's internet if it gets it through the phone; Orbit asks first.

## Earbuds don't show controls

- Any Bluetooth buds show battery and *Play here*. Noise control, sound and tap settings are only for realme / OnePlus / OPPO buds that use the realme Link or HeyMelody app.
- The buds must be paired with the device over Bluetooth and connected.
- Close other apps that control the buds (including the vendor app): only one app per device can hold their control channel.
- Android needs the *Nearby devices* (Bluetooth) permission.

## Orbit says one app needs updating

Both apps must speak the same protocol. Install the latest release on the device the message names.
