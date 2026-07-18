package com.ketoanapk.hr.ui

import androidx.activity.compose.BackHandler
import androidx.activity.compose.LocalOnBackPressedDispatcherOwner
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.swipe
import com.ketoanapk.hr.data.Timesheet
import com.ketoanapk.hr.data.TimesheetDay
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

class EdgeBackGestureUiTest {
    @get:Rule
    val compose = createComposeRule()

    @Test
    fun rightEdgeSwipeInvokesHighestPriorityBackHandlerAndShowsPreviousContent() {
        val backCount = mutableIntStateOf(0)
        val screen = mutableStateOf("Màn chi tiết")
        compose.setContent {
            val dispatcher = LocalOnBackPressedDispatcherOwner.current!!.onBackPressedDispatcher
            MaterialTheme {
                EdgeBackContainer(onBack = dispatcher::onBackPressed) {
                    BackHandler {
                        backCount.intValue += 1
                        screen.value = "Màn trước"
                    }
                    Box(Modifier.fillMaxSize()) { Text(screen.value) }
                }
            }
        }

        compose.onNodeWithTag("app-edge-back-root").performTouchInput {
            swipe(
                start = Offset(width - 2f, centerY),
                end = Offset(width * 0.42f, centerY),
                durationMillis = 240,
            )
        }

        compose.waitUntil(timeoutMillis = 2_000) { backCount.intValue == 1 }
        compose.waitForIdle()
        assertEquals(1, backCount.intValue)
        compose.onNodeWithText("Màn trước").assertIsDisplayed()
    }

    @Test
    fun leftEdgeSwipeInvokesHighestPriorityBackHandlerAndShowsPreviousContent() {
        val backCount = mutableIntStateOf(0)
        val screen = mutableStateOf("Màn chi tiết")
        compose.setContent {
            val dispatcher = LocalOnBackPressedDispatcherOwner.current!!.onBackPressedDispatcher
            MaterialTheme {
                EdgeBackContainer(onBack = dispatcher::onBackPressed) {
                    BackHandler {
                        backCount.intValue += 1
                        screen.value = "Màn trước"
                    }
                    Box(Modifier.fillMaxSize()) { Text(screen.value) }
                }
            }
        }

        compose.onNodeWithTag("app-edge-back-root").performTouchInput {
            swipe(
                start = Offset(2f, centerY),
                end = Offset(width * 0.58f, centerY),
                durationMillis = 240,
            )
        }

        compose.waitUntil(timeoutMillis = 2_000) { backCount.intValue == 1 }
        compose.waitForIdle()
        assertEquals(1, backCount.intValue)
        compose.onNodeWithText("Màn trước").assertIsDisplayed()
    }

    @Test
    fun horizontalSwipeAwayFromLeftEdgeDoesNotNavigateBack() {
        val backCount = mutableIntStateOf(0)
        showScreen { backCount.intValue += 1 }

        compose.onNodeWithTag("app-edge-back-root").performTouchInput {
            swipe(
                start = Offset(width * 0.30f, centerY),
                end = Offset(width * 0.80f, centerY),
                durationMillis = 240,
            )
        }
        compose.waitForIdle()

        assertEquals(0, backCount.intValue)
    }

    @Test
    fun horizontalSwipeAwayFromRightEdgeDoesNotNavigateBack() {
        val backCount = mutableIntStateOf(0)
        showScreen { backCount.intValue += 1 }

        compose.onNodeWithTag("app-edge-back-root").performTouchInput {
            swipe(
                start = Offset(width * 0.70f, centerY),
                end = Offset(width * 0.20f, centerY),
                durationMillis = 240,
            )
        }
        compose.waitForIdle()

        assertEquals(0, backCount.intValue)
    }

    @Test
    fun verticalGestureAtRightEdgeDoesNotNavigateBack() {
        val backCount = mutableIntStateOf(0)
        showScreen { backCount.intValue += 1 }

        compose.onNodeWithTag("app-edge-back-root").performTouchInput {
            swipe(
                start = Offset(width - 2f, height * 0.75f),
                end = Offset(width - 2f, height * 0.25f),
                durationMillis = 240,
            )
        }
        compose.waitForIdle()

        assertEquals(0, backCount.intValue)
    }

    @Test
    fun shortSlowEdgeDragSpringsBackWithoutNavigating() {
        val backCount = mutableIntStateOf(0)
        showScreen { backCount.intValue += 1 }

        compose.onNodeWithTag("app-edge-back-root").performTouchInput {
            swipe(
                start = Offset(width - 2f, centerY),
                end = Offset(width - 72f, centerY),
                durationMillis = 600,
            )
        }
        compose.waitForIdle()

        assertEquals(0, backCount.intValue)
        compose.onNodeWithText("Nội dung").assertIsDisplayed()
    }

    @Test
    fun edgeBackGestureWinsOverCalendarMonthSwipe() {
        val backCount = mutableIntStateOf(0)
        val monthOffset = mutableIntStateOf(0)
        compose.setContent {
            MaterialTheme {
                EdgeBackContainer(onBack = { backCount.intValue += 1 }) {
                    TimesheetScreen(
                        state = TimesheetUiState(
                            month = "2026-07",
                            timesheet = Timesheet(
                                period = "2026-07",
                                days = listOf(TimesheetDay(date = "2026-07-01", workedHours = 8.0)),
                            ),
                        ),
                        onMonthOffset = { monthOffset.intValue = it },
                        onSelectMonth = {},
                        onShiftSwap = {},
                    )
                }
            }
        }

        compose.onNodeWithTag("app-edge-back-root").performTouchInput {
            swipe(
                start = Offset(width - 2f, centerY),
                end = Offset(width * 0.42f, centerY),
                durationMillis = 240,
            )
        }

        compose.waitUntil(timeoutMillis = 2_000) { backCount.intValue == 1 }
        assertEquals(1, backCount.intValue)
        assertEquals(0, monthOffset.intValue)
    }

    private fun showScreen(onBack: () -> Unit) {
        compose.setContent {
            MaterialTheme {
                EdgeBackContainer(onBack = onBack) {
                    Box(Modifier.fillMaxSize()) { Text("Nội dung") }
                }
            }
        }
    }
}
