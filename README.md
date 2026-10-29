# Google TV Screen Receiver

Stream a Windows 11 desktop and system audio to Google TV over your local network. Includes a remote-controlled Projector Control overlay, tested on a ViewSonic X1-4K Pro.

## What works

- H.264 video and Opus stereo audio over one WebRTC connection.
- Windows desktop and WASAPI system-audio capture; no microphone capture.
- Background receiver with automatic stream display when a sender connects.
- Projector-only audio mode that restores the previous Windows audio state on disconnect.
- Windows media keys controlling Google TV volume in projector-only mode.
- Long HOME opens Projector Control; short HOME keeps normal launcher behavior.
- D-pad/BACK navigation, persistent software screen dimming and sleep-timer UI/state.

Screen dimming changes the Android image, not projector LED power. Actual projector shutdown needs a separately verified control endpoint. No built-in Android hardware-control path has been verified. iPhone sending, AirPlay and automatic receiver discovery are not implemented.

## Android build and setup

Open the repository in Android Studio and configure an Android SDK supporting the project's compile SDK (API 37). Gradle's daemon toolchain requests JDK 25; app source targets Java 11. Dependency versions are pinned in the existing build files.

From PowerShell at the repository root:

```powershell
.\gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
```

APK: `app/build/outputs/apk/debug/app-debug.apk`.

Enable USB or wireless debugging on the TV, pair/connect ADB as required, then use the current connected identifier:

```powershell
adb devices -l
adb -s '<CURRENT_ADB_DEVICE>' install -r app/build/outputs/apk/debug/app-debug.apk
adb -s '<CURRENT_ADB_DEVICE>' shell am start -n pl.screenreceiver.tv/.MainActivity
```

Open Screen Receiver once, allow notifications/local-network access if prompted, and enable **Projector Control** in Android Accessibility settings if you want the HOME shortcut and dimming. The receiver shows its current LAN address and port.

See [Projector Control setup and limitations](docs/public/projector-control.md) for optional startup recovery and physical remote checks.

## Windows sender

Requires Windows 11 x64, the .NET 10 SDK and FFmpeg 8.1 x64 **shared** libraries for streaming. Building and managed checks do not require native FFmpeg DLLs.

```powershell
cd windows-sender
dotnet restore
dotnet build
dotnet run --project ScreenSender.Windows.Tests
dotnet run --project ScreenSender.Windows -- --host '<TV_IP>'
```

For a saved address, copy `ScreenSender.Windows/appsettings.example.json` to the ignored `ScreenSender.Windows/appsettings.json` and set its `host`. Existing local settings take precedence over built-in options; CLI options take precedence over the file. There is no built-in TV address.

See the [Windows sender guide](windows-sender/README.md) for FFmpeg setup, publishing, audio modes and media keys.

## Network and architecture

```text
Windows desktop + system playback
    -> H.264 + Opus / WebRTC
    -> Google TV receiver
    -> fullscreen video + media audio

TCP :53530 carries JSON signaling, ICE and volume messages.
```

Both devices need direct LAN connectivity. Guest-network isolation, VPN routes and firewalls can block connections. Signaling is plaintext and unauthenticated: use a trusted LAN and do not expose the listener to the internet. No cloud signaling or public STUN/TURN service is used.

See the [protocol notes](docs/public/protocol.md). The Android app uses Kotlin/Compose and `org.webrtc`; the sender uses SIPSorcery 10.0.17 and FFmpeg 8.1 shared libraries.

## Repository contents

- `app/`: Android receiver, Projector Control, resources and JVM tests.
- `windows-sender/`: Windows sender, executable checks and publishing helper.
- `gradle/`, `gradlew`, `gradlew.bat`: Gradle wrapper and dependency versions.
- `Install-ProjectorControl.ps1`: optional explicit ADB setup for HOME startup recovery.
- `docs/public/`: setup, protocol and source-publication notes.

Device captures, screenshots, vendor APKs/decompilations, local configuration, private keys and build products are excluded. See the [publication boundary](docs/public/publication.md) before adding diagnostic files. No project license has been selected; third-party dependencies retain their own licenses.
