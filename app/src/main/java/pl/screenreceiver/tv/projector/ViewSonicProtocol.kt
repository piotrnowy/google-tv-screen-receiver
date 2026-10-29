package pl.screenreceiver.tv.projector

/** ViewSonic RS-232/LAN specification V1.5, pp. 16-17; model support must be verified. */
object ViewSonicProtocol {
    val lightLevels = listOf(20, 40, 60, 80, 100)

    fun lightMode(percent: Int): Int {
        require(percent in lightLevels) { "Light source must be 20, 40, 60, 80 or 100" }
        return lightLevels.indexOf(percent) + 4
    }

    fun lightSource(percent: Int) = write(0x10, lightMode(percent))
    fun powerOff() = write(0x01, 0x00)
    fun readLightMode() = packet(0x07, 0x14, 0, 5, 0, 0x34, 0, 0, 0x11, 0x10)
    private fun write(function: Int, value: Int) = packet(0x06, 0x14, 0, 4, 0, 0x34, 0x11, function, value)

    private fun packet(vararg bytes: Int): ByteArray =
        (bytes.toList() + (bytes.drop(1).sum() and 0xff)).map(Int::toByte).toByteArray()

    fun acknowledged(response: ByteArray) = response.contentEquals(byteArrayOf(3, 0x14, 0, 0, 0, 0x14))

    fun readMode(response: ByteArray): Int? {
        if (response.size != 9 || response[0] != 5.toByte() || response[1] != 0x14.toByte() ||
            response[2] != 0.toByte() || response[3] != 3.toByte() || response[4] != 0.toByte() ||
            response[5] != 0.toByte() || response[6] != 0.toByte()
        ) return null
        val checksum = response.drop(1).dropLast(1).sumOf { it.toInt() and 0xff } and 0xff
        return if (checksum == (response.last().toInt() and 0xff)) response[7].toInt() and 0xff else null
    }
}
