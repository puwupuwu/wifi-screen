WiFi Screen 1.3 — AirPlay connection fix
========================================
Extract this COMPLETE ZIP and run WifiScreen.exe.
AirPlay sender > Open AirPlay sender launches the corrected AirSpan build.
Keep all files in AirPlay/Sender together; its EXE needs that runtime folder.
You may also launch AirPlay/Sender/AirSpan.exe directly.

FIX VERIFIED WITH YOUR TCL
The original sender found zero receivers and preferred the VPN address
10.212.134.181 for browser sharing. The corrected sender chooses the network
route to the TV (Wi-Fi 192.168.100.9), queries each adapter, and finds your
TCL 75C845 at 192.168.100.10:7000. The AirSpan design is preserved.

The live test completed discovery, TV PIN pairing, media connection and
H.264 transmission. You confirmed the PC screen was visible on the TV.
System-audio packets were also sent; audible playback was not confirmed.

USING AIRPLAY
1. Select TV / TCL in the sender.
2. Enter the PIN shown on the TV if requested.
3. After first pairing, select Reconnect.
4. Click Share screen.
5. Use Stop sharing / Disconnect in AirSpan when finished.
The sender now reports a useful error instead of capturing with no receiver.
Pairing stays in AirSpan's existing Windows user profile; it is not included
in this ZIP. Avoid running the old and fixed senders at the same time.
Closing WiFi Screen does not close AirSpan's separate sharing session.

BROWSER SHARING
Keep the same browser link checked to reuse a bookmarked link. Default port:
8765. Settings/private token are saved in %LOCALAPPDATA%\WifiScreen\sharing.xml.
Reserve the PC's Wi-Fi IP in your router so its address remains stable.
Choose 30 fps and the balanced size, or the smaller size for slower TVs.
The TV page reports measured displayed FPS. This browser path uses JPEG,
not AirPlay, and has no audio. AirSpan has a separate browser-display mode.

PRIVACY
Anyone with the saved browser link can view future sessions while sharing
is enabled. Use a trusted network; browser transport is unencrypted.
Replace saved private link to revoke it. Stop browser sharing ends only the
WiFi Screen browser session; use AirSpan's Stop/Disconnect for AirPlay.

SOURCE AND VALIDATION
The main EXE source is in source/. Rebuild with source/build.ps1.
AirPlay/AirSpan-fixed-source.zip contains the modified sender source.
AirPlay/FIX-NOTES.txt describes changes, dependencies and rebuilding.
VALIDATION.txt records the live TV test and remaining limits.
