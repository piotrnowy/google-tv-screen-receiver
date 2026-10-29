package pl.screenreceiver.tv.timer

import org.junit.Assert.*
import org.junit.Test

class SleepTimerPlanTest {
    @Test fun allOptionsCalculateMinutesWithoutIntegerOverflow() {
        SleepTimerPlan.options.filter { it > 0 }.forEach { minutes ->
            val plan = SleepTimerPlan.create(minutes, 1_000_000, 5000, 2, "timer")
            assertEquals(minutes * 60_000L, plan.remainingMs(1_000_000, 5000, 2))
        }
    }

    @Test fun sameBootUsesElapsedTimeDespiteWallClockChanges() {
        val plan = SleepTimerPlan.create(15, 1_000_000, 5000, 2, "timer")
        assertEquals(840_000L, plan.remainingMs(99_000_000, 65_000, 2))
        assertEquals(0L, plan.remainingMs(1, 905_001, 2))
    }

    @Test fun rebootRestoresRemainingWallTimeAndExpiredPlansStayExpired() {
        val plan = SleepTimerPlan.create(15, 1_000_000, 5000, 2, "timer")
        assertEquals(600_000L, plan.remainingMs(1_300_000, 100, 3))
        assertEquals(0L, plan.remainingMs(2_000_000, 100, 3))
    }

    @Test fun devicesWithoutBootCounterUsePersistedWallDeadline() {
        val plan = SleepTimerPlan.create(15, 1_000_000, 5000, -1, "timer")
        assertEquals(600_000L, plan.remainingMs(1_300_000, 100, -1))
    }

    @Test fun offAndUnsupportedIntervalsCannotCreateAnActivePlan() {
        listOf(0, -1, 1, 180).forEach { minutes ->
            assertThrows(IllegalArgumentException::class.java) { SleepTimerPlan.create(minutes, 0, 0, 0, "timer") }
        }
    }
}
