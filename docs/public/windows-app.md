# Projector Connect

Projector Connect is the Windows desktop front end for the existing H.264/Opus sender. It uses Windows Forms supplied by .NET 10 and the same streaming session as the console sender. No cloud service or new media framework is used.

## Everyday use

1. Install the updated Google TV receiver and open it once, or start Streaming through Projector Control. Discovery is available while its background receiver is listening.
2. Open **Projektor** on Windows, or launch `windows-sender/publish/ScreenSender.Desktop.exe`.
3. Choose a receiver in **Available devices**. A single receiver is preselected for convenience; sharing always requires **Share screen**. Multiple receivers are shown as compact cards with friendly names, readiness and muted addresses. The selection has a visible outline.
4. Under **Sharing options**, choose **System audio**, **Play audio on this PC**, and **Media keys control TV**. Inapplicable options are disabled. Projector-only audio and remote media keys remain the defaults.
5. Choose **Share screen**. The action becomes **Connecting...**, with progress and an optional Cancel action. Once the existing media pipeline starts, the status reads **Sharing with [device]** and the main action becomes **Stop sharing**.
6. Choose **Stop sharing**, or close the window, to stop the session and restore the previous Windows audio state. Closing waits for streaming cleanup.

Browsing never starts sharing or probes the TV's signaling socket. The list shows Ready to connect, Connecting..., Connected or Unavailable, updates automatically, and removes stale receivers within 30 seconds. An unavailable receiver cannot be started through the main selection action. Manual connections are explicit and retain the existing receiver's one-sender behavior.

## A window that fits

The Windows Forms window starts around 900x620 and supports resizing down to 760x520 logical pixels. Per-monitor DPI awareness scales the interface with Windows. Auto/percentage layouts measure wrapped text rather than positioning primary controls with coordinates. A single receiver occupies one compact card; the list grows for additional devices and scrolls after three visible entries.

The sharing status and primary action stay outside the scrolling content. Smaller windows, long device names and expanded advanced sections can use the content scrollbar without hiding Share screen or Stop sharing. Technical logs have their own bounded-height viewer and scrollbars. P&K Applications branding stays in a subtle footer.

Tab moves from devices through the applicable sharing options to the main action, then refresh/advanced controls. Arrow keys choose a device, Space toggles checkboxes, and Enter activates the focused button or default sharing action. Enter in the manual-address field activates its Connect action. Escape does not close the window or terminate sharing. Keyboard focus uses visible outlines.

## Receiver names

The default name comes from Google TV's device name, falling back to its model. In Screen Receiver, choose **Receiver name → CHANGE RECEIVER NAME**, enter a name such as “Living room projector”, and save. The custom name survives app restarts/updates and is announced to Windows. Names support Unicode, with limits of 48 code points and 180 UTF-8 bytes to fit a bounded TXT field. Name editing is optional and opens the keyboard only when requested.

Each installation also saves a random app-specific identity so duplicate or changing names do not merge different receivers. Clearing app data resets that identity. No account, device serial or hardware ID is advertised.

## If discovery finds nothing

Keep both devices on the same trusted LAN and start the TV receiver. Allow the Windows application on the private network if Windows Firewall prompts. Guest-network isolation, VPNs, blocked UDP 5353 and VLAN separation can prevent mDNS discovery. The first version browses IPv4; manual connections also support IPv6 literals in brackets.

Expand **Connect by IP address**, enter the address displayed by Screen Receiver, and choose **Connect**. Invalid addresses show an error beside the field. An optional `:port` is accepted; the default is 53530. Older receiver APKs remain usable manually, but cannot appear in discovery until updated. The manual field can use an existing ignored `appsettings.json` host; it never autoconnects.

## Errors and technical details

Errors use a plain-language title and explanation; original exceptions stay in the diagnostics. **Try again** repeats the last attempted endpoint. Selecting a different receiver returns the main action to Share screen.

**Technical details** is collapsed by default. Expand it for the existing bounded connection logs and **Copy logs**. Technical paths, ports, codec/RTP/volume diagnostics and original exceptions are kept here. Review logs before sharing them externally because runtime diagnostics can contain device names and addresses.

FFmpeg remains an external runtime requirement: use FFmpeg 8.1 x64 shared libraries, `FFMPEG_ROOT`, or `publish/ffmpeg/bin`, as described in the [sender guide](../../windows-sender/README.md). A missing shared runtime produces a friendly connection error with setup information in Technical details.

## Build and validate

From `windows-sender/`:

```powershell
dotnet restore
dotnet build
dotnet run --project ScreenSender.Windows.Tests
dotnet run --project ScreenSender.Desktop.Tests
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

The desktop checks use a simulated backend and never capture/share the desktop. They exercise responsive sizing, the native WinForms child-before-parent/parent/child-after-parent DPI-change sequence at 96/120/144/192 DPI, state transitions, retry, invalid addresses, option dependencies, keyboard navigation and log copying. Physical display settings are not changed. A real monitor transition remains a separate OS/display compatibility check.

Physical checks: verify discovery and names, choose a receiver, Share screen, inspect detailed video/system audio, exercise remote media keys, then Stop sharing and verify exact prior Windows audio state. Resize the window and expand both advanced sections; check that the main action remains accessible. UI tests and local WebRTC checks do not replace this physical test.
