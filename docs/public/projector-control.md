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

**Sleep timer:** Off, 15, 30, 45, 60, 90 or 120 minutes. Requires Alarms & reminders access and power-control setup below. At expiry the app sends standard HDMI-CEC Standby directly to the projector, even when Google TV is not recorded as the active HDMI source. It never sends a power toggle. An explicitly configured external ViewSonic bridge remains an alternative.

### One-time automatic power-off setup

1. Enable HDMI-CEC and TV power control on the projector and Google TV. The tested ViewSonic module uses `power_control_mode=broadcast`; this may also put other CEC-connected equipment into standby.
2. Enable network/ADB debugging in Google TV Developer options. The tested firmware exposes its debugging daemon on port 5555, including on loopback. Wireless-debugging pairing-only ports are not supported by this path.
3. In the receiver's **Projector Control** settings, select **Authorize automatic power-off**. Approve the debugging key on the TV and select **Always allow**. This grants the app debugging access to this Google TV module. The setup checks connectivity and CEC support without powering off anything.
4. Allow Alarms & reminders, then set the sleep timer through the HOME overlay. Unconfigured shutdown is rejected before an alarm is scheduled.

The app connects only to `127.0.0.1:5555` using its own key stored in app-private, backup-excluded storage. At expiry it runs its fixed-purpose `CecStandbyCommand` as the authorized Android shell. That entry point accepts only `status` and `off`, checks CEC settings and the presence of a TV/projector, and invokes Android's HDMI standby API. No vendor packet, root access, background Mac service or guessed Binder transaction is used. The app exposes no arbitrary shell-command endpoint to network senders. The dadb library supplies the authenticated ADB transport.

**Check power-off connection** revalidates authorization and CEC readiness without turning off the projector. **Disable automatic power-off** cancels the current timer and deletes the app's local key. Android's debugging authorization list can separately be revoked in Developer options. Turning debugging off, revoking authorization, clearing app data or changing firmware may require setup again. The helper uses the Android HDMI system interface from the authorized shell; compatibility must be checked on each firmware.

An accepted command means Android accepted the standby request; it does not prove physical light output is off. Failures are saved as **Sleep timer failed** and shown in the overlay. Scheduling, token validation and cancellation remain independent of power-command delivery.

**Streaming:** shows receiver status and opens the receiver. The foreground receiver can accept a sender while the app is in the background and bring up the stream automatically, subject to Android's activity-launch restrictions.

**Volume:** controls Google TV's media stream. Fixed-volume outputs are reported as unavailable.

There is no LED light-source row. Actual LED brightness control has not been verified. Direct standard CEC standby was physically confirmed on the tested ViewSonic projector; ordinary Android sleep alone only stopped Google TV while the source was inactive. The retained ViewSonic RS-232/LAN transport is an optional external bridge. Unknown vendor CEC, Binder, serial or sysfs commands must not be replayed or guessed.

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

On 2026-11-05, a shortened test timer was restored after restarting the receiver and ran with the Mac disconnected. The projector physically powered off, confirmed by the user. The saved result was `Sleep timer expired: HDMI-CEC projector standby command sent`, and the timer token was cleared. The shortened deadline was staged only for that device test; the application retains its normal 15–120 minute options.

After installation, verify short HOME, long HOME, D-pad and BACK with the physical remote. Apply 40 percent brightness, close the panel, then restore 100 percent. Verify sender connect/disconnect, H.264 video, Opus system audio and media-key volume with the Windows sender. If using startup recovery, repeat the HOME checks after a real restart without first launching the app.

```powershell
adb -s '<CURRENT_ADB_DEVICE>' logcat -s SleepTimer ProjectorControl ProjectorShortcut ProjectorOverlay ReceiverServer WebRtcReceiver
```

Review logs for addresses and device names before sharing. Raw device evidence and copied vendor binaries are intentionally excluded from this repository.
