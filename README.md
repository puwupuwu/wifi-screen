# WiFi Screen 1.3

Windows 11 screen sharing over Wi-Fi, with a native launcher and bundled modified AirSpan sender.

## Download

Download **WifiScreen-1.3.zip** from [Releases](https://github.com/puwupuwu/wifi-screen/releases/latest), extract the entire ZIP, and run `WifiScreen.exe`. Keep the `AirPlay` folder beside it. The executable is unsigned.

## Features

- AirPlay screen sharing and TV pairing through the bundled AirSpan sender.
- Improved receiver discovery and selection of the correct LAN interface on multi-adapter/VPN systems.
- Browser sharing with a persistent bookmark URL and a 30 FPS target.
- AirPlay, Google Cast, DLNA and DIAL discovery, plus Windows Miracast and Chrome casting shortcuts.

DLNA/DIAL discovery does not implement screen mirroring. Browser mode does not carry audio. Browser capture benchmark was about 28 FPS; actual performance depends on the PC, network and receiver.

AirPlay pairing and visible PC screen playback were confirmed on a TCL 75C845. Actual TV frame rate and audible audio playback were not measured.

## Stable browser URL

Enable the stable URL option and bookmark the displayed address. Reserve the PC's LAN address in your router so it stays stable. Browser sharing uses unencrypted HTTP/WebSocket on the trusted local network; keep the private link private.

## Build and source

Run `powershell -File build.ps1` on Windows with .NET Framework 4.x to rebuild the native application. The executable is written beside the script. For the complete application, place the release's `AirPlay` directory beside it.

The release ZIP includes the exact modified AirSpan source in `AirPlay/AirSpan-fixed-source.zip`, upstream source, helper source, and all third-party notices. See `AirPlay/FIX-NOTES.txt` in the release for its changes and build details. Runtime binaries are distributed as release assets.

See [README.txt](README.txt) for usage and [VALIDATION.txt](VALIDATION.txt) for validation details.

## Attribution

The AirPlay sender is based on [DiznyP/AirSpan](https://github.com/DiznyP/AirSpan), under its MIT license. Bundled helper and runtime components retain their own licenses; see the complete release package. No new license is granted here for the original WiFi Screen code.
