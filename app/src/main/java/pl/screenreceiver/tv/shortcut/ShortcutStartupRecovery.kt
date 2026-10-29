package pl.screenreceiver.tv.shortcut

import android.Manifest
import android.app.job.JobInfo
import android.app.job.JobScheduler
import android.content.ComponentName
import android.content.Context
import android.content.pm.PackageManager
import android.provider.Settings
import android.util.Log

/** Optional workaround for firmware that replaces the accessibility selection during boot.
 * Requires an explicit one-time ADB grant and opt-in; ordinary installs remain user-enabled.
 */
object ShortcutStartupRecovery {
    private const val ENABLED = "screenreceiver_home_startup_recovery"
    private const val JOB_ID = 8001
    private const val TAG = "ProjectorShortcut"

    fun available(context: Context): Boolean =
        context.checkSelfPermission(Manifest.permission.WRITE_SECURE_SETTINGS) == PackageManager.PERMISSION_GRANTED

    fun enabled(context: Context): Boolean = available(context) &&
        Settings.Secure.getInt(context.contentResolver, ENABLED, 0) == 1

    fun setEnabled(context: Context, enabled: Boolean) {
        check(available(context)) { "Startup recovery needs one-time ADB setup" }
        check(Settings.Secure.putInt(context.contentResolver, ENABLED, if (enabled) 1 else 0)) {
            "Could not save startup recovery preference"
        }
        if (enabled) schedule(context) else context.getSystemService(JobScheduler::class.java).cancel(JOB_ID)
    }

    fun schedule(context: Context) {
        if (!enabled(context)) return
        // Give the vendor's BOOT_COMPLETED handler time to finish replacing its selection.
        // JobScheduler owns the delay so the broadcast/process need not remain alive.
        val job = JobInfo.Builder(JOB_ID, ComponentName(context, ShortcutRecoveryJobService::class.java))
            .setMinimumLatency(15_000)
            .setOverrideDeadline(45_000)
            .build()
        val result = context.getSystemService(JobScheduler::class.java).schedule(job)
        Log.i(TAG, "Startup recovery ${if (result == JobScheduler.RESULT_SUCCESS) "scheduled" else "could not be scheduled"}")
    }

    internal fun restore(context: Context) {
        if (!enabled(context)) return
        val resolver = context.contentResolver
        val current = Settings.Secure.getString(resolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES)
        val selection = ShortcutServiceSelection.includeProjector(current)
        if (selection != current) {
            check(Settings.Secure.putString(resolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES, selection)) {
                "Could not restore HOME shortcut selection"
            }
        }
        if (Settings.Secure.getInt(resolver, Settings.Secure.ACCESSIBILITY_ENABLED, 0) != 1) {
            check(Settings.Secure.putInt(resolver, Settings.Secure.ACCESSIBILITY_ENABLED, 1)) {
                "Could not enable HOME shortcut access"
            }
        }
        check(ShortcutServiceSelection.containsProjector(
            Settings.Secure.getString(resolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES),
        )) { "Firmware replaced the restored HOME shortcut selection" }
        Log.i(TAG, "Startup selection restored; Android manages service binding")
    }
}
