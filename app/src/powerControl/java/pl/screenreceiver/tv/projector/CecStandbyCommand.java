package pl.screenreceiver.tv.projector;

import java.lang.reflect.InvocationTargetException;
import java.util.List;

/** Fixed-purpose entry point run by app_process as the user-authorized ADB shell, never as the app. */
public final class CecStandbyCommand {
    private CecStandbyCommand() {}

    public static void main(String[] args) {
        try {
            int uid = (Integer) Class.forName("android.os.Process").getMethod("myUid").invoke(null);
            if (uid != 2000 && uid != 0) {
                throw new SecurityException("Android debugging authorization is required");
            }
            if (args.length != 1 || !(args[0].equals("status") || args[0].equals("off"))) {
                throw new IllegalArgumentException("Expected status or off");
            }
            Object binder = Class.forName("android.os.ServiceManager")
                .getMethod("getService", String.class).invoke(null, "hdmi_control");
            if (binder == null) throw new IllegalStateException("HDMI-CEC is unavailable");
            Class<?> api = Class.forName("android.hardware.hdmi.IHdmiControlService");
            Object service = Class.forName("android.hardware.hdmi.IHdmiControlService$Stub")
                .getMethod("asInterface", Class.forName("android.os.IBinder")).invoke(null, binder);
            int enabled = (Integer) api.getMethod("getCecSettingIntValue", String.class)
                .invoke(service, "hdmi_cec_enabled");
            String mode = (String) api.getMethod("getCecSettingStringValue", String.class)
                .invoke(service, "power_control_mode");
            if (enabled != 1 || !("broadcast".equals(mode) || "to_tv".equals(mode) || "to_tv_and_audio_system".equals(mode))) {
                throw new IllegalStateException("Enable HDMI-CEC and TV power control in Google TV settings");
            }
            int[] types = (int[]) api.getMethod("getSupportedTypes").invoke(service);
            boolean playback = false;
            for (int type : types) if (type == 4) playback = true;
            if (!playback) throw new IllegalStateException("This device is not an HDMI-CEC playback source");
            List<?> devices = (List<?>) api.getMethod("getDeviceList").invoke(service);
            boolean projector = false;
            for (Object device : devices) {
                int address = (Integer) device.getClass().getMethod("getLogicalAddress").invoke(device);
                if (address == 0) projector = true;
            }
            if (!projector) throw new IllegalStateException("No TV/projector found on HDMI-CEC");
            if (args[0].equals("off")) {
                // Standard, idempotent CEC Standby. Does not depend on active-source bookkeeping.
                api.getMethod("sendStandby", int.class, int.class).invoke(service, 4, 0);
                System.out.println("PROJECTOR_CEC_STANDBY_REQUESTED");
            } else {
                System.out.println("PROJECTOR_CEC_READY");
            }
            System.out.flush();
            System.exit(0); // Binder worker threads must not keep app_process alive.
        } catch (Throwable error) {
            Throwable cause = error instanceof InvocationTargetException && error.getCause() != null ? error.getCause() : error;
            System.err.println("Projector control unavailable: " + cause.getMessage());
            System.err.flush();
            System.exit(1);
        }
    }
}
