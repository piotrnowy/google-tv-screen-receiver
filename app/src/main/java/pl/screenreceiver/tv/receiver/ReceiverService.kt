package pl.screenreceiver.tv.receiver

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.launch
import pl.screenreceiver.tv.MainActivity
import pl.screenreceiver.tv.R
import pl.screenreceiver.tv.network.ReceiverAdvertisement
import pl.screenreceiver.tv.overlay.ProjectorOverlayBridge

class ReceiverService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val runtime by lazy { ReceiverRuntime.get(this) }
    private val advertisement by lazy { ReceiverAdvertisement(this) }

    override fun onCreate() {
        super.onCreate()
        val notifications = getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26) {
            notifications.createNotificationChannel(NotificationChannel(CHANNEL, "Screen receiver", NotificationManager.IMPORTANCE_LOW))
        }
        startForeground(NOTIFICATION_ID, notification("Starting receiver"))
        scope.launch {
            combine(runtime.listening, runtime.connected, runtime.uiState) { listening, connected, state ->
                Triple(listening, connected, state.deviceName to state.signalingPort)
            }.distinctUntilChanged().collect { (listening, connected, identity) ->
                advertisement.update(listening, identity.first, identity.second, connected)
            }
        }
        scope.launch {
            combine(runtime.listening, runtime.connected, runtime.uiState) { listening, connected, state ->
                streamingStatus(listening, connected, state.errorMessage)
            }.distinctUntilChanged().collect { status ->
                Log.i("ScreenReceiver", "Streaming status: ${status.name}")
                if (status == StreamingStatus.CONNECTED) ProjectorOverlayBridge.close?.invoke()
                if (Build.VERSION.SDK_INT < 33 || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) {
                    notifications.notify(NOTIFICATION_ID, notification("Streaming: ${status.name}"))
                }
            }
        }
        scope.launch {
            runtime.videoOutput.filterNotNull().distinctUntilChanged().collect {
                ProjectorOverlayBridge.showStream?.invoke()
            }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == STOP) {
            stopSelf()
            return START_NOT_STICKY
        }
        if (!hasLanPermission(this)) {
            runtime.localNetworkPermissionDenied()
            stopSelf()
            return START_NOT_STICKY
        }
        runtime.start()
        return START_STICKY
    }

    private fun notification(status: String): Notification {
        val open = PendingIntent.getActivity(this, 0,
            Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val stop = PendingIntent.getService(this, 1, Intent(this, ReceiverService::class.java).setAction(STOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val builder = if (Build.VERSION.SDK_INT >= 26) Notification.Builder(this, CHANNEL) else Notification.Builder(this)
        return builder.setSmallIcon(R.drawable.ic_receiver_notification).setContentTitle("Projector Control · Streaming")
            .setContentText(status).setContentIntent(open).setOngoing(true)
            .addAction(Notification.Action.Builder(null, "Stop receiver", stop).build()).build()
    }

    override fun onDestroy() {
        advertisement.close()
        scope.cancel()
        runtime.release()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    companion object {
        private const val CHANNEL = "screen_receiver"
        private const val NOTIFICATION_ID = 53530
        private const val STOP = "pl.screenreceiver.tv.STOP_RECEIVER"

        fun hasLanPermission(context: Context) = Build.VERSION.SDK_INT < 37 ||
            ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_LOCAL_NETWORK) == PackageManager.PERMISSION_GRANTED

        fun start(context: Context): Boolean {
            if (!hasLanPermission(context)) {
                ReceiverRuntime.get(context).localNetworkPermissionDenied()
                return false
            }
            return try {
                ContextCompat.startForegroundService(context, Intent(context, ReceiverService::class.java))
                true
            } catch (error: RuntimeException) {
                Log.e("ScreenReceiver", "Could not start receiver service", error)
                ReceiverRuntime.get(context).reportFailure("Receiver could not start: ${error.message}")
                false
            }
        }
    }
}
