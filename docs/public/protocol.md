# LAN signaling

The Android receiver and Windows sender use TCP port **53530**, with newline-delimited UTF-8 JSON. SDP line breaks are escaped within JSON. H.264 video and Opus audio use the same WebRTC PeerConnection; signaling remains open throughout the session. No cloud signaling or public STUN/TURN is configured.

| Type | Direction | Fields |
| --- | --- | --- |
| `hello` | Sender to TV | `deviceName` |
| `offer` | Sender to TV | `sdp` |
| `answer` | TV to sender | `sdp` |
| `ice` | Both | `sdpMid` (nullable), `sdpMLineIndex`, `candidate` |
| `bye` | Sender to TV | None |
| `getVolume` | Sender to TV | None |
| `volume` | Sender to TV | `action`: `up`, `down`, or `muteToggle` |
| `volumeState` | TV to sender | `current`, `max`, `muted`, `available` |

The code also retains reserved absolute-volume/mute messages. Use the implementations and compatibility tests as the authority for field validation before extending the protocol.

The TV answers a sender's Unified Plan offer. Both sides queue incoming ICE until their remote description is ready. One sender and one initial offer are supported per TCP session. `bye`, TCP closure or service stop cleans up the peer; reconnect to renegotiate. The receiver continues listening after a normal sender disconnect.

The receiver renders video with aspect-fit scaling and plays audio through the media stream. The sender captures one desktop monitor and default Windows playback through WASAPI loopback, without microphone capture. Its FFmpeg path respects packed BGR stride and converts/scales to YUV420P before H.264 encoding. Opus uses 48 kHz stereo, with 20 ms frames.

Signaling is plaintext and unauthenticated. Use a trusted local network; do not forward the signaling port to the internet. Endpoint addresses, sender names and SDP/ICE can appear in diagnostics. Runtime device identification is sent to the connected receiver, not to a cloud backend.

## Receiver discovery

Discovery is independent of the TCP/media protocol. While the foreground receiver has actually bound its signaling listener, Android NSD advertises `_screenreceiver._tcp.local` over mDNS (UDP 5353). The SRV record supplies the real signaling port. Discovery never opens TCP, creates a PeerConnection, closes the overlay or changes volume.

TXT attributes: `protocol=1`, `id=<app-scoped UUID>`, `name64=<base64 of UTF-8 receiver name>`, `busy=0|1`. The ASCII instance label uses an app-generated identifier; no device serial, account or hardware identifier is used. The name is base64-encoded because the pinned Windows DNS parser accepts only ASCII TXT character strings. This encoding is not encryption; the friendly name is intentionally visible on the local network.

Windows resolves PTR/SRV/TXT/A records, including fragmented answers, checks discovery version and name/ID validity, and deduplicates by app identity. IPv4 browsing refreshes every five seconds and expires unrefreshed records after at most 30 seconds. DNS goodbye removes a stopped receiver earlier. Manual IPv4/hostname/IPv6 connections remain available. Announcements are withdrawn/replaced when the listener stops, the name changes or the connected/busy state changes.

Discovery does not guarantee that the TV will remain reachable when Connect is clicked; a connection timeout/error remains visible in the Windows window. Multicast normally stays on one LAN segment and may be blocked by guest Wi-Fi, VPN/firewall rules or VLAN boundaries.
