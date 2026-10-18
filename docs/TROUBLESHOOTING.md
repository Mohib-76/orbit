# Troubleshooting

## The PC can't find my phone

Orbit connects fastest when the phone is the laptop's gateway (USB tethering or the phone's hotspot),
or over a USB cable with USB debugging on. On a shared Wi-Fi it searches the network.

1. Make sure Orbit is open on the phone at least once after installing, and that it isn't being stopped in the background: on the phone, *Settings → Stay connected in the background*, and see [dontkillmyapp.com](https://dontkillmyapp.com) for your brand.
2. **Same Wi-Fi but still not found?** Many routers (guest networks, offices, hotels, some mesh systems) block devices from seeing each other, and VPNs on either device can hide the network. Try USB tethering or the hotspot, or:
3. **Enter the address manually:** on the phone, *Settings → This phone's address*; on the PC, *Settings → Connection → Phone address*.
4. Still stuck? Export diagnostics on both devices (*Settings*) and open an issue.

## Turn on USB debugging

Needed for Pair over USB, auto USB tethering and screen mirroring.

1. On the phone: *Settings → About phone → Software information* (varies by brand) → tap **Build number** seven times.
2. *Settings → Developer options* → turn on **USB debugging**.
3. Plug into the PC and tap **Allow** on the phone (tick *Always allow from this computer*).

Orbit's PC Settings → Connection → USB shows whether the phone is authorized.

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

## Earbuds don't show controls

- Only realme / OnePlus buds that use the realme Link or HeyMelody app are supported.
- The buds must be paired with the device over Bluetooth and connected.
- Close other apps that control the buds (including the vendor app): only one app per device can hold their control channel.
- Android needs the *Nearby devices* (Bluetooth) permission.

## Orbit says one app needs updating

Both apps must speak the same protocol. Install the latest release on the device the message names.
