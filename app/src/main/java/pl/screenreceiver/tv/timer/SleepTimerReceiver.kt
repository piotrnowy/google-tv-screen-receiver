package pl.screenreceiver.tv.timer

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import pl.screenreceiver.tv.projector.ProjectorControllers

class SleepTimerReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val pending = goAsync()
        CoroutineScope(Dispatchers.IO).launch {
            val timer = SleepTimerManager(context)
            try {
                if (intent.action == SleepTimerManager.EXPIRE) {
                    if (timer.claimExpiry(intent.getStringExtra("token"))) {
                        val result = ProjectorControllers.create(context).powerOff()
                        timer.recordResult("Sleep timer expired: ${result.message}")
                    }
                } else {
                    timer.restore()
                }
            } catch (error: Exception) {
                timer.recordResult("Sleep timer failed: ${error.message}")
            } finally {
                pending.finish()
            }
        }
    }
}
