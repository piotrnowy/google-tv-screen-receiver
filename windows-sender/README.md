# Windows Screen Sender

Sends a Windows 11 desktop and default system playback audio to the Google TV receiver using TCP signaling on port 53530 and a single WebRTC connection carrying H.264 video and Opus audio.

## Projector Connect desktop app

Run `dotnet run --project ScreenSender.Desktop` from this directory. The Windows window discovers named, active receivers on the local network. Choose a device card and **Share screen**; the same primary action becomes **Stop sharing** while connected. Multiple receivers remain separate even when they have the same name. **Connect by IP address** and **Technical details** are collapsed by default, keeping manual connection and original logs available without cluttering the main workflow.

The TV app now advertises its Google TV name by default. Use **Receiver name → CHANGE RECEIVER NAME** inside Screen Receiver to save a custom name. See [desktop app setup and discovery behavior](../docs/public/windows-app.md).

The existing `ScreenSender.Windows` command-line executable and its options remain available. Both front ends use the same streaming lifecycle, capture, encoding, audio restoration and media-key control code.

## Requirements

- Windows 11 x64 with an interactive desktop and an attached monitor.
- .NET 10 SDK for building. The pinned SIPSorcery packages remain at 10.0.17.
- FFmpeg 8.1.x x64 **shared** libraries with `gdigrab` and `libx264` for actual streaming. A standalone static `ffmpeg.exe` is insufficient.
- A reachable receiver on the same trusted LAN. No microphone, virtual cable or Stereo Mix is required.

## Build and checks

From this directory:

```powershell
dotnet restore
dotnet build
dotnet run --project ScreenSender.Windows.Tests
dotnet run --project ScreenSender.Desktop.Tests
```

`global.json` selects a stable .NET 10 SDK, checking an optional ignored `.tools/dotnet` installation before the machine-wide SDK. The executable checks exercise configuration, signaling, frame layout, audio conversion/Opus, audio-state restoration, media-key handling and local WebRTC interoperability. Physical desktop/audio output still needs an actual TV.

## Configuration and running

There is no built-in receiver address. Use the address displayed by the TV app:

```powershell
dotnet run --project ScreenSender.Windows -- --host '<TV_IP>'
```

For saved settings:

```powershell
Copy-Item ScreenSender.Windows/appsettings.example.json ScreenSender.Windows/appsettings.json
```

Edit `host` in that local file to your TV's address before running. `appsettings.json` is ignored by Git and copied beside the executable when present. The example intentionally leaves `host` empty; passing `--host` also works. Do not overwrite existing local settings when updating an existing checkout.

Priority: CLI arguments, then `appsettings.json` beside the executable, then built-in options. Missing or empty host reports a configuration error before capture starts. Malformed JSON identifies its file and location.

| Option | Default | Meaning |
| --- | --- | --- |
| `--host` | Required via CLI or local file | TV IP address or local hostname |
| `--port` | `53530` | TCP signaling |
| `--monitor` | `primary` | Primary display or positive 1-based display number |
| `--width`, `--height` | `1920`, `1080` | Even-valued bounding box; preserves aspect ratio |
| `--fps` | `30` | Capture frame rate |
| `--audio` | `true` | Default Windows playback via WASAPI loopback |
| `--audio-debug` | `false` | Additional audio-buffer diagnostics |
| `--audio-output` | `projector` | `projector` or `mirror` |
| `--remote-volume` | `true` | Media keys control TV in projector mode |

`--help` prints usage without loading FFmpeg. Ctrl+C disconnects and restores saved local audio state.

The software H.264 encoder uses YUV420P, `veryfast`, `zerolatency`, no B-frames, a two-second GOP and a 6 Mbps target. Output respects the TV's negotiated H.264 level; a level 3.1 receiver selects 1280x720 at 30 fps from a 1920x1080 request. The full desktop is scaled without cropping or stretching.

## FFmpeg

Obtain an FFmpeg 8.1 x64 shared build from a provider linked by the [FFmpeg download page](https://ffmpeg.org/download.html#build-windows). Set the local shared-library directory:

```powershell
$env:FFMPEG_ROOT = 'C:\Tools\ffmpeg-8.1-shared\bin'
```

The directory must contain `avcodec-62.dll`, `avformat-62.dll`, `avutil-60.dll`, `avdevice-62.dll`, `swscale-9.dll`, `swresample-6.dll` and their required dependencies. Keep the native binaries outside source control and review their licenses before redistribution.

Discovery checks `FFMPEG_ROOT`, then `ffmpeg/bin` beside the executable, a compatible shared WinGet installation, then `PATH`. Do not substitute another FFmpeg ABI.

## Audio and volume

Projector mode captures the current default playback endpoint and saves its mute and volume state. After audio RTP starts, it mutes local playback while keeping loopback capture active. Disconnect, cancellation and error cleanup restore the exact prior state. If muting suppresses loopback on the device driver, the safety fallback restores playback. Mirror mode leaves Windows output unchanged.

When projector-only mode and remote volume are active, Windows mute/up/down keys enqueue remote Google TV media-volume commands. Network work runs outside the keyboard-hook callback. Other keys and inactive sessions retain normal Windows behavior. Fixed-volume TVs are reported as unavailable.

## Publish and desktop shortcut

```powershell
dotnet build -c Release
dotnet publish ScreenSender.Desktop/ScreenSender.Desktop.csproj -c Release -r win-x64
```

Output: `publish/ScreenSender.Desktop.exe`. The desktop app is self-contained for .NET and Windows Forms; native FFmpeg remains external. Discovery needs no saved host. An existing source `appsettings.json` is copied to `publish/` for audio settings and the manual-address field. Otherwise, the example can be copied there as `appsettings.json` if saved settings are wanted.

To publish the console sender separately, run `dotnet publish ScreenSender.Windows/ScreenSender.Windows.csproj -c Release -r win-x64`; its output remains `publish/ScreenSender.Windows.exe`.

Place shared FFmpeg libraries in `publish/ffmpeg/bin`, or use another discovery path. Then:

```powershell
.\Create-DesktopShortcut.ps1
```

The helper creates `Projektor.lnk` on the current user's desktop, targeting `ScreenSender.Desktop.exe` with `publish` as its working directory. Existing local configuration and publish output are excluded from Git.

## Physical check

Open/enable the receiver on the TV, run the sender with your TV address, and verify a detailed desktop, smooth motion and system audio. In projector mode check media keys and local mute, then disconnect and verify the exact prior Windows audio state returns. Repeat with audio disabled if video-only behavior is needed.

Signaling is plaintext and unauthenticated. Use a trusted LAN and avoid forwarding TCP 53530. Review diagnostics before sharing: they can contain local addresses, device names and SDP/ICE data.
