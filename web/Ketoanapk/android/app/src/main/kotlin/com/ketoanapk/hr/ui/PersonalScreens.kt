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
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowLeft
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.automirrored.filled.Login
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material.icons.filled.AssignmentLate
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.CenterFocusStrong
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.EventAvailable
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.Gavel
import androidx.compose.material.icons.filled.Groups
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Inbox
import androidx.compose.material.icons.filled.Payments
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.PersonOff
import androidx.compose.material.icons.filled.PostAdd
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Timer
import androidx.compose.material.icons.filled.WatchLater
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import androidx.compose.ui.unit.sp
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.ManagerHeadcount
import com.ketoanapk.hr.data.PayEstimate
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.Timesheet
import com.ketoanapk.hr.data.TimesheetDay
import com.ketoanapk.hr.ui.theme.BrandRed
import com.ketoanapk.hr.ui.theme.Danger
import com.ketoanapk.hr.ui.theme.InfoBlue
import com.ketoanapk.hr.ui.theme.Success
import com.ketoanapk.hr.ui.theme.Warning
import java.time.LocalDate
import java.time.format.DateTimeFormatter

@Composable
fun HomeScreen(
    user: HrUser,
    state: HomeUiState,
    manager: ManagerUiState,
    onSelect: (HrDestination) -> Unit,
) {
    val today = state.timesheet?.days?.firstOrNull { it.date.take(10) == todayKey() }
    val name = state.employee?.fullName?.ifBlank { user.displayName } ?: user.displayName
    val position = buildString {
        append(state.employee?.position?.ifBlank { "Nhân viên" } ?: "Nhân viên")
        val dept = state.employee?.departmentName
        if (!dept.isNullOrBlank()) append(" · $dept")
    }
    val summary = state.timesheet?.summary

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item { HomeHeroCard(name, position, today) { onSelect(HrDestination.Scan) } }

        if (user.isAdmin) {
            item { AdminDashboardCard(manager.summary?.headcount) { onSelect(HrDestination.Approval) } }
        } else {
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                    StatTile(Icons.Filled.EventAvailable, "Ngày công", trimNum(summary?.workedDays ?: 0.0), Success, Modifier.weight(1f))
                    StatTile(Icons.Filled.Schedule, "Tăng ca", formatMinutes(summary?.totalOvertimeMinutes ?: 0), InfoBlue, Modifier.weight(1f))
                }
            }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                    StatTile(Icons.Filled.WatchLater, "Đi muộn", "${summary?.lateDays ?: 0}", Warning, Modifier.weight(1f))
                    StatTile(Icons.Filled.PersonOff, "Vắng", trimNum(summary?.absentDays ?: 0.0), Danger, Modifier.weight(1f))
                }
            }
        }

        item { QuickActionsCard(onSelect) }

        if (!user.isAdmin) {
            item { MiniWeekCard(state.timesheet) { onSelect(HrDestination.Timesheet) } }
        }

        item { AttentionCard(state.requests) { onSelect(HrDestination.Requests) } }

        state.error?.let { item { ErrorText(it) } }
    }
}

/** Thẻ tiêu đề tối: avatar + tên + trạng thái ca + Vào/Ra + nút Chấm công ngay. */
@Composable
private fun HomeHeroCard(name: String, position: String, today: TimesheetDay?, onScan: () -> Unit) {
    val checkedIn = !today?.checkIn.isNullOrBlank()
    val checkedOut = !today?.checkOut.isNullOrBlank()
    val statusText = when {
        checkedIn && !checkedOut -> "Đã vào ca"
        checkedIn && checkedOut -> "Đã tan ca"
        else -> "Chưa vào ca"
    }
    val statusColor = when {
        checkedIn && !checkedOut -> Color(0xFF34D399)
        checkedIn && checkedOut -> Color(0xFF60A5FA)
        else -> Color(0xFF94A3B8)
    }
    HeroContainer {
        Row(verticalAlignment = Alignment.CenterVertically) {
            UserAvatar(name, 56)
            Spacer(Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(5.dp)) {
                Text(name, style = MaterialTheme.typography.titleLarge, color = Color.White, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(position, style = MaterialTheme.typography.bodyMedium, color = Color(0xFFB7C0CE), maxLines = 1, overflow = TextOverflow.Ellipsis)
                HeroBadge(statusText, statusColor)
            }
        }
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(16.dp))
                .background(Color(0x1FFFFFFF))
                .padding(4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            HeroPunch(Icons.AutoMirrored.Filled.Login, "Vào", today?.checkIn ?: "--:--", Color(0xFF22C55E), Modifier.weight(1f))
            Box(
                Modifier
                    .width(1.dp)
                    .height(38.dp)
                    .background(Color(0x26FFFFFF)),
            )
            HeroPunch(Icons.AutoMirrored.Filled.Logout, "Ra", today?.checkOut ?: "--:--", Color(0xFF64748B), Modifier.weight(1f))
        }
        Button(
            onClick = onScan,
            modifier = Modifier
                .fillMaxWidth()
                .height(52.dp),
            shape = RoundedCornerShape(16.dp),
            colors = ButtonDefaults.buttonColors(containerColor = BrandRed, contentColor = Color.White),
        ) {
            Icon(Icons.Filled.CenterFocusStrong, contentDescription = null, modifier = Modifier.size(22.dp))
            Spacer(Modifier.width(10.dp))
            Text("Chấm công ngay", fontWeight = FontWeight.Bold, fontSize = 16.sp)
        }
    }
}

@Composable
private fun HeroPunch(icon: ImageVector, label: String, value: String, accent: Color, modifier: Modifier = Modifier) {
    Row(
        modifier = modifier.padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Box(
            modifier = Modifier
                .size(38.dp)
                .clip(RoundedCornerShape(11.dp))
                .background(accent),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, contentDescription = null, tint = Color.White, modifier = Modifier.size(20.dp))
        }
        Column(verticalArrangement = Arrangement.spacedBy(1.dp)) {
            Text(label, style = MaterialTheme.typography.labelSmall, color = Color(0xFF94A3B8))
            Text(value, style = MaterialTheme.typography.titleMedium, color = Color.White)
        }
    }
}

/** Tác vụ nhanh: 4 phím tắt tròn màu. */
@Composable
private fun QuickActionsCard(onSelect: (HrDestination) -> Unit) {
    HrCard {
        CardHeader("Tác vụ nhanh")
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            QuickAction(Icons.Filled.PostAdd, "Tạo đơn", BrandRed, Modifier.weight(1f)) { onSelect(HrDestination.Requests) }
            QuickAction(Icons.Filled.CalendarMonth, "Bảng công", Success, Modifier.weight(1f)) { onSelect(HrDestination.Timesheet) }
            QuickAction(Icons.Filled.Folder, "Hồ sơ", InfoBlue, Modifier.weight(1f)) { onSelect(HrDestination.Profile) }
            QuickAction(Icons.Filled.Gavel, "Kỷ luật", Warning, Modifier.weight(1f)) { onSelect(HrDestination.Penalty) }
        }
    }
}

@Composable
private fun QuickAction(icon: ImageVector, label: String, accent: Color, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Column(
        modifier = modifier
            .clip(RoundedCornerShape(14.dp))
            .clickable(onClick = onClick)
            .padding(vertical = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Box(
            modifier = Modifier
                .size(52.dp)
                .clip(RoundedCornerShape(16.dp))
                .background(accent.copy(alpha = 0.14f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, contentDescription = null, tint = accent, modifier = Modifier.size(26.dp))
        }
        Text(label, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center)
    }
}

/** Bảng công mini: dải tuần hiện tại + số ngày đủ công + số lần đi muộn + nút xem đầy đủ. */
@Composable
private fun MiniWeekCard(ts: Timesheet?, onView: () -> Unit) {
    val today = LocalDate.now()
    val monday = today.minusDays((today.dayOfWeek.value - 1).toLong())
    val daysByDate = ts?.days?.associateBy { it.date.take(10) } ?: emptyMap()
    val labels = listOf("T2", "T3", "T4", "T5", "T6", "T7", "CN")
    HrCard {
        CardHeader("Bảng công tuần này")
        Text(weekRangeLabel(monday), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            for (i in 0..6) {
                val date = monday.plusDays(i.toLong())
                MiniDayCell(
                    label = labels[i],
                    dayNumber = date.dayOfMonth,
                    day = daysByDate[date.toString()],
                    isToday = date == today,
                    isFuture = date.isAfter(today),
                    modifier = Modifier.weight(1f),
                )
            }
        }
        HorizontalDivider(color = MaterialTheme.colorScheme.outline)
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            MiniSummaryStat(trimNum(ts?.summary?.workedDays ?: 0.0), "ngày đủ công", Success, Modifier.weight(1f))
            MiniSummaryStat("${ts?.summary?.lateDays ?: 0}", "lần đi muộn", Warning, Modifier.weight(1f))
        }
        OutlinedButton(onClick = onView, modifier = Modifier.fillMaxWidth()) {
            Text("Xem bảng công", fontWeight = FontWeight.Bold)
            Spacer(Modifier.width(4.dp))
            Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, modifier = Modifier.size(18.dp))
        }
    }
}

@Composable
private fun MiniDayCell(
    label: String,
    dayNumber: Int,
    day: TimesheetDay?,
    isToday: Boolean,
    isFuture: Boolean,
    modifier: Modifier = Modifier,
) {
    val dotColor = if (isFuture || day == null) {
        MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.35f)
    } else {
        timesheetCalendarColor(timesheetCalendarTone(day))
    }
    Column(
        modifier = modifier
            .clip(RoundedCornerShape(12.dp))
            .background(if (isToday) MaterialTheme.colorScheme.primaryContainer else Color.Transparent)
            .padding(vertical = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Text(label, style = MaterialTheme.typography.labelSmall, color = if (isToday) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant)
        Text("$dayNumber", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
        Box(
            Modifier
                .size(7.dp)
                .clip(CircleShape)
                .background(dotColor),
        )
    }
}

@Composable
private fun MiniSummaryStat(value: String, label: String, accent: Color, modifier: Modifier = Modifier) {
    Row(modifier = modifier, verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(value, style = MaterialTheme.typography.headlineSmall, color = accent)
        Text(label, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

/** Việc cần chú ý: số đơn của tôi đang chờ + đơn gần nhất. */
@Composable
private fun AttentionCard(requests: List<RequestListItem>, onOpen: () -> Unit) {
    val pending = requests.count { it.status.equals("Pending", true) }
    val latest = requests.firstOrNull()
    HrCard {
        CardHeader("Việc cần chú ý", onMore = onOpen)
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(14.dp))
                .clickable(onClick = onOpen),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Box(
                modifier = Modifier
                    .size(46.dp)
                    .clip(CircleShape)
                    .background(Warning.copy(alpha = 0.14f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Filled.AssignmentLate, contentDescription = null, tint = Warning, modifier = Modifier.size(24.dp))
            }
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(
                    if (pending > 0) "$pending đơn chờ duyệt" else "Không có đơn chờ duyệt",
                    style = MaterialTheme.typography.titleMedium,
                    color = if (pending > 0) Warning else MaterialTheme.colorScheme.onSurface,
                )
                Text(
                    latest?.let { "Đơn gần nhất: ${it.typeLabel} · ${requestStatusLabel(it.status)}" } ?: "Bạn chưa gửi đơn nào",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

/** Dashboard quản trị trên Trang chủ: quân số, có mặt, nghỉ/công tác, vắng, đi muộn, đơn chờ duyệt. */
@Composable
private fun AdminDashboardCard(h: ManagerHeadcount?, onApprovals: () -> Unit) {
    val onLeave = (h?.leave ?: 0) + (h?.business ?: 0)
    HrCard {
        CardHeader("Tổng quan hôm nay")
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
            StatTile(Icons.Filled.Groups, "Quân số", "${h?.active ?: 0}", InfoBlue, Modifier.weight(1f))
            StatTile(Icons.Filled.CheckCircle, "Có mặt", "${h?.present ?: 0}", Success, Modifier.weight(1f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
            StatTile(Icons.Filled.FlightTakeoff, "Nghỉ / công tác", "$onLeave", Warning, Modifier.weight(1f))
            StatTile(Icons.Filled.PersonOff, "Vắng", "${h?.absent ?: 0}", Danger, Modifier.weight(1f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
            StatTile(Icons.Filled.WatchLater, "Đi muộn", "${h?.late ?: 0}", Warning, Modifier.weight(1f))
            StatTile(Icons.Filled.Inbox, "Đơn chờ duyệt", "${h?.pendingApprovals ?: 0}", InfoBlue, Modifier.weight(1f), onClick = onApprovals)
        }
    }
}

/** Bỏ đuôi ".0" cho số ngày công (18.0 → 18, 18.5 → 18.5). */
private fun trimNum(v: Double): String =
    if (v == v.toLong().toDouble()) v.toLong().toString() else v.toString()

private fun weekRangeLabel(monday: LocalDate): String {
    val fmt = DateTimeFormatter.ofPattern("dd/MM")
    return "${monday.format(fmt)} - ${monday.plusDays(6).format(fmt)}"
}

@Composable
fun ProfileScreen(state: HomeUiState) {
    val e = state.employee
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        if (e == null) {
            item { PageHeader(Icons.Filled.AccountCircle, "Hồ sơ của tôi", "Nhân viên", Tone.Neutral) }
            if (state.loading) item { LoadingBlock() }
            item { EmptyState("Chưa có hồ sơ", state.error ?: "Không tải được hồ sơ nhân viên.") }
        } else {
            item {
                IdentityHero(
                    name = e.fullName.ifBlank { e.username },
                    subtitle = buildString {
                        append(e.position.ifBlank { "Nhân viên" })
                        if (e.departmentName.isNotBlank()) append(" · ${e.departmentName}")
                    },
                    statusText = if (e.status == "Active") "Đang làm việc" else e.status,
                    statusColor = if (e.status == "Active") Color(0xFF34D399) else Color(0xFF94A3B8),
                    footer = {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(16.dp))
                                .background(Color(0x1FFFFFFF))
                                .padding(vertical = 12.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            HeroFooterStat("Mã nhân viên", e.employeeCode.ifBlank { "--" }, Modifier.weight(1f))
                            Box(
                                Modifier
                                    .width(1.dp)
                                    .height(30.dp)
                                    .background(Color(0x26FFFFFF)),
                            )
                            HeroFooterStat("Ngày vào làm", formatIsoDate(e.hireDate), Modifier.weight(1f))
                        }
                    },
                )
            }
            item {
                HrCard {
                    CardHeader("Thông tin nhân sự")
                    LabelValue("Phòng ban", e.departmentName.ifBlank { "Chưa gán" })
                    LabelValue("Quản lý", e.managerName.ifBlank { "--" })
                    LabelValue("Ngày vào làm", formatIsoDate(e.hireDate))
                    LabelValue("Ngày sinh", formatIsoDate(e.dob))
                    LabelValue("Giới tính", e.gender.ifBlank { "--" })
                }
            }
            item {
                HrCard {
                    CardHeader("Liên hệ")
                    LabelValue("Điện thoại", e.phone.ifBlank { "--" })
                    LabelValue("Email", e.email.ifBlank { "--" })
                    LabelValue("Địa chỉ", e.address.ifBlank { "--" })
                }
            }
        }
    }
}

/** Ô số liệu nhỏ trên nền hero tối (canh giữa) — dùng cho footer Hồ sơ. */
@Composable
private fun HeroFooterStat(label: String, value: String, modifier: Modifier = Modifier) {
    Column(
        modifier = modifier,
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(3.dp),
    ) {
        Text(label, style = MaterialTheme.typography.labelSmall, color = Color(0xFF94A3B8), maxLines = 1, overflow = TextOverflow.Ellipsis)
        Text(value, style = MaterialTheme.typography.titleSmall, color = Color.White, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

@Composable
fun TimesheetScreen(
    state: TimesheetUiState,
    onMonthOffset: (Int) -> Unit,
    onSelectMonth: (String) -> Unit,
) {
    val ts = state.timesheet
    var selectedDate by rememberSaveable(ts?.period ?: "") { mutableStateOf<String?>(null) }
    var pickerOpen by rememberSaveable { mutableStateOf(false) }
    val currentStart = timesheetMonthStart(ts?.period ?: state.month)
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHeader(Icons.Filled.CalendarMonth, "Bảng công", formatTimesheetPeriod(ts?.period ?: state.month), Tone.Neutral) }
        item {
            MonthSelectorBar(
                label = formatTimesheetPeriod(ts?.period ?: state.month),
                onPrev = { onMonthOffset(-1) },
                onNext = { onMonthOffset(1) },
                onPick = { pickerOpen = true },
            )
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
            item { SectionTitle("Tổng hợp tháng", modifier = Modifier.padding(start = 4.dp)) }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                    StatTile(Icons.Filled.EventAvailable, "Ngày công", trimNum(ts.summary.workedDays), Success, Modifier.weight(1f))
                    StatTile(Icons.Filled.Schedule, "Tăng ca", formatMinutes(ts.summary.totalOvertimeMinutes), InfoBlue, Modifier.weight(1f))
                }
            }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                    StatTile(Icons.Filled.WatchLater, "Đi muộn", "${ts.summary.lateDays}", Warning, Modifier.weight(1f))
                    StatTile(Icons.AutoMirrored.Filled.Logout, "Về sớm", "${ts.summary.earlyDays}", Warning, Modifier.weight(1f))
                }
            }
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                    StatTile(Icons.Filled.PersonOff, "Vắng", trimNum(ts.summary.absentDays), Danger, Modifier.weight(1f))
                    StatTile(Icons.Filled.Timer, "Giờ làm", "${trimNum(ts.summary.totalWorkedHours)}h", Color(0xFF0D9488), Modifier.weight(1f))
                }
            }
        }
    }

    if (pickerOpen) {
        MonthYearPickerDialog(
            initialYear = currentStart.year,
            initialMonth = currentStart.monthValue,
            onDismiss = { pickerOpen = false },
            onConfirm = { year, month ->
                pickerOpen = false
                onSelectMonth("%04d-%02d".format(year, month))
            },
        )
    }
}

/**
 * Thanh chọn tháng: hai mũi tên ở hai đầu (lùi/tiến tháng), ở giữa hiện tháng đang xem
 * (vd "Tháng 7/2026") — bấm vào giữa để mở bộ chọn tháng/năm.
 */
@Composable
private fun MonthSelectorBar(
    label: String,
    onPrev: () -> Unit,
    onNext: () -> Unit,
    onPick: () -> Unit,
) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 4.dp, vertical = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = onPrev) {
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowLeft, contentDescription = "Tháng trước", tint = MaterialTheme.colorScheme.primary)
            }
            Row(
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(12.dp))
                    .clickable(onClick = onPick)
                    .padding(vertical = 10.dp),
                horizontalArrangement = Arrangement.Center,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(label, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
                Spacer(Modifier.width(4.dp))
                Icon(Icons.Filled.ArrowDropDown, contentDescription = "Chọn tháng/năm", tint = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            IconButton(onClick = onNext) {
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = "Tháng sau", tint = MaterialTheme.colorScheme.primary)
            }
        }
    }
}

/** Bộ chọn tháng/năm: chỉnh năm bằng 2 mũi tên, chọn 1 trong 12 tháng dạng lưới. */
@Composable
private fun MonthYearPickerDialog(
    initialYear: Int,
    initialMonth: Int,
    onDismiss: () -> Unit,
    onConfirm: (Int, Int) -> Unit,
) {
    var year by rememberSaveable { mutableStateOf(initialYear) }
    var month by rememberSaveable { mutableStateOf(initialMonth) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Chọn tháng bảng công", fontWeight = FontWeight.Bold) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                // Chỉnh năm
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    IconButton(onClick = { year -= 1 }) {
                        Icon(Icons.AutoMirrored.Filled.KeyboardArrowLeft, contentDescription = "Năm trước", tint = MaterialTheme.colorScheme.primary)
                    }
                    Text("Năm $year", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
                    IconButton(onClick = { year += 1 }) {
                        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = "Năm sau", tint = MaterialTheme.colorScheme.primary)
                    }
                }
                // Lưới 12 tháng (3 hàng x 4 cột)
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    (0 until 3).forEach { r ->
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                            (1..4).forEach { c ->
                                val m = r * 4 + c
                                val selected = m == month
                                Surface(
                                    modifier = Modifier
                                        .weight(1f)
                                        .clip(RoundedCornerShape(12.dp))
                                        .clickable { month = m },
                                    shape = RoundedCornerShape(12.dp),
                                    color = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surface,
                                    border = BorderStroke(1.dp, if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outline),
                                ) {
                                    Text(
                                        "Th $m",
                                        modifier = Modifier.padding(vertical = 12.dp),
                                        textAlign = TextAlign.Center,
                                        style = MaterialTheme.typography.bodyMedium,
                                        fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
                                        color = if (selected) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurface,
                                    )
                                }
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {
            Button(onClick = { onConfirm(year, month) }) { Text("Xem", fontWeight = FontWeight.Bold) }
        },
        dismissButton = {
            OutlinedButton(onClick = onDismiss) { Text("Hủy") }
        },
    )
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
fun SimpleScreen(title: String, message: String) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHeader(Icons.Filled.History, title, "Thông tin", Tone.Neutral) }
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

// ── Lương của tôi (nhân viên tự xem lương dự tính tháng hiện tại) ─────────────
@Composable
fun MySalaryScreen(state: PayEstimateUiState) {
    val est = state.data
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            PageHeader(
                Icons.Filled.Payments,
                "Lương của tôi",
                "Dự tính ${formatTimesheetPeriod(est?.period)}",
                Tone.Success,
            )
        }
        if (state.loading && est == null) item { LoadingBlock() }
        if (est == null && !state.loading) {
            item { EmptyState("Chưa có dữ liệu lương", state.error ?: "Không tải được lương dự tính.") }
        }
        if (est != null) {
            if (!est.hasSalary) {
                item {
                    HrCard {
                        Text(
                            "Bạn chưa được thiết lập mức lương. Số liệu dưới đây có thể chưa đầy đủ — vui lòng liên hệ quản trị nhân sự.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }

            // Thẻ nổi bật: thực nhận dự tính.
            item {
                HrCard {
                    Text("Thực nhận dự tính", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(
                        formatMoney(est.netPay),
                        style = MaterialTheme.typography.headlineMedium,
                        color = MaterialTheme.colorScheme.primary,
                        fontWeight = FontWeight.ExtraBold,
                    )
                    Text(
                        "Tổng thu ${formatMoney(est.totalEarnings)} − Khấu trừ ${formatMoney(est.totalDeductions)}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }

            item {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                    StatTile(Icons.Filled.EventAvailable, "Ngày công", "${est.workedDays}", Success, Modifier.weight(1f))
                    StatTile(Icons.Filled.Schedule, "Tăng ca", "${trimNum(est.overtimeHours)}h", InfoBlue, Modifier.weight(1f))
                }
            }

            item { SectionTitle("Khoản cộng", modifier = Modifier.padding(start = 4.dp)) }
            item {
                HrCard {
                    if (est.earnings.isEmpty()) {
                        Text("Không có khoản cộng.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    } else {
                        est.earnings.forEach { line -> LabelValue(line.label, formatMoney(line.amount)) }
                    }
                    HorizontalDivider(color = MaterialTheme.colorScheme.outline)
                    LabelValue("Tổng thu nhập", formatMoney(est.totalEarnings))
                }
            }

            item { SectionTitle("Khoản trừ", modifier = Modifier.padding(start = 4.dp)) }
            item {
                HrCard {
                    if (est.deductions.isEmpty()) {
                        Text("Không có khoản trừ.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    } else {
                        est.deductions.forEach { line -> LabelValue(line.label, "− ${formatMoney(line.amount)}") }
                    }
                    HorizontalDivider(color = MaterialTheme.colorScheme.outline)
                    LabelValue("Tổng khấu trừ", "− ${formatMoney(est.totalDeductions)}")
                }
            }

            item {
                Text(
                    "Đây là lương DỰ TÍNH của tháng hiện tại, đã gồm khấu trừ tiền phạt (nếu có). Số liệu có thể thay đổi khi quản trị chốt phiếu lương.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(horizontal = 4.dp),
                )
            }
        }
    }
}
