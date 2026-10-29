package pl.screenreceiver.tv.timer

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.SystemClock
import android.provider.Settings
import android.util.Log
import java.util.UUID

class SleepTimerManager(context: Context) {
    private val context = context.applicationContext
    private val prefs = this.context.getSharedPreferences("sleep_timer", Context.MODE_PRIVATE)
    private val alarms = this.context.getSystemService(AlarmManager::class.java)

    fun canSchedule(): Boolean = Build.VERSION.SDK_INT < 31 || alarms.canScheduleExactAlarms()

    fun active(): SleepTimerPlan? = synchronized(lock) {
        val token = prefs.getString("token", null) ?: return@synchronized null
        SleepTimerPlan(token, prefs.getLong("wall", 0), prefs.getLong("elapsed", 0), prefs.getInt("boot", -1))
    }

    fun remainingMs(plan: SleepTimerPlan): Long = plan.remainingMs(
        System.currentTimeMillis(), SystemClock.elapsedRealtime(), bootCount(),
    )

    fun lastResult(): String = prefs.getString("result", "").orEmpty()

    fun set(minutes: Int): String = synchronized(lock) {
        require(minutes in SleepTimerPlan.options)
        if (minutes == 0) {
            alarms.cancel(operation(""))
            check(prefs.edit().remove("token").putString("result", "Sleep timer cancelled").commit())
            Log.i(TAG, "Sleep timer cancelled")
            return@synchronized "Sleep timer cancelled"
        }
        check(canSchedule()) { "Enable Alarms & reminders before setting the sleep timer" }
        val plan = SleepTimerPlan.create(minutes, System.currentTimeMillis(), SystemClock.elapsedRealtime(), bootCount(), UUID.randomUUID().toString())
        val previous = active()
        save(plan)
        try {
            schedule(plan)
        } catch (error: Exception) {
            if (previous == null) prefs.edit().remove("token").commit() else save(previous)
            throw error
        }
        prefs.edit().putString("result", "").apply()
        Log.i(TAG, "Sleep timer set: $minutes min")
        "Sleep timer set: $minutes min"
    }

    fun restore() = synchronized(lock) {
        val plan = active() ?: return@synchronized
        if (remainingMs(plan) == 0L) {
            context.sendBroadcast(Intent(context, SleepTimerReceiver::class.java).setAction(EXPIRE).putExtra("token", plan.token))
        } else if (canSchedule()) {
            schedule(plan)
        } else {
            recordResult("Sleep timer paused: enable Alarms & reminders")
        }
    }

    /** Claims each expiry once and rejects old/cancelled alarms, including alarms racing a replacement. */
    internal fun claimExpiry(token: String?): Boolean = synchronized(lock) {
        val plan = active() ?: return@synchronized false
        if (plan.token != token) return@synchronized false
        if (remainingMs(plan) > 0L) {
            if (canSchedule()) schedule(plan)
            return@synchronized false
        }
        check(prefs.edit().remove("token").putString("result", "Timer expired; sending power-off").commit())
        true
    }

    internal fun recordResult(result: String) {
        prefs.edit().putString("result", result).commit()
        Log.i(TAG, result)
    }

    private fun save(plan: SleepTimerPlan) {
        check(prefs.edit().putString("token", plan.token).putLong("wall", plan.deadlineWallMs)
            .putLong("elapsed", plan.deadlineElapsedMs).putInt("boot", plan.bootCount).commit()) { "Could not persist sleep timer" }
    }

    private fun bootCount(): Int = if (Build.VERSION.SDK_INT >= 24) {
        Settings.Global.getInt(context.contentResolver, Settings.Global.BOOT_COUNT, -1)
    } else {
        // API 23 has no boot counter; use wall time so reboot restoration remains valid.
        -1
    }

    private fun schedule(plan: SleepTimerPlan) {
        alarms.setExactAndAllowWhileIdle(
            AlarmManager.ELAPSED_REALTIME_WAKEUP,
            SystemClock.elapsedRealtime() + remainingMs(plan),
            operation(plan.token),
        )
    }

    private fun operation(token: String): PendingIntent = PendingIntent.getBroadcast(
        context, 46,
        Intent(context, SleepTimerReceiver::class.java).setAction(EXPIRE).putExtra("token", token),
        PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
    )

    companion object {
        internal const val EXPIRE = "pl.screenreceiver.tv.SLEEP_TIMER_EXPIRE"
        private const val TAG = "SleepTimer"
        private val lock = Any()
    }
}
