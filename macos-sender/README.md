# Projector Connect for macOS

Shares one Mac display and system playback audio with the Google TV application in this repository. The window follows the Windows sender's palette, device cards, sharing options, status area and collapsed manual connection/diagnostic sections. No receiver changes or FFmpeg installation are required.

## Build and run

Requires macOS 15 or newer, Node.js 22 or newer, and a receiver on the same trusted LAN.

```sh
cd macos-sender
npm ci
npm test
npm start
```

Build a standalone application for the current Mac architecture:

```sh
npm run pack
```

Open `dist/mac-arm64/Projector Connect.app` on Apple Silicon, or `dist/mac/Projector Connect.app` on Intel. Use the packaged app for audio permission testing; development mode attributes permissions to Electron or the launching terminal. `npm run dist` builds a DMG. Distribution to other Macs requires your Apple signing identity and notarization; local builds are unsigned if no identity is configured.

## Sharing

1. Open Screen Receiver on Google TV and leave it enabled.
2. Select an available receiver and click **Share screen**, or expand **Connect by IP address** and enter the address shown on the TV (default port 53530).
3. Choose your monitor in the native macOS screen picker and allow screen/system-audio recording. Enable audio in that picker if offered. Only the selected display is sent; microphone capture is never requested.
4. Click **Stop sharing**, close the window, or stop from the macOS sharing indicator to disconnect.

If permissions were denied, enable Projector Connect in **System Settings → Privacy & Security → Screen & System Audio Recording** (and System Audio Recording, where shown), then quit and reopen it. macOS can change permission naming across releases. If an audio-enabled request returns no live audio track, the app reports an error rather than silently sharing video only. Turn off **System audio** for intentional silent sharing.

Local playback remains enabled on macOS. The Windows sender's projector-only mute/restoration behavior is not implemented. The corresponding option stays checked and explains this difference. **Media keys control TV** is opt-in; registered volume keys send the existing receiver volume commands while connected and are released on disconnect. Other applications can prevent registration, reported in Technical details. When enabled, these keys control TV volume while local playback continues; adjust Mac volume before connecting if needed.

## Transport and troubleshooting

Bonjour browses `_screenreceiver._tcp`, validates `protocol=1`, decodes `name64`, and keys cards by the app-scoped receiver ID. Discovery never opens a media connection. Manual addresses accept hostnames, IPv4 and bracketed IPv6 with an optional port. Streaming uses newline-delimited JSON over TCP and browser Unified Plan WebRTC with H.264 and Opus, without external STUN/TURN. Video requests up to 1920×1080 at 30 fps and a 6 Mbps bitrate; actual dimensions and bitrate depend on the display, encoder and negotiated receiver capabilities.

Both devices need direct LAN connectivity. Guest Wi-Fi, VPNs, multicast filtering and firewalls may prevent discovery or media; try a manual address if discovery is unavailable. TCP connection timeout is 10 seconds; media setup timeout is 45 seconds. Capture tracks, peer connections, media-key registrations, TCP and display-sleep prevention are released when stopping or on failure.

Tests cover manual-address validation, fragmented/coalesced TCP framing, UTF-8, signaling size limits, receiver TXT decoding, and a real loopback TCP session exercising hello/offer/answer/bye and resource cleanup. End-to-end display/audio capture, permissions, media keys and Google TV playback require a physical Mac/TV check. Do not interpret successful packaging as that hardware validation.

Signaling is plaintext and unauthenticated. Use a trusted LAN and do not expose TCP 53530 publicly. Diagnostic logs can contain receiver names and local addresses.

Electron capture behavior: [desktopCapturer documentation](https://www.electronjs.org/docs/latest/api/desktop-capturer). The app uses the native system picker to let macOS manage capture authorization.
