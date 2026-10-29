package pl.screenreceiver.tv.receiver

import android.content.Context
import android.os.Build
import android.util.Log
import java.util.concurrent.atomic.AtomicReference
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import pl.screenreceiver.tv.model.ReceiverEvent
import pl.screenreceiver.tv.model.ReceiverState
import pl.screenreceiver.tv.network.LocalIpAddressProvider
import pl.screenreceiver.tv.network.ReceiverServer
import pl.screenreceiver.tv.network.SignalingMessage
import pl.screenreceiver.tv.network.StreamSession
import pl.screenreceiver.tv.audio.AndroidMediaVolumeController
import pl.screenreceiver.tv.audio.RemoteVolumeController
import pl.screenreceiver.tv.webrtc.VideoOutput
import pl.screenreceiver.tv.webrtc.WebRtcEngine
import pl.screenreceiver.tv.webrtc.WebRtcReceiver

class ReceiverRuntime private constructor(private val context: Context) {
    companion object {
        private const val TAG = "ScreenReceiver"
        @Volatile private var instance: ReceiverRuntime? = null
        fun get(context: Context): ReceiverRuntime = instance ?: synchronized(this) {
            instance ?: ReceiverRuntime(context.applicationContext).also { instance = it }
        }
    }

    private val localIpAddressProvider = LocalIpAddressProvider()
    private val remoteVolume = RemoteVolumeController(AndroidMediaVolumeController(context)) { Log.i("Volume", it) }
    private val state = MutableStateFlow(
        ReceiverState(deviceName = Build.MODEL.takeIf { it.isNotBlank() } ?: "Google TV"),
    )
    val uiState = state.asStateFlow()
    private val video = MutableStateFlow<VideoOutput?>(null)
    val videoOutput = video.asStateFlow()
    private val mutableListening = MutableStateFlow(false)
    val listening = mutableListening.asStateFlow()
    private val mutableConnected = MutableStateFlow(false)
    val connected = mutableConnected.asStateFlow()

    // Process-owned runtime; the foreground service serializes native/socket ownership here.
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private enum class LifecycleCommand { START, STOP, PERMISSION_DENIED, CLOSE }
    private val lifecycleCommands = Channel<LifecycleCommand>(Channel.UNLIMITED)
    private var engine: WebRtcEngine? = null
    private var server: ReceiverServer? = null
    private var serverJob: Job? = null
    private val currentSession = AtomicReference<StreamSession?>(null)

    init {
        scope.launch {
            for (command in lifecycleCommands) {
                try {
                    when (command) {
                        LifecycleCommand.START -> startReceiver()
                        LifecycleCommand.STOP -> stopReceiver()
                        LifecycleCommand.PERMISSION_DENIED -> {
                            stopReceiver()
                            state.update {
                                it.reduce(ReceiverEvent.Failure(
                                    "Allow local network access in Settings > Apps > Screen Receiver > Permissions, then reopen the app.",
                                ))
                            }
                        }
                        LifecycleCommand.CLOSE -> {
                            stopReceiver()
                            engine?.release()
                            engine = null
                        }
                    }
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (error: Exception) {
                    Log.e(TAG, "Exception: receiver lifecycle command $command failed", error)
                    state.update { it.reduce(ReceiverEvent.Failure(error.message ?: "Receiver failed")) }
                }
            }
        }
    }

    fun start() {
        Log.d(TAG, "Receiver start requested")
        lifecycleCommands.trySend(LifecycleCommand.START)
    }

    fun stop() {
        Log.d(TAG, "Receiver stop requested")
        lifecycleCommands.trySend(LifecycleCommand.STOP)
    }

    fun localNetworkPermissionDenied() {
        lifecycleCommands.trySend(LifecycleCommand.PERMISSION_DENIED)
    }

    fun reportFailure(message: String) {
        state.update { it.reduce(ReceiverEvent.Failure(message)) }
    }

    private suspend fun startReceiver() {
        state.update { it.copy(localIpAddress = localIpAddressProvider.getLocalIpv4Address()) }
        if (serverJob?.isActive == true) return
        val rtcEngine = engine ?: WebRtcEngine.create(context).also { engine = it }
        val newServer = ReceiverServer(state.value.signalingPort)
        Log.d(TAG, "Starting signaling server on ${state.value.signalingPort}")
        server = newServer
        state.update { it.reduce(ReceiverEvent.Disconnected) }
        serverJob = scope.launch {
            var receiver: WebRtcReceiver? = null
            try {
                newServer.run(object : ReceiverServer.Listener {
                    override fun onListening(port: Int) {
                        mutableListening.value = true
                    }

                    override suspend fun onConnected(session: StreamSession) {
                        Log.d(TAG, "Client session started")
                        currentSession.set(session)
                        mutableConnected.value = true
                        state.update { it.reduce(ReceiverEvent.Connected) }
                        receiver = WebRtcReceiver(rtcEngine, session, object : WebRtcReceiver.Listener {
                            override fun onNegotiating() {
                                if (currentSession.get() === session) {
                                    state.update { it.reduce(ReceiverEvent.Negotiating) }
                                }
                            }

                            override fun onVideo(output: VideoOutput) {
                                if (currentSession.get() === session) {
                                    video.value = output
                                    state.update { it.reduce(ReceiverEvent.VideoReceived) }
                                }
                            }

                            override fun onError(message: String) {
                                Log.w(TAG, "WebRTC error: $message")
                                if (currentSession.get() === session) {
                                    video.value = null
                                    state.update { it.reduce(ReceiverEvent.Failure(message)) }
                                }
                            }
                        })
                    }

                    override suspend fun onMessage(session: StreamSession, message: SignalingMessage) {
                        when (message) {
                            is SignalingMessage.Hello -> state.update { it.reduce(ReceiverEvent.Hello(message.deviceName)) }
                            is SignalingMessage.VolumeAction -> {
                                val result = runCatching { remoteVolume.apply(message.action) }.getOrElse { error ->
                                    Log.e("Volume", "Could not apply media-volume action", error)
                                    remoteVolume.current().copy(available = false)
                                }
                                session.send(SignalingMessage.VolumeState(result.current, result.max, result.muted, result.available))
                                if (result.available) Log.i("Volume", "Applied ${message.action}; media=${result.current}/${result.max}, muted=${result.muted}")
                            }
                            SignalingMessage.GetVolume -> {
                                val result = remoteVolume.current()
                                session.send(SignalingMessage.VolumeState(result.current, result.max, result.muted, result.available))
                            }
                            is SignalingMessage.Volume, is SignalingMessage.Mute ->
                                Log.i(TAG, "Reserved media-volume command received; remote volume control is not enabled")
                            else -> receiver?.receive(message)
                        }
                    }

                    override suspend fun onDisconnected(session: StreamSession) {
                        Log.d(TAG, "Client session ended; cleaning up WebRTC")
                        currentSession.compareAndSet(session, null)
                        mutableConnected.value = false
                        receiver?.close()
                        receiver = null
                        video.value = null
                        state.update {
                            it.reduce(ReceiverEvent.Disconnected).copy(
                                localIpAddress = localIpAddressProvider.getLocalIpv4Address(),
                            )
                        }
                    }
                })
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (error: Exception) {
                Log.e(TAG, "Exception: signaling server failed", error)
                state.update { it.reduce(ReceiverEvent.Failure("Signaling server: ${error.message}")) }
            } finally {
                mutableListening.value = false
                mutableConnected.value = false
            }
        }
    }

    private suspend fun stopReceiver() {
        Log.d(TAG, "Stopping receiver and closing signaling sockets")
        // Close first: cancellation alone cannot interrupt blocking Java socket reads.
        server?.stop()
        serverJob?.join()
        serverJob = null
        server = null
        video.value = null
        mutableListening.value = false
        mutableConnected.value = false
        state.update { it.reduce(ReceiverEvent.Disconnected) }
    }

    fun release() {
        lifecycleCommands.trySend(LifecycleCommand.CLOSE)
    }
}
