# Modified AirSpan 1.0.5 — Windows build

This is the modified sender bundled with WiFi Screen 1.4.2, based on AirSpan
1.0.1. It includes Samsung password pairing, corrected audio synchronization,
and Windows PC mute synchronization. It is not an upstream AirSpan release.

## Prerequisites

- Windows 10/11 x64 and Windows PowerShell.
- Node.js 22 or newer, with npm available on PATH.
- .NET Framework 4.x and its C# compiler (`csc.exe`). The monitor build checks
  the standard Framework64 and Framework locations beneath `%WINDIR%`.
- The FairPlay helper at `tools/fpsap-helper/fairplay-helper.exe`, as described
  below.

## Dependencies and checks

The frontend shipped in this release uses `pnpm-lock.yaml`. With pnpm
11.25.0 installed, run these commands from this folder:

```powershell
pnpm install --frozen-lockfile
pnpm test
pnpm typecheck
pnpm run desktop:dir
```

The npm lockfile is also retained. An npm build uses its recorded dependency
resolution; it may differ from the pnpm frontend dependency resolution:

```powershell
npm.cmd ci
npm.cmd test
npm.cmd run typecheck
npm.cmd run desktop:dir
```

Use `desktop:dist` for the installer or `desktop:portable` for a portable EXE.
These Windows packaging commands first build the PC mute helper, then the
frontend, then run electron-builder. Outputs are written beneath `dist/`.

## Windows PC mute helper

`tools/audio-endpoint-monitor/Program.cs` is compiled by
`tools/audio-endpoint-monitor/build.ps1`. To build only that helper, run:

```powershell
npm.cmd run build:mute-helper
```

electron-builder copies it to `resources/audio-endpoint-monitor.exe`.
Development runs use the EXE beneath `tools/audio-endpoint-monitor/`.
Electron starts it with `windowsHide: true` and the parent PID, then reads its
bounded JSONL output. The helper reads the default Windows playback endpoint's
mute state; it never changes OS mute or volume. TV audio stays silent while
the PC is muted or its mute state is unavailable. See the helper's README and
validation file for its interface and lifecycle checks.

The manual system-audio switch remains independent. RTP timing continues
while muted. There is no receiver-volume regulator in this version.

## FairPlay helper and licenses

For a source checkout, either copy the release's
`AirPlay/Sender/resources/fairplay-helper.exe` to `tools/fpsap-helper/`, or
rebuild the helper with the Go version specified in `tools/fpsap-helper/go.mod`:

```powershell
Push-Location tools/fpsap-helper
go build -o fairplay-helper.exe .
Pop-Location
```

The release includes the exact upstream helper-library source archive and
LGPL/GPL/Blue Oak notices. For modifying or relinking that dependency, follow
`AirPlay/INTEGRATION.txt` in the complete WiFi Screen release. Retain
`LICENSE`, `THIRD-PARTY-NOTICES.md`, and the runtime's Electron/Chromium notices
when redistributing. The Windows mute helper is original code covered by this
modified sender's MIT license.

The desktop app serves its UI locally. Profiles, saved pairing credentials,
logs, and captured media belong outside the source and release packages.
