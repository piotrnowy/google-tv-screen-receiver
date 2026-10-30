# Projector Connect

Projector Connect is the Windows desktop front end for the existing H.264/Opus sender. It uses Windows Forms supplied by .NET 10 and the same streaming session as the console sender. No cloud service or new media framework is used.

## Everyday use

1. Install the updated Google TV receiver and open it once, or start Streaming through Projector Control. Discovery is available while its background receiver is listening.
2. Open **Projektor** on Windows, or launch `windows-sender/publish/ScreenSender.Desktop.exe`.
3. Choose the receiver by name. A single receiver is preselected for convenience; sharing always requires **Connect selected**. With multiple receivers, select the row you want. The address distinguishes receivers with the same name.
4. Choose **System audio**, **Also play on this PC**, and **Media keys control TV** as appropriate. Projector-only audio and remote media keys remain the defaults.
5. Choose **Connect selected**. Status changes from connecting to sharing only after the existing media pipeline has started.
6. Choose **Disconnect**, or close the window, to cancel/stop the session and restore the previous Windows audio state. Closing waits for streaming cleanup.

Browsing never starts sharing or probes the TV's signaling socket. The list shows Available or In use from current service announcements, updates automatically, and removes stale receivers within 30 seconds. A receiver already in use cannot be connected through the selection button. Manual connections are explicit and retain the existing receiver's one-sender behavior.

## Receiver names

The default name comes from Google TV's device name, falling back to its model. In Screen Receiver, choose **Receiver name → CHANGE RECEIVER NAME**, enter a name such as “Living room projector”, and save. The custom name survives app restarts/updates and is announced to Windows. Names support Unicode, with limits of 48 code points and 180 UTF-8 bytes to fit a bounded TXT field. Name editing is optional and opens the keyboard only when requested.

Each installation also saves a random app-specific identity so duplicate or changing names do not merge different receivers. Clearing app data resets that identity. No account, device serial or hardware ID is advertised.

## If discovery finds nothing

Keep both devices on the same trusted LAN and start the TV receiver. Allow the Windows application on the private network if Windows Firewall prompts. Guest-network isolation, VPNs, blocked UDP 5353 and VLAN separation can prevent mDNS discovery. The first version browses IPv4; manual connections also support IPv6 literals in brackets.

Enter the address displayed by Screen Receiver and choose **Connect by address**. An optional `:port` is accepted; the default is 53530. Older receiver APKs remain usable manually, but cannot appear in discovery until updated. The manual field can use an existing ignored `appsettings.json` host; it never autoconnects.

The diagnostics panel shows bounded connection logs. FFmpeg remains an external runtime requirement: use FFmpeg 8.1 x64 shared libraries, `FFMPEG_ROOT`, or `publish/ffmpeg/bin`, as described in the [sender guide](../../windows-sender/README.md). A missing shared runtime produces a visible connection error.

## Build and validate

From `windows-sender/`:

```powershell
dotnet restore
dotnet build
dotnet run --project ScreenSender.Windows.Tests
dotnet run --project ScreenSender.Desktop
```

Publish and create the desktop shortcut:

```powershell
dotnet build -c Release
dotnet publish ScreenSender.Desktop/ScreenSender.Desktop.csproj -c Release -r win-x64
.\Create-DesktopShortcut.ps1
```

On Android, build the updated announcement/name UI with the normal root command:

```powershell
.\gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
```

Physical checks: verify the receiver appears with its current/custom name; select among multiple receivers if available; connect and inspect detailed desktop video and system audio; check remote media keys; disconnect and verify exact prior Windows audio state. Check short/long HOME, D-pad, BACK and software dimming on the updated TV APK. Unit and local WebRTC tests do not replace those physical checks.
