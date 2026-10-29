package pl.screenreceiver.tv.timer

/** Monotonic within a boot; the persisted wall deadline restores the timer after reboot. */
data class SleepTimerPlan(
    val token: String,
    val deadlineWallMs: Long,
    val deadlineElapsedMs: Long,
    val bootCount: Int,
) {
    fun remainingMs(wallMs: Long, elapsedMs: Long, currentBoot: Int): Long =
        ((if (bootCount >= 0 && bootCount == currentBoot) deadlineElapsedMs - elapsedMs else deadlineWallMs - wallMs)).coerceAtLeast(0)

    companion object {
        val options = listOf(0, 15, 30, 45, 60, 90, 120)

        fun create(minutes: Int, wallMs: Long, elapsedMs: Long, boot: Int, token: String): SleepTimerPlan {
            require(minutes in options && minutes > 0)
            val duration = minutes * 60_000L
            return SleepTimerPlan(token, wallMs + duration, elapsedMs + duration, boot)
        }
    }
}
