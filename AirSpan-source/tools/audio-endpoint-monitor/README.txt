AirSpan Windows default playback mute monitor
===========================================
Build: powershell -NoProfile -File tools/audio-endpoint-monitor/build.ps1
Requires Windows with .NET Framework 4.x (included in Windows 11).

Run hidden from Electron:
  spawn(exe, ['--parent-pid', String(process.pid)], { windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'] })

Stdout is JSONL, initially and whenever either value changes:
  {"muted":false,"available":true}
  {"muted":true,"available":true}
  {"muted":false,"available":false}
available:false means the current mute state could not be read; its muted
value is not a valid unmute instruction. The app should keep audio gated
until a valid available:true state is received.

The helper polls every 200ms, resolving the default render eConsole endpoint
again each time to follow output device changes. It reads GetMute only.
It never changes Windows mute/volume, invokes TV commands, or captures audio.
Set-method declarations exist only to preserve native COM vtable layout.
The process releases endpoint/volume COM references on every read and resets
the enumerator after a transient error. It retries when no endpoint is present.
It exits when the parent PID exits, or when stdout reports a broken pipe.
--once prints one actual state and exits (0=available, 2=unavailable).

eConsole follows ordinary Windows default playback/system master mute.
eCommunications is a separate voice-call device role; eMultimedia is not
used for ordinary default stream routing, per Microsoft's Core Audio docs:
https://learn.microsoft.com/en-us/windows/win32/coreaudio/getting-the-default-device-endpoint-for-stream-routing
https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-getmute

Development path: tools/audio-endpoint-monitor/audio-endpoint-monitor.exe
Packaged path: resources/audio-endpoint-monitor.exe
The console-subsystem helper must always be spawned with windowsHide:true.
