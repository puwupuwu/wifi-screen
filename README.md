# WiFi Screen 1.4.2

Windows 11 screen sharing over Wi-Fi, with a native launcher and bundled modified AirSpan sender.

## Download

Download [**WifiScreen.zip — latest Windows version**](https://github.com/puwupuwu/wifi-screen/releases/latest/download/WifiScreen.zip), extract the entire ZIP, and run `WifiScreen.exe`. Keep the complete `AirPlay` folder beside it, including the sender's `resources` folder and helpers. The executable is unsigned.

The latest download link follows new releases; existing versioned release links remain available.

## New in 1.4.2

- Samsung AirPlay pairing recognizes a configured password instead of always asking for a new on-screen PIN. PIN pairing remains supported, with corrected connection handling and clearer errors.
- Receiver discovery uses the advertised AirPlay service port and selects the appropriate LAN adapter.
- Corrected AirPlay PTP audio timing and an independent 64-bit audio encryption counter that continues safely past RTP sequence wraparound.
- TV audio follows the PC's default playback-device mute state. The existing system-audio Enabled/Disabled switch remains; the separate receiver volume regulator has been removed.
- Browser sharing automatically falls back to JPEG compatibility mode when WebSocket streaming or image decoding fails, and preserves the picture's aspect ratio on older browsers.

Picture, audible system audio, and PC mute/unmute behavior were confirmed by the user on a Samsung UE65TU8000. Actual TV frame rate and extended-duration stability have not been measured. See [release notes](RELEASE-NOTES-1.4.2.md) and [validation](VALIDATION.txt).

## Sharing options

Use **AirPlay sender → Open AirPlay sender**, select the receiver, complete its password or PIN prompt, and choose **Share screen**. Keep **AirPlay system audio** enabled to transmit system sound. Muting the PC's default playback device mutes TV audio; unmuting restores it when the switch is enabled. Use the TV remote for listening volume.

The launcher also discovers AirPlay, Google Cast, DLNA and DIAL services and provides Windows Miracast and Chrome casting shortcuts. DLNA/DIAL discovery alone does not implement screen mirroring.

The launcher's **Browser sharing** mode sends JPEG screen frames with a 30 FPS target and no audio. Open its generated link in the TV's browser. Enable the stable-link option to reuse a bookmark; reserve the PC's LAN address in the router if needed. This mode uses unencrypted HTTP/WebSocket on a trusted local network, so keep its private link private. Stop browser sharing or close the launcher to end that session. AirPlay sessions must be stopped in AirSpan.

## Build and source

Native launcher source is at the repository root. Run `powershell -File build.ps1` on Windows with .NET Framework 4.x; it writes `WifiScreen.exe` beside the script. The release ZIP includes these files under `source/`.

Modified sender source is in `AirSpan-source/`, including its Windows mute-monitor helper source. Follow `AirSpan-source/BUILD-WINDOWS.md` for dependencies, helper builds, tests and packaging. Runtime binaries are distributed in the release ZIP.

The release also contains the exact modified sender source in `AirPlay/AirSpan-fixed-source.zip`, upstream source archives, helper source and third-party notices. Keep the complete runtime folder when moving or extracting the application. See `AirPlay/FIX-NOTES.txt` and [README.txt](README.txt) for further details.

## Attribution

The AirPlay sender is based on [DiznyP/AirSpan](https://github.com/DiznyP/AirSpan), under its MIT license. Bundled helpers and runtime components retain their own licenses; see the complete release package. No new license is granted here for the original WiFi Screen code.
