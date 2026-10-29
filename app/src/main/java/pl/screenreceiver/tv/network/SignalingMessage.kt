package pl.screenreceiver.tv.network

import org.json.JSONObject

sealed interface SignalingMessage {
    data class Hello(val deviceName: String) : SignalingMessage
    data class Offer(val sdp: String) : SignalingMessage
    data class Answer(val sdp: String) : SignalingMessage
    data class Ice(val sdpMid: String?, val sdpMLineIndex: Int, val candidate: String) : SignalingMessage
    data object Bye : SignalingMessage
    data class Volume(val value: Double) : SignalingMessage
    data class Mute(val value: Boolean) : SignalingMessage
    data class VolumeAction(val action: String) : SignalingMessage
    data object GetVolume : SignalingMessage
    data class VolumeState(val current: Int, val max: Int, val muted: Boolean, val available: Boolean = true) : SignalingMessage
}

/** One UTF-8 JSON object per line. Invalid/unknown messages are ignored by the server. */
object SignalingCodec {
    const val MAX_LINE_CHARS = 1_048_576

    fun parse(line: String): SignalingMessage? {
        if (line.length > MAX_LINE_CHARS) return null
        return try {
            val tokener = org.json.JSONTokener(line)
            val json = tokener.nextValue() as? JSONObject ?: return null
            if (tokener.nextClean() != '\u0000') return null
            when (json.requiredString("type")) {
                "hello" -> SignalingMessage.Hello(json.requiredString("deviceName").take(256))
                "offer" -> SignalingMessage.Offer(json.requiredString("sdp"))
                "answer" -> SignalingMessage.Answer(json.requiredString("sdp"))
                "ice" -> {
                    val mid = when (val value = json.opt("sdpMid")) {
                        null, JSONObject.NULL -> null
                        is String -> value.takeIf { it.isNotBlank() } ?: return null
                        else -> return null
                    }
                    val index = json.get("sdpMLineIndex")
                    if (index !is Int || index < 0) return null
                    SignalingMessage.Ice(mid, index, json.requiredString("candidate"))
                }
                "bye" -> SignalingMessage.Bye
                "volume" -> {
                    if (json.has("action")) {
                        val action = json.get("action") as? String ?: return null
                        if (action !in setOf("up", "down", "muteToggle")) return null
                        return SignalingMessage.VolumeAction(action)
                    }
                    val value = (json.get("value") as? Number)?.toDouble() ?: return null
                    if (!value.isFinite() || value !in 0.0..1.0) return null
                    SignalingMessage.Volume(value)
                }
                "getVolume" -> SignalingMessage.GetVolume
                "volumeState" -> {
                    val current = json.get("current") as? Int ?: return null
                    val max = json.get("max") as? Int ?: return null
                    if (current < 0 || max < current) return null
                    val muted = json.get("muted") as? Boolean ?: return null
                    val available = if (json.has("available")) json.get("available") as? Boolean ?: return null else true
                    SignalingMessage.VolumeState(current, max, muted, available)
                }
                "mute" -> SignalingMessage.Mute(json.get("value") as? Boolean ?: return null)
                else -> null
            }
        } catch (_: org.json.JSONException) {
            null
        } catch (_: IllegalArgumentException) {
            null
        }
    }

    fun encode(message: SignalingMessage): String = JSONObject().apply {
        when (message) {
            is SignalingMessage.Hello -> put("type", "hello").put("deviceName", message.deviceName)
            is SignalingMessage.Offer -> put("type", "offer").put("sdp", message.sdp)
            is SignalingMessage.Answer -> put("type", "answer").put("sdp", message.sdp)
            is SignalingMessage.Ice -> put("type", "ice")
                .put("sdpMid", message.sdpMid ?: JSONObject.NULL)
                .put("sdpMLineIndex", message.sdpMLineIndex).put("candidate", message.candidate)
            SignalingMessage.Bye -> put("type", "bye")
            is SignalingMessage.Volume -> put("type", "volume").put("value", message.value)
            is SignalingMessage.Mute -> put("type", "mute").put("value", message.value)
            is SignalingMessage.VolumeAction -> {
                require(message.action in setOf("up", "down", "muteToggle")) { "Unsupported volume action" }
                put("type", "volume").put("action", message.action)
            }
            SignalingMessage.GetVolume -> put("type", "getVolume")
            is SignalingMessage.VolumeState -> put("type", "volumeState").put("current", message.current).put("max", message.max)
                .put("muted", message.muted).put("available", message.available)
        }
    }.toString()

    private fun JSONObject.requiredString(key: String): String =
        (get(key) as? String)?.takeIf { it.isNotBlank() }
            ?: throw IllegalArgumentException("Missing or invalid $key")
}
