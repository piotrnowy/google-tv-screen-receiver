package pl.screenreceiver.tv.viewmodel

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import pl.screenreceiver.tv.receiver.ReceiverRuntime
import pl.screenreceiver.tv.receiver.ReceiverService

/** Activity facade: leaving the Activity no longer tears down a background receiver. */
class ReceiverViewModel(application: Application) : AndroidViewModel(application) {
    private val runtime = ReceiverRuntime.get(application)
    val uiState = runtime.uiState
    val videoOutput = runtime.videoOutput

    fun start() { ReceiverService.start(getApplication()) }
    fun localNetworkPermissionDenied() { runtime.localNetworkPermissionDenied() }
}
