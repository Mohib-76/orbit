# Third-party: scrcpy 5.0.1

From the official release `scrcpy-win64-v5.0.1.zip` (https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1),
SHA-256 `b12a2c4ee8be317422451fc7dcf8ee20a71b5ea7ef9ad73ddd825a25316227a5`.

- `scrcpy-server`: the device-side server (Apache License 2.0, see `LICENSE.txt`). Orbit pushes it to the
  phone and talks to it directly (`Core/Mirror/MirrorSession.cs`). The version string Orbit passes must
  match this file: update `MirrorSession.ServerVersion` when replacing it.
- `avcodec-63.dll`, `avutil-61.dll`, `swresample-7.dll`, `avformat-63.dll`: FFmpeg 9.0.2, LGPL build made
  by the scrcpy project (decoders: h264/hevc/av1…, muxers: mp4/matroska).
