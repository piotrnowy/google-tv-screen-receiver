package pl.screenreceiver.tv.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test

class SignalingMessageTest {
    @Test
    fun futureVolumeAndMuteCommandsRoundTrip() {
        val messages = listOf(SignalingMessage.Volume(0.0), SignalingMessage.Volume(0.5), SignalingMessage.Volume(1.0),
            SignalingMessage.Mute(true), SignalingMessage.Mute(false))
        messages.forEach { assertEquals(it, SignalingCodec.parse(SignalingCodec.encode(it))) }
        assertEquals(SignalingMessage.Volume(0.5), SignalingCodec.parse("""{"type":"volume","value":0.5}"""))
        assertEquals(SignalingMessage.Mute(true), SignalingCodec.parse("""{"type":"mute","value":true}"""))
    }

    @Test
    fun invalidFutureVolumeAndMuteCommandsAreIgnored() {
        listOf(
            """{"type":"volume"}""", """{"type":"volume","value":-0.1}""", """{"type":"volume","value":1.1}""",
            """{"type":"volume","value":"0.5"}""", """{"type":"mute","value":"true"}""", """{"type":"mute","value":1}""",
        ).forEach { assertNull(SignalingCodec.parse(it)) }
    }

    @Test
    fun relativeVolumeActionsAndStateRoundTrip() {
        listOf("up", "down", "muteToggle").forEach { action ->
            val message = SignalingMessage.VolumeAction(action)
            assertEquals(message, SignalingCodec.parse(SignalingCodec.encode(message)))
            assertEquals(message, SignalingCodec.parse("""{"type":"volume","action":"$action"}"""))
        }
        assertEquals(SignalingMessage.GetVolume, SignalingCodec.parse("""{"type":"getVolume"}"""))
        val state = SignalingMessage.VolumeState(8, 15, false)
        assertEquals(state, SignalingCodec.parse(SignalingCodec.encode(state)))
        assertEquals(SignalingMessage.VolumeState(0, 0, true, false),
            SignalingCodec.parse("""{"type":"volumeState","current":0,"max":0,"muted":true,"available":false}"""))
        assertNull(SignalingCodec.parse("""{"type":"volume","action":"unsupported"}"""))
        assertNull(SignalingCodec.parse("""{"type":"volume","action":"unsupported","value":0.5}"""))
    }

    @Test
    fun parsesAllWireMessages() {
        assertEquals(SignalingMessage.Hello("Example iPhone"),
            SignalingCodec.parse("""{"type":"hello","deviceName":"Example iPhone"}"""))
        assertEquals(SignalingMessage.Offer("v=0\r\n"),
            SignalingCodec.parse("""{"type":"offer","sdp":"v=0\r\n"}"""))
        assertEquals(SignalingMessage.Answer("v=0\r\n"),
            SignalingCodec.parse("""{"type":"answer","sdp":"v=0\r\n"}"""))
        assertEquals(SignalingMessage.Ice("0", 0, "candidate:host"),
            SignalingCodec.parse("""{"type":"ice","sdpMid":"0","sdpMLineIndex":0,"candidate":"candidate:host"}"""))
        assertEquals(SignalingMessage.Bye, SignalingCodec.parse("""{"type":"bye"}"""))
    }

    @Test
    fun malformedUnknownMissingAndWronglyTypedFieldsAreRejected() {
        val invalid = listOf(
            "", "not JSON", "{", "[]", "null", "{}", "{\"type\":\"bye\"} garbage",
            """{"type":"unknown"}""", """{"type":2}""",
            """{"type":"hello"}""", """{"type":"hello","deviceName":true}""",
            """{"type":"offer","sdp":"  "}""", """{"type":"offer","sdp":123}""",
            """{"type":"answer","sdp":null}""",
            """{"type":"ice","sdpMLineIndex":-1,"candidate":"c"}""",
            """{"type":"ice","sdpMLineIndex":"0","candidate":"c"}""",
            """{"type":"ice","sdpMLineIndex":0.5,"candidate":"c"}""",
            """{"type":"ice","sdpMLineIndex":2147483648,"candidate":"c"}""",
            """{"type":"ice","sdpMid":42,"sdpMLineIndex":0,"candidate":"c"}""",
            """{"type":"ice","sdpMLineIndex":0}""",
        )
        invalid.forEach { assertNull("Should reject: $it", SignalingCodec.parse(it)) }
    }

    @Test
    fun acceptsNullableOrAbsentIceMid() {
        for (mid in listOf("", "\"sdpMid\":null,")) {
            assertEquals(SignalingMessage.Ice(null, 1, "c"),
                SignalingCodec.parse("""{"type":"ice",${mid}"sdpMLineIndex":1,"candidate":"c"}"""))
        }
    }

    @Test
    fun roundTripsUnicodeAndEscapedSdpWithoutBreakingLineFraming() {
        val messages = listOf(
            SignalingMessage.Hello("Example’s iPad 📱"),
            SignalingMessage.Offer("v=0\r\na=\"quoted\"\r\n"),
            SignalingMessage.Answer("v=0\r\n"),
            SignalingMessage.Ice(null, 0, "candidate:1 1 udp 123 192.168.1.2 9000 typ host"),
            SignalingMessage.Bye,
        )
        messages.forEach {
            val encoded = SignalingCodec.encode(it)
            assertFalse(encoded.contains('\n'))
            assertEquals(it, SignalingCodec.parse(encoded))
        }
    }

    @Test
    fun rejectsOversizedMessages() {
        assertNull(SignalingCodec.parse(" ".repeat(SignalingCodec.MAX_LINE_CHARS + 1)))
    }
}
