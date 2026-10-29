package pl.screenreceiver.tv.webrtc

import android.util.Log
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.channels.ReceiveChannel
import kotlinx.coroutines.launch
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.Job
import org.webrtc.DataChannel
import org.webrtc.AudioTrack
import org.webrtc.IceCandidate
import org.webrtc.MediaConstraints
import org.webrtc.MediaStream
import org.webrtc.MediaStreamTrack
import org.webrtc.PeerConnection
import org.webrtc.RtpReceiver
import org.webrtc.RtpTransceiver
import org.webrtc.SdpObserver
import org.webrtc.SessionDescription
import org.webrtc.VideoTrack
import pl.screenreceiver.tv.network.SignalingMessage
import pl.screenreceiver.tv.network.StreamSession

/** All peer mutations and native callbacks are serialized by a bounded IO command queue. */
class WebRtcReceiver(
    private val engine: WebRtcEngine,
    private val session: StreamSession,
    private val listener: Listener,
    private val iceServers: List<PeerConnection.IceServer> = emptyList(),
) {
    companion object {
        private const val TAG = "WebRtcReceiver"
    }

    interface Listener {
        fun onNegotiating()
        fun onVideo(output: VideoOutput)
        fun onError(message: String)
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val commands = Channel<suspend () -> Unit>(128)
    private val closing = AtomicBoolean(false)
    private val output = VideoOutput(engine)
    private var peer: PeerConnection? = null
    private var videoTrack: VideoTrack? = null
    private var audioTrack: AudioTrack? = null
    private var offered = false
    private var remoteDescriptionSet = false
    private val pendingIce = ArrayList<IceCandidate>()
    private var audioDiagnostics: Job? = null

    private val worker = scope.launch {
        try {
            val configuration = PeerConnection.RTCConfiguration(iceServers).apply {
                sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN
                // Empty iceServers means host candidates only: no public STUN or TURN.
                continualGatheringPolicy = PeerConnection.ContinualGatheringPolicy.GATHER_CONTINUALLY
            }
            Log.d(TAG, "Creating PeerConnection")
            peer = checkNotNull(engine.factory.createPeerConnection(configuration, observer)) {
                "Could not create WebRTC PeerConnection"
            }.apply {
                setAudioPlayout(true)
                setAudioRecording(false)
            }
            processWebRtcCommands(commands) { error ->
                Log.e(TAG, "Exception: WebRTC command failed; continuing signaling session", error)
                listener.onError(error.message ?: "WebRTC command failed")
            }
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (error: Exception) {
            Log.e(TAG, "Exception: WebRTC session failed", error)
            listener.onError(error.message ?: "WebRTC session failed")
        } finally {
            closing.set(true)
            commands.close()
            output.clear()
            videoTrack?.removeSink(output)
            videoTrack = null
            audioTrack?.setEnabled(false)
            audioTrack = null
            peer?.dispose()
            peer = null
            pendingIce.clear()
        }
    }

    suspend fun receive(message: SignalingMessage) {
        if (closing.get()) return
        try {
            commands.send {
                when (message) {
                    is SignalingMessage.Offer -> acceptOffer(message.sdp)
                    is SignalingMessage.Ice -> {
                        Log.d(TAG, "ICE candidate received")
                        val candidate = IceCandidate(message.sdpMid, message.sdpMLineIndex, message.candidate)
                        if (remoteDescriptionSet) addIce(candidate) else {
                            check(pendingIce.size < 256) { "Too many pending ICE candidates" }
                            pendingIce.add(candidate)
                            Log.d(TAG, "ICE candidate queued until remote offer is accepted (${pendingIce.size})")
                        }
                    }
                    // The TV is always the answerer. Hello/bye are handled by signaling.
                    is SignalingMessage.Answer -> error("Receiver expects an offer, not an answer")
                    else -> Unit
                }
            }
        } catch (error: kotlinx.coroutines.channels.ClosedSendChannelException) {
            Log.e(TAG, "Exception: message arrived after WebRTC command queue closed", error)
        }
    }

    suspend fun close() {
        Log.d(TAG, "Closing PeerConnection after signaling session ended")
        closing.set(true)
        commands.close()
        worker.cancelAndJoin()
        scope.cancel()
    }

    private fun post(command: suspend () -> Unit) {
        if (closing.get()) return
        if (commands.trySend(command).isFailure && !closing.get()) {
            val error = IllegalStateException("WebRTC callback queue overflow")
            Log.e(TAG, "Exception: could not enqueue WebRTC callback", error)
            scope.launch {
                listener.onError(error.message!!)
            }
        }
    }

    private fun acceptOffer(sdp: String) {
        check(!offered) { "Only one offer is supported per TCP connection; reconnect to renegotiate" }
        offered = true
        listener.onNegotiating()
        Log.d(TAG, "Setting remote offer")
        peer!!.setRemoteDescription(sdpObserver(onSet = {
            remoteDescriptionSet = true
            Log.d(TAG, "Remote offer accepted")
            // Receive both media types; the TV never creates a microphone/source track.
            peer!!.transceivers.forEach { transceiver ->
                val track = transceiver.receiver.track()
                // Determine the media type even when a receiver track is not yet exposed.
                // Video handling stays unchanged; an audio transceiver must not become inactive
                // merely because its asynchronous onTrack callback has not run yet.
                transceiver.direction = if (track is VideoTrack || track is AudioTrack ||
                    transceiver.mediaType == MediaStreamTrack.MediaType.MEDIA_TYPE_AUDIO) {
                    RtpTransceiver.RtpTransceiverDirection.RECV_ONLY
                } else {
                    RtpTransceiver.RtpTransceiverDirection.INACTIVE
                }
                Log.i(TAG, "Transceiver ${transceiver.mediaType}: mid=${transceiver.mid}, direction=${transceiver.direction}, current=${transceiver.currentDirection}")
            }
            pendingIce.forEach(::addIce)
            pendingIce.clear()
            Log.d(TAG, "Creating answer")
            peer!!.createAnswer(sdpObserver(onCreate = { answer ->
                peer!!.setLocalDescription(sdpObserver(onSet = {
                    Log.d(TAG, "Local answer set")
                    peer!!.transceivers.forEach { transceiver ->
                        Log.i(TAG, "Negotiated transceiver ${transceiver.mediaType}: mid=${transceiver.mid}, direction=${transceiver.direction}, current=${transceiver.currentDirection}")
                    }
                    session.send(SignalingMessage.Answer(answer.description))
                }), answer)
            }), MediaConstraints())
        }), SessionDescription(SessionDescription.Type.OFFER, sdp))
    }

    private fun addIce(candidate: IceCandidate) {
        try {
            check(peer!!.addIceCandidate(candidate)) { "Remote ICE candidate was rejected" }
        } catch (error: Exception) {
            // One unusable candidate must not abort draining early ICE or creating the answer.
            Log.e(TAG, "Exception: remote ICE candidate rejected; ignoring candidate", error)
        }
    }

    private fun attachTrack(track: VideoTrack?) {
        if (track == null || track === videoTrack) return
        videoTrack?.removeSink(output)
        videoTrack = track
        track.addSink(output)
        listener.onVideo(output)
    }

    private fun attachRemoteTrack(track: MediaStreamTrack?) {
        when (track) {
            is VideoTrack -> attachTrack(track)
            is AudioTrack -> {
                // onTrack/onAddTrack may expose different Java wrappers for the same native track.
                if (track.id() == audioTrack?.id()) return
                Log.i(TAG, "Remote AUDIO track received")
                audioTrack?.setEnabled(false)
                audioTrack = track
                track.setEnabled(true)
                check(track.enabled()) { "Remote audio track is disabled after enabling it" }
                Log.i(TAG, "Audio track enabled=${track.enabled()}")
                Log.i(TAG, "Audio track state=${track.state()}")
                startAudioDiagnostics()
            }
        }
    }

    private fun startAudioDiagnostics() {
        if (audioDiagnostics != null) return
        audioDiagnostics = scope.launch {
            while (isActive && !closing.get()) {
                delay(1000)
                post {
                    peer?.getStats { report ->
                        report.statsMap.values.filter { stat ->
                            stat.type == "inbound-rtp" && (stat.members["kind"] == "audio" || stat.members["mediaType"] == "audio")
                        }.forEach { stat ->
                            Log.i(TAG, "Audio RTP received: packets=${stat.members["packetsReceived"]}, bytes=${stat.members["bytesReceived"]}, audioLevel=${stat.members["audioLevel"]}, totalAudioEnergy=${stat.members["totalAudioEnergy"]}")
                        }
                    }
                }
            }
        }
    }

    private fun sdpObserver(
        onSet: suspend () -> Unit = {},
        onCreate: suspend (SessionDescription) -> Unit = {},
    ): SdpObserver = object : SdpObserver {
        override fun onCreateSuccess(sdp: SessionDescription) = post { onCreate(sdp) }
        override fun onSetSuccess() = post { onSet() }
        override fun onCreateFailure(error: String) = post { error("Create SDP failed: $error") }
        override fun onSetFailure(error: String) = post { error("Set SDP failed: $error") }
    }

    private val observer: PeerConnection.Observer
        get() = object : PeerConnection.Observer {
            override fun onSignalingChange(state: PeerConnection.SignalingState) {
                Log.d(TAG, "Signaling state: $state")
            }
            override fun onIceConnectionChange(state: PeerConnection.IceConnectionState) {
                Log.d(TAG, "ICE state: $state")
                // ICE DISCONNECTED can recover. TCP owns the session lifetime.
                if (state == PeerConnection.IceConnectionState.FAILED) post {
                    error("WebRTC ICE connection failed")
                }
            }
            override fun onConnectionChange(state: PeerConnection.PeerConnectionState) {
                Log.d(TAG, "PeerConnection state: $state")
                if (state == PeerConnection.PeerConnectionState.FAILED) post {
                    error("WebRTC connection failed")
                }
            }
            override fun onIceConnectionReceivingChange(receiving: Boolean) = Unit
            override fun onIceGatheringChange(state: PeerConnection.IceGatheringState) {
                Log.d(TAG, "ICE gathering state: $state")
            }
            override fun onIceCandidate(candidate: IceCandidate) = post {
                Log.d(TAG, "ICE candidate generated")
                session.send(SignalingMessage.Ice(candidate.sdpMid, candidate.sdpMLineIndex, candidate.sdp))
            }
            override fun onIceCandidatesRemoved(candidates: Array<out IceCandidate>) = Unit
            override fun onAddStream(stream: MediaStream) = Unit
            override fun onRemoveStream(stream: MediaStream) = Unit
            override fun onDataChannel(channel: DataChannel) = post { channel.dispose() }
            override fun onRenegotiationNeeded() = Unit
            override fun onAddTrack(receiver: RtpReceiver, streams: Array<out MediaStream>) = post {
                Log.i(TAG, "onAddTrack: kind=${receiver.track()?.kind()}")
                attachRemoteTrack(receiver.track())
            }
            override fun onTrack(transceiver: RtpTransceiver) = post {
                Log.i(TAG, "onTrack: media=${transceiver.mediaType}, mid=${transceiver.mid}, current=${transceiver.currentDirection}")
                attachRemoteTrack(transceiver.receiver.track())
            }
        }
}

/** A failed signaling command must not dispose the peer or discard later SDP/ICE callbacks. */
internal suspend fun processWebRtcCommands(
    commands: ReceiveChannel<suspend () -> Unit>,
    onError: (Exception) -> Unit,
) {
    for (command in commands) {
        try {
            command()
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (error: Exception) {
            onError(error)
        }
    }
}
