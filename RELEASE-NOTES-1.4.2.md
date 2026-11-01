# WiFi Screen 1.4.2

Samsung AirPlay connection and audio fixes, with PC mute synchronization.

- Recognizes TVs configured with an AirPlay password and preserves the password exactly. On-screen PIN pairing remains available, with corrected keep-alive handling and clearer failures.
- Uses discovered AirPlay service ports and the appropriate LAN adapter.
- Corrects PTP audio synchronization and uses an independent 64-bit encryption nonce so audio continues safely beyond RTP sequence wraparound.
- Makes TV audio follow Windows' default playback-device mute state. The system-audio Enabled/Disabled switch remains; the separate receiver volume regulator has been removed.
- Adds automatic browser-viewer compatibility fallback for failed or stalled WebSocket streams and decoding errors, plus aspect-ratio handling for older browsers.

The user confirmed picture, audible system audio, and PC mute/unmute on a Samsung UE65TU8000. Native checks, TypeScript and production builds, 37 sender tests, 8 browser-viewer tests, and Windows mute-monitor helper checks passed. Actual TV FPS and extended-duration stability remain unmeasured. See [VALIDATION.txt](VALIDATION.txt).

Download [WifiScreen.zip](https://github.com/puwupuwu/wifi-screen/releases/latest/download/WifiScreen.zip), extract the entire archive, and run `WifiScreen.exe`. Keep the complete `AirPlay` runtime folder beside it, including `Sender/resources` and its helpers. Source archives and third-party licenses are retained.
