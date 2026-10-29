package pl.screenreceiver.tv.overlay

/** Image dimming only. Percentages are software levels, never projector LED-power readback. */
object ScreenBrightnessPolicy {
    val levels = listOf(20, 40, 60, 80, 100)

    fun restoredLevel(saved: Int): Int = saved.takeIf { it in levels } ?: 100

    fun opacity(percent: Int): Float {
        require(percent in levels) { "Screen brightness must be 20, 40, 60, 80 or 100" }
        return (100 - percent) / 100f
    }
}
