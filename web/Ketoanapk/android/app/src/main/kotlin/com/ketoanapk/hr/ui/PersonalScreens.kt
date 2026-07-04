package com.ketoanapk.hr.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Gavel
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Inbox
import androidx.compose.material.icons.filled.Payments
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.TimesheetDay
import com.ketoanapk.hr.ui.theme.Danger
import com.ketoanapk.hr.ui.theme.Success
import com.ketoanapk.hr.ui.theme.Warning
import java.time.LocalDate

@Composable
fun HomeScreen(user: HrUser, state: HomeUiState, onSelect: (HrDestination) -> Unit) {
    val today = state.timesheet?.days?.firstOrNull { it.date.take(10) == todayKey() }
    val name = state.employee?.fullName?.ifBlank { user.displayName } ?: user.displayName
    val latest = state.requests.firstOrNull()

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item {
            HrCard {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    UserAvatar(name, 52)
                    Spacer(Modifier.width(12.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text(name, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(
                            buildString {
                                append(state.employee?.position?.ifBlank { "Nhân viên" } ?: "Nhân viên")
                                val dept = state.employee?.departmentName
                                if (!dept.isNullOrBlank()) append(" · $dept")
                            },
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(state.employee?.employeeCode?.ifBlank { user.username } ?: user.username, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
                Button(
                    onClick = { onSelect(HrDestination.Scan) },
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Icon(Icons.Filled.Face, contentDescription = null, modifier = Modifier.size(20.dp))
                    Spacer(Modifier.width(8.dp))
                    Text("Chấm công", fontWeight = FontWeight.Bold)
                }
            }
        }

        item {
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
                StatCard("Hôm nay", today?.status?.ifBlank { "Chưa chấm" } ?: "Chưa chấm", "Vào ${today?.checkIn ?: "--:--"}", timesheetTone(today?.status), modifier = Modifier.weight(1f))
                StatCard("Ngày công", "${state.timesheet?.summary?.workedDays ?: 0.0}", "Vắng ${state.timesheet?.summary?.absentDays ?: 0.0}", modifier = Modifier.weight(1f))
            }
        }

        item { SectionTitle("Cá nhân") }
        item {
            MenuGroupCard {
                MenuRow(Icons.Filled.AccountCircle, "Hồ sơ của tôi", "Thông tin, hợp đồng, lương") { onSelect(HrDestination.Profile) }
                MenuDivider()
                MenuRow(Icons.Filled.CalendarMonth, "Bảng công", "Công, đi muộn, tăng ca") { onSelect(HrDestination.Timesheet) }
                MenuDivider()
                MenuRow(
                    Icons.Filled.Description,
                    "Đơn từ",
                    latest?.let { "Gần nhất: ${it.typeLabel} · ${requestStatusLabel(it.status)}" } ?: "Nghỉ phép, tăng ca, quên chấm công",
                ) { onSelect(HrDestination.Requests) }
            }
        }

        item { SectionTitle("Công việc") }
        item {
            val pending = state.inbox.count { it.status.equals("Pending", true) }
            MenuGroupCard {
                MenuRow(Icons.Filled.Inbox, "Phê duyệt", if (user.isAdmin) "$pending đơn chờ xử lý" else "Hộp thư duyệt của bạn") { onSelect(HrDestination.Approval) }
                MenuDivider()
                MenuRow(Icons.Filled.Gavel, "Phạt / kỷ luật", if (user.isAdmin) "Quản lý quyết định phạt" else "Xem các lần bị phạt") { onSelect(HrDestination.Penalty) }
            }
        }

        if (user.isAdmin) {
            item { SectionTitle("Quản trị") }
            item {
                MenuGroupCard {
                    MenuRow(Icons.Filled.People, "Quản lý nhân sự", "Quân số, phòng ban, nhân viên") { onSelect(HrDestination.People) }
                    MenuDivider()
                    MenuRow(Icons.Filled.Payments, "Bảng lương", "Mức lương nhân viên") { onSelect(HrDestination.Payroll) }
                    MenuDivider()
                    MenuRow(Icons.Filled.History, "Nhật ký hệ thống", "Lịch sử thao tác") { onSelect(HrDestination.Audit) }
                }
            }
        }

        item { SectionTitle("Hệ thống") }
        item {
            MenuGroupCard {
                MenuRow(Icons.Filled.Settings, "Cài đặt", "Tài khoản, đăng xuất") { onSelect(HrDestination.Settings) }
            }
        }

        state.error?.let { item { ErrorText(it) } }
    }
}

@Composable
fun ProfileScreen(state: HomeUiState) {
    val e = state.employee
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHead("Hồ sơ của tôi", e?.employeeCode?.ifBlank { "Nhân viên" } ?: "Nhân viên") }
        if (state.loading && e == null) item { LoadingBlock() }
        if (e == null) {
            item { EmptyState("Chưa có hồ sơ", state.error ?: "Không tải được hồ sơ nhân viên.") }
        } else {
            item {
                HrCard {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        UserAvatar(e.fullName.ifBlank { e.username }, 56)
                        Spacer(Modifier.width(12.dp))
                        Column(modifier = Modifier.weight(1f)) {
                            Text(e.fullName.ifBlank { e.username }, style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(e.position.ifBlank { "Nhân viên" }, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        StatusChip(if (e.status == "Active") "Đang làm" else e.status, if (e.status == "Active") Tone.Success else Tone.Muted)
                    }
                }
            }
            item {
                HrCard {
                    SectionTitle("Thông tin")
                    LabelValue("Phòng ban", e.departmentName.ifBlank { "Chưa gán" })
                    LabelValue("Quản lý", e.managerName.ifBlank { "--" })
                    LabelValue("Ngày vào làm", formatIsoDate(e.hireDate))
                    LabelValue("Ngày sinh", formatIsoDate(e.dob))
                    LabelValue("Giới tính", e.gender.ifBlank { "--" })
                }
            }
            item {
                HrCard {
                    SectionTitle("Liên hệ")
                    LabelValue("Điện thoại", e.phone.ifBlank { "--" })
                    LabelValue("Email", e.email.ifBlank { "--" })
                    LabelValue("Địa chỉ", e.address.ifBlank { "--" })
                }
            }
        }
    }
}

@Composable
fun TimesheetScreen(
    state: TimesheetUiState,
    onMonthOffset: (Int) -> Unit,
    onCurrentMonth: () -> Unit,
) {
    val ts = state.timesheet
    var selectedDate by rememberSaveable(ts?.period ?: "") { mutableStateOf<String?>(null) }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHead("Bảng công", formatTimesheetPeriod(ts?.period ?: state.month)) }
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                OutlinedButton(onClick = { onMonthOffset(-1) }, modifier = Modifier.weight(1f)) { Text("Trước") }
                Button(onClick = onCurrentMonth, modifier = Modifier.weight(1f)) { Text("Tháng này") }
                OutlinedButton(onClick = { onMonthOffset(1) }, modifier = Modifier.weight(1f)) { Text("Sau") }
            }
        }
        if (state.loading && ts == null) item { LoadingBlock() }
        if (ts == null) {
            item { EmptyState("Chưa có bảng công", state.error ?: "Không tải được dữ liệu bảng công.") }
        } else {
            val daysByDate = ts.days.associateBy { it.date.take(10) }
            val selectedDay = selectedDate?.let { daysByDate[it] }

            item {
                TimesheetCalendarCard(
                    period = ts.period,
                    daysByDate = daysByDate,
                    selectedDate = selectedDate,
                    onSelectDate = { selectedDate = it },
                )
            }
            if (selectedDate != null) {
                item { TimesheetDayDetailCard(selectedDate.orEmpty(), selectedDay) }
            }
            item {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                        StatCard("Ngày công", "${ts.summary.workedDays}", "Vắng ${ts.summary.absentDays} ngày", modifier = Modifier.weight(1f))
                        StatCard("Đi muộn", "${ts.summary.lateDays}", formatMinutes(ts.summary.totalLateMinutes), Tone.Warning, modifier = Modifier.weight(1f))
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                        StatCard("Tăng ca", formatMinutes(ts.summary.totalOvertimeMinutes), "${ts.summary.totalWorkedHours} giờ", Tone.Success, modifier = Modifier.weight(1f))
                        StatCard("Về sớm", "${ts.summary.earlyDays}", formatMinutes(ts.summary.totalEarlyMinutes), Tone.Warning, modifier = Modifier.weight(1f))
                    }
                }
            }
        }
    }
}

@Composable
private fun TimesheetCalendarCard(
    period: String,
    daysByDate: Map<String, TimesheetDay>,
    selectedDate: String?,
    onSelectDate: (String) -> Unit,
) {
    val rows = timesheetCalendarCells(period).chunked(7)
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text("Lịch tháng", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
                Text(
                    "Chọn một ngày để mở chi tiết chấm công.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            StatusChip(formatTimesheetPeriod(period), Tone.Neutral)
        }

        TimesheetLegend()

        Row(horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.fillMaxWidth()) {
            timesheetWeekdays.forEach { label ->
                Text(
                    text = label,
                    modifier = Modifier.weight(1f),
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                    fontWeight = FontWeight.ExtraBold,
                )
            }
        }

        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            rows.forEach { week ->
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.fillMaxWidth()) {
                    week.forEach { cell ->
                        if (cell.dateKey == null || cell.day == null) {
                            Spacer(
                                modifier = Modifier
                                    .weight(1f)
                                    .aspectRatio(0.92f),
                            )
                        } else {
                            val day = daysByDate[cell.dateKey]
                            TimesheetCalendarDayCell(
                                dayNumber = cell.day,
                                day = day,
                                selected = selectedDate == cell.dateKey,
                                modifier = Modifier.weight(1f),
                                onClick = { onSelectDate(cell.dateKey) },
                            )
                        }
                    }
                }
            }
        }

        if (daysByDate.isEmpty()) {
            Text(
                "Tháng này chưa có chấm công hoặc phân ca. Các ô sẽ đổi màu khi có dữ liệu.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun TimesheetLegend() {
    val rows = timesheetLegendItems.chunked(2)
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        rows.forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                row.forEach { item ->
                    LegendChip(item.label, item.tone, Modifier.weight(1f))
                }
                if (row.size == 1) Spacer(Modifier.weight(1f))
            }
        }
    }
}

@Composable
private fun LegendChip(label: String, tone: TimesheetCalendarTone, modifier: Modifier = Modifier) {
    val color = timesheetCalendarColor(tone)
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(999.dp),
        color = color.copy(alpha = 0.10f),
        border = BorderStroke(1.dp, color.copy(alpha = 0.28f)),
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 7.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(7.dp),
        ) {
            Box(
                modifier = Modifier
                    .size(8.dp)
                    .background(color, CircleShape),
            )
            Text(
                label,
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurface,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun TimesheetCalendarDayCell(
    dayNumber: Int,
    day: TimesheetDay?,
    selected: Boolean,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val tone = timesheetCalendarTone(day)
    val color = timesheetCalendarColor(tone)
    Surface(
        modifier = modifier
            .aspectRatio(0.92f)
            .clickable(onClick = onClick),
        shape = RoundedCornerShape(11.dp),
        color = color.copy(alpha = if (selected) 0.22f else 0.12f),
        border = BorderStroke(if (selected) 2.dp else 1.dp, if (selected) MaterialTheme.colorScheme.primary else color.copy(alpha = 0.45f)),
    ) {
        Column(
            modifier = Modifier.padding(7.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Text(
                "$dayNumber",
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurface,
                fontWeight = FontWeight.ExtraBold,
                maxLines = 1,
            )
            Box(
                modifier = Modifier
                    .size(8.dp)
                    .background(color, CircleShape),
            )
            Text(
                timesheetCalendarLabel(day),
                style = MaterialTheme.typography.labelSmall,
                color = color,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun TimesheetDayDetailCard(dateKey: String, day: TimesheetDay?) {
    val holidayLabel = day?.takeIf { isTimesheetHoliday(it) }?.let { timesheetHolidayLabel(it) }
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text("Chi tiết ngày ${formatIsoDate(dateKey)}", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
                Text(
                    holidayLabel
                        ?: day?.shiftName?.ifBlank { "Không phân ca" }
                        ?: "Chưa có ca làm hoặc log chấm công",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            StatusChip(
                if (holidayLabel != null) "Ngày nghỉ" else day?.status?.ifBlank { "--" } ?: "Chưa có dữ liệu",
                day?.let { if (isTimesheetHoliday(it)) Tone.Info else timesheetTone(it.status) } ?: Tone.Muted,
            )
        }
        if (day == null) {
            Text(
                "Ngày này chưa có dữ liệu chấm công để rà soát.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        } else {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                TimesheetDetailMetric("Giờ vào", day.checkIn ?: "--:--", modifier = Modifier.weight(1f))
                TimesheetDetailMetric("Giờ ra", day.checkOut ?: "--:--", modifier = Modifier.weight(1f))
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                TimesheetDetailMetric("Giờ làm", "${day.workedHours} giờ", modifier = Modifier.weight(1f))
                TimesheetDetailMetric("Tăng ca", formatMinutes(day.overtimeMinutes), if (day.overtimeMinutes > 0) TimesheetCalendarTone.Overtime else null, Modifier.weight(1f))
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                TimesheetDetailMetric("Đi muộn", formatMinutes(day.lateMinutes), if (day.lateMinutes > 0) TimesheetCalendarTone.Warning else null, Modifier.weight(1f))
                TimesheetDetailMetric("Về sớm", formatMinutes(day.earlyMinutes), if (day.earlyMinutes > 0) TimesheetCalendarTone.Warning else null, Modifier.weight(1f))
            }
            if (isTimesheetHoliday(day)) {
                TimesheetDetailMetric(
                    "Ngày nghỉ",
                    timesheetHolidayLabel(day),
                    TimesheetCalendarTone.Holiday,
                )
            }
        }
    }
}

@Composable
private fun TimesheetDetailMetric(
    label: String,
    value: String,
    tone: TimesheetCalendarTone? = null,
    modifier: Modifier = Modifier,
) {
    val color = tone?.let { timesheetCalendarColor(it) } ?: MaterialTheme.colorScheme.onSurface
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(12.dp),
        color = (tone?.let { timesheetCalendarColor(it).copy(alpha = 0.10f) } ?: MaterialTheme.colorScheme.surfaceVariant),
    ) {
        Column(modifier = Modifier.padding(10.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(label, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(value, style = MaterialTheme.typography.titleSmall, color = color, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

private val timesheetWeekdays = listOf("T2", "T3", "T4", "T5", "T6", "T7", "CN")

private enum class TimesheetCalendarTone { Worked, Absent, Overtime, Warning, Holiday, Off, Empty }

private data class TimesheetCalendarCell(
    val key: String,
    val day: Int? = null,
    val dateKey: String? = null,
)

private data class TimesheetLegendItem(val tone: TimesheetCalendarTone, val label: String)

private val timesheetLegendItems = listOf(
    TimesheetLegendItem(TimesheetCalendarTone.Worked, "Đi làm"),
    TimesheetLegendItem(TimesheetCalendarTone.Absent, "Nghỉ / vắng"),
    TimesheetLegendItem(TimesheetCalendarTone.Overtime, "Tăng ca"),
    TimesheetLegendItem(TimesheetCalendarTone.Warning, "Muộn / thiếu công"),
    TimesheetLegendItem(TimesheetCalendarTone.Holiday, "Ngày nghỉ"),
    TimesheetLegendItem(TimesheetCalendarTone.Off, "Không ca"),
)

private fun timesheetCalendarCells(period: String): List<TimesheetCalendarCell> {
    val monthStart = timesheetMonthStart(period)
    val cells = mutableListOf<TimesheetCalendarCell>()
    val leadingBlanks = monthStart.dayOfWeek.value - 1
    repeat(leadingBlanks) { cells += TimesheetCalendarCell(key = "blank-start-$it") }
    for (day in 1..monthStart.lengthOfMonth()) {
        val date = monthStart.withDayOfMonth(day).toString()
        cells += TimesheetCalendarCell(key = date, day = day, dateKey = date)
    }
    val trailingBlanks = (7 - cells.size % 7) % 7
    repeat(trailingBlanks) { cells += TimesheetCalendarCell(key = "blank-end-$it") }
    return cells
}

private fun timesheetMonthStart(period: String?): LocalDate {
    val key = period?.trim()?.take(7).orEmpty()
    return runCatching {
        if (key.length == 7) LocalDate.parse("$key-01") else LocalDate.now().withDayOfMonth(1)
    }.getOrElse { LocalDate.now().withDayOfMonth(1) }
}

private fun formatTimesheetPeriod(period: String?): String {
    if (period.isNullOrBlank()) return "Tháng hiện tại"
    return runCatching {
        val start = timesheetMonthStart(period)
        "Tháng ${start.monthValue.toString().padStart(2, '0')}/${start.year}"
    }.getOrElse { period }
}

private fun timesheetCalendarTone(day: TimesheetDay?): TimesheetCalendarTone {
    if (day == null) return TimesheetCalendarTone.Empty
    if (isTimesheetHoliday(day)) return TimesheetCalendarTone.Holiday
    val status = day.status.lowercase()
    return when {
        status.contains("vắng") || status.contains("nghỉ") || status.contains("absent") -> TimesheetCalendarTone.Absent
        day.overtimeMinutes > 0 || status.contains("tăng ca") || status.contains("overtime") -> TimesheetCalendarTone.Overtime
        day.lateMinutes > 0 || day.earlyMinutes > 0 ||
            status.contains("muộn") || status.contains("sớm") || status.contains("thiếu") -> TimesheetCalendarTone.Warning
        day.workedHours > 0.0 || !day.checkIn.isNullOrBlank() || !day.checkOut.isNullOrBlank() ||
            status.contains("đủ công") || status == "present" || status == "ok" -> TimesheetCalendarTone.Worked
        status.contains("không phân ca") -> TimesheetCalendarTone.Off
        else -> TimesheetCalendarTone.Empty
    }
}

private fun timesheetCalendarLabel(day: TimesheetDay?): String = when (timesheetCalendarTone(day)) {
    TimesheetCalendarTone.Worked -> "Đi làm"
    TimesheetCalendarTone.Absent -> "Vắng"
    TimesheetCalendarTone.Overtime -> "Tăng ca"
    TimesheetCalendarTone.Warning -> "Rà soát"
    TimesheetCalendarTone.Holiday -> day?.let { timesheetHolidayLabel(it) } ?: "Ngày nghỉ"
    TimesheetCalendarTone.Off -> "Không ca"
    TimesheetCalendarTone.Empty -> "Trống"
}

private fun isTimesheetHoliday(day: TimesheetDay): Boolean {
    val status = day.status.lowercase()
    return day.holidayType.isNotBlank() ||
        day.holidayName.isNotBlank() ||
        status.contains("nghỉ lễ") ||
        status.contains("nghỉ chủ nhật") ||
        status.contains("nghỉ công ty")
}

private fun timesheetHolidayLabel(day: TimesheetDay): String =
    day.holidayName.ifBlank {
        when {
            day.holidayType == "public" -> "Nghỉ lễ"
            day.holidayType == "weekly" -> "Nghỉ chủ nhật"
            day.holidayType.isNotBlank() -> "Nghỉ công ty"
            day.status.isNotBlank() -> day.status
            else -> "Ngày nghỉ"
        }
    }

@Composable
private fun timesheetCalendarColor(tone: TimesheetCalendarTone): Color = when (tone) {
    TimesheetCalendarTone.Worked -> Success
    TimesheetCalendarTone.Absent -> Danger
    TimesheetCalendarTone.Overtime -> Color(0xFF7C3AED)
    TimesheetCalendarTone.Warning -> Warning
    TimesheetCalendarTone.Holiday -> Color(0xFF0284C7)
    TimesheetCalendarTone.Off -> Color(0xFF64748B)
    TimesheetCalendarTone.Empty -> MaterialTheme.colorScheme.onSurfaceVariant
}

@Composable
fun RequestsScreen(state: HomeUiState, onCancel: (String) -> Unit) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHead("Đơn từ của tôi", "${state.requests.size} đơn") }
        if (state.loading && state.requests.isEmpty()) item { LoadingBlock() }
        if (state.requests.isEmpty()) {
            item { EmptyState("Chưa có đơn", "Bạn chưa gửi đơn từ nào.") }
        } else {
            items(state.requests, key = { it.id }) { req -> RequestCard(req, onCancel) }
        }
    }
}

@Composable
private fun RequestCard(req: RequestListItem, onCancel: (String) -> Unit) {
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text(req.typeLabel.ifBlank { req.title }, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${req.requestNo} · ${formatIsoDateTime(req.createdAt)}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            StatusChip(requestStatusLabel(req.status), requestTone(req.status))
        }
        if (req.status.equals("Pending", true)) {
            OutlinedButton(onClick = { onCancel(req.id) }) { Text("Hủy đơn") }
        }
    }
}

@Composable
fun SimpleScreen(title: String, message: String) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHead(title, "Thông tin") }
        item { EmptyState(title, message) }
    }
}

// --- Helpers nhãn/màu trạng thái ---

fun requestStatusLabel(status: String): String = when (status.lowercase()) {
    "pending" -> "Chờ duyệt"
    "approved" -> "Đã duyệt"
    "rejected" -> "Từ chối"
    "cancelled", "canceled" -> "Đã hủy"
    else -> status.ifBlank { "--" }
}

fun requestTone(status: String): Tone = when (status.lowercase()) {
    "approved" -> Tone.Success
    "pending" -> Tone.Warning
    "rejected" -> Tone.Danger
    else -> Tone.Muted
}

fun timesheetTone(status: String?): Tone {
    val normalized = status?.lowercase()?.trim().orEmpty()
    return when {
        normalized in listOf("đủ công", "present", "ok") -> Tone.Success
        normalized.contains("nghỉ lễ") || normalized.contains("nghỉ công ty") || normalized.contains("nghỉ chủ nhật") -> Tone.Info
        normalized.contains("vắng") || normalized.contains("nghỉ") || normalized.contains("absent") -> Tone.Danger
        normalized.contains("muộn") || normalized.contains("sớm") || normalized.contains("thiếu") || normalized == "late" -> Tone.Warning
        normalized.contains("không phân ca") -> Tone.Muted
        else -> Tone.Neutral
    }
}
