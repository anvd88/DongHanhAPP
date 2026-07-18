package com.ketoanapk.hr.ui

import com.ketoanapk.hr.data.TimesheetDay
import org.junit.Assert.assertEquals
import org.junit.Test

class ScheduleTest {
    @Test fun approvedScheduleEventsHaveDistinctCalendarTones() {
        assertEquals(TimesheetCalendarTone.Leave, timesheetCalendarTone(TimesheetDay(eventType = "leave")))
        assertEquals(TimesheetCalendarTone.Business, timesheetCalendarTone(TimesheetDay(eventType = "business_trip")))
        assertEquals(TimesheetCalendarTone.Overtime, timesheetCalendarTone(TimesheetDay(eventType = "overtime")))
        assertEquals(TimesheetCalendarTone.Worked, timesheetCalendarTone(TimesheetDay(workedHours = 8.0)))
    }

    @Test fun explicitEventTypeWinsOverLocalizedStatus() {
        val day = TimesheetDay(eventType = "business_trip", status = "Vắng")
        assertEquals("Công tác", timesheetCalendarLabel(day))
    }
}
