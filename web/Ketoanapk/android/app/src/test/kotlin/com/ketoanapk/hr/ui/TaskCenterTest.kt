package com.ketoanapk.hr.ui

import com.ketoanapk.hr.data.ManagerHeadcount
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.Timesheet
import com.ketoanapk.hr.data.TimesheetDay
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant
import java.time.ZoneOffset

class TaskCenterTest {
    private val now = Instant.parse("2026-07-12T12:00:00Z")

    @Test
    fun groupsPendingApprovalsByTwentyFourHourSla() {
        val inbox = listOf(
            request("today", "2026-07-12T08:00:00Z"),
            request("soon", "2026-07-11T18:00:00Z"),
            request("late", "2026-07-10T08:00:00Z"),
            request("done", "2026-07-10T08:00:00Z", "Approved"),
        )

        val tasks = buildTaskCenterItems(inbox, null, null, now, ZoneOffset.UTC)

        assertEquals(TaskBucket.Today, tasks.first { it.entityId == "today" }.bucket)
        assertEquals(TaskBucket.DueSoon, tasks.first { it.entityId == "soon" }.bucket)
        assertEquals(TaskBucket.Overdue, tasks.first { it.entityId == "late" }.bucket)
        assertTrue(tasks.none { it.entityId == "done" })
    }

    @Test
    fun createsAttendanceAndContractTasksFromRealApiModels() {
        val timesheet = Timesheet(days = listOf(TimesheetDay(date = "2026-07-12", shiftName = "Ca hành chính")))
        val manager = ManagerHeadcount(expiringContracts = 3)

        val tasks = buildTaskCenterItems(emptyList(), timesheet, manager, now, ZoneOffset.UTC)

        assertEquals(HrDestination.Scan, tasks.first { it.kind == TaskKind.Attendance }.target)
        assertEquals(HrDestination.People, tasks.first { it.kind == TaskKind.ExpiringContract }.target)
    }

    private fun request(id: String, createdAt: String, status: String = "Pending") = RequestListItem(
        id = id,
        requestNo = id,
        typeLabel = "Xin nghỉ",
        employeeName = "Nhân viên",
        status = status,
        createdAt = createdAt,
    )
}
