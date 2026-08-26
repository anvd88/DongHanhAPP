package com.ketoanapk.hr.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class HomeActionsOrderTest {
    @Test
    fun savedItemsMoveFirstAndDefaultsKeepTheirRelativeOrder() {
        val actions = listOf(
            HrDestination.Scan,
            HrDestination.Requests,
            HrDestination.Tasks,
            HrDestination.Timesheet,
        )

        assertEquals(
            listOf(
                HrDestination.Timesheet,
                HrDestination.Requests,
                HrDestination.Scan,
                HrDestination.Tasks,
            ),
            orderHomeActions(actions, listOf("Timesheet", "Requests")),
        )
    }

    @Test
    fun unknownSavedDestinationIsIgnored() {
        val actions = listOf(HrDestination.Scan, HrDestination.Requests)
        assertEquals(actions, orderHomeActions(actions, listOf("RemovedFeature")))
    }
}
