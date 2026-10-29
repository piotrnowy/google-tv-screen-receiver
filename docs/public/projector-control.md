# Projector Control

Tested on a ViewSonic X1-4K Pro running Google TV. Compatibility with other TV firmware and remotes requires device testing.

## Setup and navigation

Install the receiver, open it once, and enable **Projector Control** in Android Accessibility settings. The service filters remote keys; it does not retrieve window content. No separate draw-over-other-apps permission is required.

- Hold physical HOME for about 800 ms to open or close the overlay.
- Short HOME opens the normal Google TV launcher.
- UP/DOWN selects a row; LEFT/RIGHT changes a value; OK applies it.
- BACK closes the overlay.

HOME handling depends on the firmware. An ADB-injected HOME event does not establish that a physical remote works.

## Available controls

**Screen brightness:** 20, 40, 60, 80 or 100 percent. A black accessibility overlay darkens the Android image across ordinary applications. The selected level persists when the panel closes and is restored when the service reconnects. Select 100 percent to remove software dimming. This does not reduce projector LED power or change the projector's native OSD. Protected or tunneled video needs application-specific testing.

**Sleep timer:** Off, 15, 30, 45, 60, 90 or 120 minutes. Requires Alarms & reminders access. Timer state exists, but actual projector shutdown requires a separately verified and configured control endpoint. An unconfigured timer cannot power off this projector.

**Streaming:** shows receiver status and opens the receiver. The foreground receiver can accept a sender while the app is in the background and bring up the stream automatically, subject to Android's activity-launch restrictions.

**Volume:** controls Google TV's media stream. Fixed-volume outputs are reported as unavailable.

There is no LED light-source row. No built-in Android path to actual projector LED power or power-off has been verified. The retained ViewSonic protocol/transport code is separate from the working screen-dimming feature. Unknown vendor CEC, Binder, serial or sysfs commands must not be replayed or guessed.

## Optional HOME startup recovery

On the tested firmware, a reboot can replace the saved accessibility-service selection. HOME diagnostic history also resets with the app process; it is not a calibration step.

The optional setup script installs the APK, grants `WRITE_SECURE_SETTINGS` through ADB, opts into recovery, and appends this service while preserving other accessibility entries:

```powershell
adb devices -l
.\Install-ProjectorControl.ps1 -DeviceSerial '<CURRENT_ADB_DEVICE>'
```

This broad Android permission is optional and cannot be self-granted by the APK. Review the script before use. Without the grant and explicit opt-in, recovery does nothing. Boot and package updates schedule one delayed JobScheduler recovery attempt; Android decides execution timing. The attempt checks selection readback, while actual binding and remote behavior remain separate checks. Firmware may subsequently overwrite the selection again; recovery is not a continuous watchdog.

Turn **STARTUP RECOVERY** off inside the app before disabling the service if it should remain disabled across restarts. To remove the optional permission afterward:

```powershell
adb -s '<CURRENT_ADB_DEVICE>' shell pm revoke pl.screenreceiver.tv android.permission.WRITE_SECURE_SETTINGS
```

## Physical verification

After installation, verify short HOME, long HOME, D-pad and BACK with the physical remote. Apply 40 percent brightness, close the panel, then restore 100 percent. Verify sender connect/disconnect, H.264 video, Opus system audio and media-key volume with the Windows sender. If using startup recovery, repeat the HOME checks after a real restart without first launching the app.

```powershell
adb -s '<CURRENT_ADB_DEVICE>' logcat -s ProjectorShortcut ProjectorOverlay ReceiverServer WebRtcReceiver
```

Review logs for addresses and device names before sharing. Raw device evidence and copied vendor binaries are intentionally excluded from this repository.
