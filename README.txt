WiFi Screen 1.4.2
=================
Windows 11 screen sharing over Wi-Fi.

START
1. Extract the COMPLETE WifiScreen.zip.
2. Run WifiScreen.exe.
3. Open AirPlay sender > Open AirPlay sender.
   You can also run AirPlay/Sender/AirSpan.exe directly.
Keep the complete AirPlay/Sender folder with its EXE, including resources,
helpers, and license files. Do not run different sender versions together.

AIRPLAY
1. Select the TV in Receivers.
2. If asked for a password, enter the password configured in the TV's
   Apple AirPlay settings. A password-configured TV need not show a new PIN.
   If asked for a code, enter the code displayed on the TV.
3. Select Reconnect after first pairing if prompted.
4. Choose Share screen and select a display or window.
5. Use Stop sharing and Disconnect in AirSpan when finished.
Closing the native launcher does not close AirSpan's separate session.
Pairing credentials remain in the local Windows user profile and are not
included in the release package.

AUDIO AND PC MUTE
- Keep AirPlay system audio Enabled to transmit system sound. Disabled
  sends silence while video and the receiver's audio clock continue.
- Muting Windows' default playback device also mutes transmitted TV audio.
  Unmuting resumes sound when AirPlay system audio is Enabled.
- The sender displays "PC muted · TV audio muted" while the PC is muted.
  If Windows mute status is unavailable, TV audio stays silent until the
  monitor recovers. Keep the bundled mute-monitor helper in resources.
- Use the TV remote for listening volume. There is no separate receiver
  volume regulator in the sender.
- This shares system sound, not the microphone. If a capture has no audio
  track, stop and start sharing again with system audio available.

NETWORK ADAPTER
Choose Automatic to scan active IPv4 adapters, or choose the Wi-Fi/LAN
adapter connected to the TV. Discovery uses each receiver's advertised
AirPlay port. Stop sharing and disconnect before changing adapters.
Refresh adapters after moving to another network. A missing saved adapter
falls back to Automatic at startup.

BROWSER SHARING
The native launcher's Browser sharing tab provides a separate JPEG viewer.
Start sharing, then open the complete generated link in the TV's browser.
This path has no audio and does not start an AirPlay or Chromecast receiver.

Keep the same browser link checked to reuse a bookmark. The default port
is 8765; settings and the private token are saved in
%LOCALAPPDATA%\WifiScreen\sharing.xml. Reserve the PC's LAN address in the
router if a stable address is needed.

The viewer automatically uses JPEG compatibility mode if WebSocket
streaming fails or stops delivering decoded frames. You can also select
Compatibility mode manually. The page reports measured displayed FPS;
30 FPS is a target. Try the smaller capture size for a slower TV browser.

Anyone with the saved private link can view sessions while browser sharing
is enabled. Use a trusted network; HTTP/WebSocket transport is unencrypted.
Replace saved private link revokes the old link. Stop browser sharing or
close the launcher to end its session; stop AirPlay separately in AirSpan.

SOURCE AND VALIDATION
Repository native source: root-level C# files and build.ps1.
Release native source: source/; run powershell -File source/build.ps1.
Repository sender source: AirSpan-source/.
Release exact sender source: AirPlay/AirSpan-fixed-source.zip.
The sender source includes the Windows audio-endpoint monitor helper.
Follow BUILD-WINDOWS.md in the sender source for rebuild instructions.
AirPlay/FIX-NOTES.txt and third-party notices describe bundled components.

The user confirmed picture, audible system audio, and PC mute/unmute on a
Samsung UE65TU8000. Actual TV FPS and extended-duration stability have not
been measured. See VALIDATION.txt and RELEASE-NOTES-1.4.2.md.
