package com.ketoanapk.hr.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.test.isDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.swipeRight
import androidx.compose.ui.test.swipeUp
import com.ketoanapk.hr.data.Timesheet
import com.ketoanapk.hr.data.TimesheetDay
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import java.time.YearMonth

class TimesheetSwipeUiTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun swipeRightMovesToPreviousMonthAfterSlideAnimation() {
        val requestedOffset = mutableIntStateOf(0)
        showTimesheet { requestedOffset.intValue = it }

        compose.onNodeWithTag("timesheet-calendar").performTouchInput {
            swipeRight(durationMillis = 240)
        }

        compose.waitUntil(timeoutMillis = 2_000) { requestedOffset.intValue == -1 }
        compose.waitForIdle()
        assertEquals(-1, requestedOffset.intValue)
        val monthLabels = compose.onAllNodesWithText("Tháng 06/2026")
        val visibleMonth = monthLabels.fetchSemanticsNodes().indices.any { monthLabels[it].isDisplayed() }
        assertTrue("Tháng mới phải hiển thị sau khi animation kết thúc", visibleMonth)
    }

    @Test
    fun verticalScrollDoesNotAccidentallyChangeMonth() {
        val requestedOffset = mutableIntStateOf(0)
        showTimesheet { requestedOffset.intValue = it }

        compose.onNodeWithTag("timesheet-calendar").performTouchInput {
            swipeUp(durationMillis = 240)
        }
        compose.waitForIdle()

        assertEquals(0, requestedOffset.intValue)
    }

    private fun showTimesheet(onMonthOffset: (Int) -> Unit) {
        val displayedMonth = mutableStateOf("2026-07")
        compose.setContent {
            val period = displayedMonth.value
            MaterialTheme {
                TimesheetScreen(
                    state = TimesheetUiState(
                        month = period,
                        timesheet = Timesheet(
                            period = period,
                            days = listOf(TimesheetDay(date = "$period-01", workedHours = 8.0)),
                        ),
                    ),
                    onMonthOffset = { offset ->
                        onMonthOffset(offset)
                        displayedMonth.value = YearMonth.parse(period).plusMonths(offset.toLong()).toString()
                    },
                    onSelectMonth = {},
                    onShiftSwap = {},
                )
            }
        }
    }
}
