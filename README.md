# WiFi Screen 1.4

Windows 11 screen sharing over Wi-Fi, with a native launcher and bundled modified AirSpan sender.

## Download

Download [**WifiScreen.zip — latest Windows version**](https://github.com/puwupuwu/wifi-screen/releases/latest/download/WifiScreen.zip), extract the entire ZIP, and run `WifiScreen.exe`. Keep the `AirPlay` folder beside it. The executable is unsigned.

The repository URL and earlier release links remain valid. Versioned links keep their original release; the latest download link above follows new releases.

## New in 1.4

- Live Enabled/Disabled switch for AirPlay system audio.
- Select the network adapter used for AirPlay discovery, with saved preferences and adapter refresh.
- Automatic scanning or Wi-Fi-only scanning; verified discovery of the TCL TV.

## Features

- AirPlay screen sharing and TV pairing through the bundled AirSpan sender.
- Improved receiver discovery and selection of the correct LAN interface on multi-adapter/VPN systems.
- Browser sharing with a persistent bookmark URL and a 30 FPS target.
- AirPlay, Google Cast, DLNA and DIAL discovery, plus Windows Miracast and Chrome casting shortcuts.

DLNA/DIAL discovery does not implement screen mirroring. Browser mode does not carry audio. Browser capture benchmark was about 28 FPS; actual performance depends on the PC, network and receiver.

AirPlay pairing and visible PC screen playback were confirmed on a TCL 75C845. Audible AirPlay playback was also confirmed with 1.4 after enabling the audio switch and unmuting the TV. Actual TV frame rate was not measured.

## Stable browser URL

Enable the stable URL option and bookmark the displayed address. Reserve the PC's LAN address in your router so it stays stable. Browser sharing uses unencrypted HTTP/WebSocket on the trusted local network; keep the private link private.

## Build and source

Run `powershell -File build.ps1` on Windows with .NET Framework 4.x to rebuild the native application. The executable is written beside the script. For the complete application, place the release's `AirPlay` directory beside it.

The release ZIP includes the exact modified AirSpan source in `AirPlay/AirSpan-fixed-source.zip`, upstream source, helper source, and all third-party notices. See `AirPlay/FIX-NOTES.txt` in the release for its changes and build details. Runtime binaries are distributed as release assets.

See [README.txt](README.txt) for usage and [VALIDATION.txt](VALIDATION.txt) for validation details.

## Attribution

The AirPlay sender is based on [DiznyP/AirSpan](https://github.com/DiznyP/AirSpan), under its MIT license. Bundled helper and runtime components retain their own licenses; see the complete release package. No new license is granted here for the original WiFi Screen code.
