package com.ketoanapk.hr.ui

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.view.WindowManager
import androidx.activity.compose.BackHandler
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.Orientation
import androidx.compose.foundation.gestures.draggable
import androidx.compose.foundation.gestures.rememberDraggableState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
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
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.CenterFocusStrong
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Done
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.EventAvailable
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Gavel
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Inbox
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.NotificationsNone
import androidx.compose.material.icons.filled.Payments
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.PersonOff
import androidx.compose.material.icons.filled.ReceiptLong
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Timer
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material.icons.filled.WatchLater
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ketoanapk.hr.data.AppNotification
import com.ketoanapk.hr.data.AppPermissions
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.PayEstimate
import com.ketoanapk.hr.data.PayLine
import com.ketoanapk.hr.data.PayslipItem
import com.ketoanapk.hr.data.PayslipOvertimeDay
import com.ketoanapk.hr.data.PayslipRequirement
import com.ketoanapk.hr.data.DayLogStep
import com.ketoanapk.hr.data.Timesheet
import com.ketoanapk.hr.data.TimesheetDay
import com.ketoanapk.hr.data.AppPersonalization
import com.ketoanapk.hr.data.ServerClock
import com.ketoanapk.hr.ui.theme.BrandRed
import com.ketoanapk.hr.ui.theme.Danger
import com.ketoanapk.hr.ui.theme.InfoBlue
import com.ketoanapk.hr.ui.theme.Success
import com.ketoanapk.hr.ui.theme.Warning
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.repeatOnLifecycle
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import kotlin.math.absoluteValue
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

@Composable
fun HomeScreen(
    user: HrUser,
    state: HomeUiState,
    actions: List<HrDestination>,
    notifications: List<AppNotification>,
    announcement: String,
    serverNotices: List<String>,
    badgeCount: (HrDestination) -> Int,
    onOpenNotifications: () -> Unit,
    onSelect: (HrDestination) -> Unit,
) {
    val today = state.timesheet?.days?.firstOrNull { it.date.take(10) == todayKey() }
    val name = state.employee?.fullName?.ifBlank { user.displayName } ?: user.displayName
    val summary = state.timesheet?.summary

    val checkedIn = !today?.checkIn.isNullOrBlank()
    val checkedOut = !today?.checkOut.isNullOrBlank()
    var homeClockMinute by remember { mutableLongStateOf(ServerClock.nowMillis() / 60_000L) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(60_000L)
            homeClockMinute = ServerClock.nowMillis() / 60_000L
        }
    }
    val nowVietnam = remember(homeClockMinute) { ServerClock.nowVietnam() }
    // Danh sách thông báo cho hiệu ứng gõ chữ: gộp thông báo điều hành + lời nhắc admin sửa từ xa +
    // thông báo nghiệp vụ + nhắc việc + lời nhắc gắn sẵn.
    val payslipRequirement = state.payslipRequirement
    val notices = remember(announcement, serverNotices, notifications, today, name, payslipRequirement, nowVietnam) {
        homeNotices(announcement, serverNotices, notifications, name, checkedIn, checkedOut, payslipRequirement, nowVietnam)
    }
    // Lời chào theo buổi (sáng/trưa/chiều/tối) tính từ GIỜ MÁY CHỦ, luôn đứng đầu dải thông báo. Tính
    // mỗi lần dựng lại giao diện (rẻ); chuỗi ổn định trong cùng một buổi nên không làm hiệu ứng gõ khởi
    // động lại vô cớ, và tự đổi khi qua buổi mới hoặc khi đồng hồ máy chủ vừa đồng bộ xong.
    val greeting = timeGreetingLine(name, nowVietnam)
    val tickerMessages = remember(greeting, notices, payslipRequirement) {
        prioritizedHomeTickerMessages(greeting, notices, payslipRequirement)
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = screenPadding(16.dp, 16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item {
            NotificationTickerCard(
                messages = tickerMessages,
                onClick = if (payslipRequirement.pendingCount > 0) {
                    { onSelect(HrDestination.MyPayslips) }
                } else onOpenNotifications,
            )
        }

        item { CheckInCard(today) { onSelect(HrDestination.Scan) } }

        if (!user.can(AppPermissions.HrRead)) {
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

        item { ActionsCard(actions = actions, badgeCount = badgeCount, onSelect = onSelect) }

        if (!user.can(AppPermissions.HrRead)) {
            item { MiniWeekCard(state.timesheet) { onSelect(HrDestination.Timesheet) } }
        }

        state.error?.let { item { ErrorText(it) } }
    }
}

/**
 * Header Trang chủ trong suốt để artwork toàn màn hình không bị một hình chữ nhật che ngang.
 * Nội dung vẫn ghim dưới thanh trạng thái và giữ đủ tương phản trên nền sáng/tối.
 */
@Composable
fun HomeHeaderBar(user: HrUser, state: HomeUiState, unread: Int, onBell: () -> Unit) {
    val name = state.employee?.fullName?.ifBlank { user.displayName } ?: user.displayName
    val position = buildString {
        append(state.employee?.position?.ifBlank { "Nhân viên" } ?: "Nhân viên")
        val dept = state.employee?.departmentName
        if (!dept.isNullOrBlank()) append(" · $dept")
    }
    val today = state.timesheet?.days?.firstOrNull { it.date.take(10) == todayKey() }
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
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .statusBarsPadding()
            .padding(start = 18.dp, end = 10.dp, top = 8.dp, bottom = 16.dp),
    ) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.Top) {
            Row(modifier = Modifier.weight(1f), verticalAlignment = Alignment.CenterVertically) {
                UserAvatar(name, 56, avatar = state.employee?.avatar)
                Spacer(Modifier.width(14.dp))
                Column(verticalArrangement = Arrangement.spacedBy(5.dp)) {
                    Text(name, style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onBackground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(position, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    HeroBadge(statusText, statusColor)
                }
            }
            HeroBell(unread = unread, onClick = onBell)
        }
    }
}

/** Chuông thông báo trên header trong suốt, kèm chấm đỏ đếm số chưa đọc. */
@Composable
private fun HeroBell(unread: Int, onClick: () -> Unit) {
    Box(contentAlignment = Alignment.TopEnd) {
        IconButton(onClick = onClick, modifier = Modifier.size(44.dp)) {
            Icon(
                if (unread > 0) Icons.Filled.Notifications else Icons.Filled.NotificationsNone,
                contentDescription = "Thông báo",
                tint = MaterialTheme.colorScheme.onBackground,
                modifier = Modifier.size(26.dp),
            )
        }
        if (unread > 0) {
            Box(
                modifier = Modifier
                    .padding(top = 2.dp, end = 2.dp)
                    .size(18.dp)
                    .clip(CircleShape)
                    .background(BrandRed),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    if (unread > 9) "9+" else "$unread",
                    color = Color.White,
                    fontSize = 10.sp,
                    fontWeight = FontWeight.Bold,
                )
            }
        }
    }
}

/** Thẻ chấm công tối: ngày lớn + thứ/tháng, hàng Vào/Ra, nút "Chấm công ngay". */
@Composable
private fun CheckInCard(today: TimesheetDay?, onScan: () -> Unit) {
    val now = LocalDate.now()
    HeroContainer {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                "${now.dayOfMonth}",
                color = Color.White,
                fontSize = 44.sp,
                fontWeight = FontWeight.Bold,
            )
            Spacer(Modifier.width(14.dp))
            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(weekdayVi(now.dayOfWeek), color = Color.White, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                Text("Tháng ${now.monthValue} · ${now.year}", color = Color(0xFFB7C0CE), style = MaterialTheme.typography.bodyMedium)
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

private fun weekdayVi(d: DayOfWeek): String = when (d) {
    DayOfWeek.MONDAY -> "Thứ Hai"
    DayOfWeek.TUESDAY -> "Thứ Ba"
    DayOfWeek.WEDNESDAY -> "Thứ Tư"
    DayOfWeek.THURSDAY -> "Thứ Năm"
    DayOfWeek.FRIDAY -> "Thứ Sáu"
    DayOfWeek.SATURDAY -> "Thứ Bảy"
    DayOfWeek.SUNDAY -> "Chủ Nhật"
}

/**
 * Gộp danh sách thông báo để chạy hiệu ứng gõ chữ trên Trang chủ. Thứ tự ưu tiên: thông báo điều hành
 * (admin, một câu) → lời nhắc admin sửa từ xa ([serverNotices]) → thông báo nghiệp vụ chưa đọc → nhắc
 * chấm công → lời nhắc gắn sẵn. Lọc rỗng, khử trùng lặp. Lời chào theo buổi được ghép ở đầu dải tại
 * [HomeScreen]; luôn có ít nhất mấy lời nhắc gắn sẵn nên dải không bao giờ trống.
 */
private fun homeNotices(
    announcement: String,
    serverNotices: List<String>,
    notifications: List<AppNotification>,
    name: String,
    checkedIn: Boolean,
    checkedOut: Boolean,
    payslipRequirement: PayslipRequirement,
    nowVietnam: ZonedDateTime,
): List<String> {
    val out = ArrayList<String>()
    payslipReminderLine(payslipRequirement)?.let(out::add)
    announcement.trim().takeIf { it.isNotEmpty() }?.let { out.add(it) }
    serverNotices.forEach { it.trim().takeIf { s -> s.isNotEmpty() }?.let(out::add) }
    notifications.asSequence()
        .filter { !it.read }
        .take(5)
        .forEach { n ->
            val body = n.body.trim()
            val title = n.title.trim()
            val line = when {
                body.isNotEmpty() && title.isNotEmpty() && !body.startsWith(title) -> "$title — $body"
                body.isNotEmpty() -> body
                else -> title
            }
            if (line.isNotEmpty()) out.add(line)
        }
    when {
        !checkedIn -> out.add("Bạn chưa chấm công vào hôm nay. Đừng quên chấm công khi bắt đầu ca nhé!")
        checkedIn && !checkedOut -> out.add("Bạn đã vào ca. Nhớ chấm công ra khi tan làm nhé!")
    }
    out.addAll(builtInReminders(nowVietnam))
    return out.map { it.trim() }.filter { it.isNotEmpty() }.distinct()
}

/** Nội dung ưu tiên trên dải gõ chữ; hạn do server tính, app chỉ định dạng để tránh lệch múi giờ. */
internal fun payslipReminderLine(requirement: PayslipRequirement): String? {
    val item = requirement.payslip ?: return null
    if (requirement.pendingCount <= 0 || item.period.isBlank()) return null
    val periodLabel = runCatching {
        val parts = item.period.split("-")
        "tháng ${parts[1].toInt()}/${parts[0]}"
    }.getOrDefault("kỳ ${item.period}")
    val dueLabel = runCatching {
        OffsetDateTime.parse(item.acknowledgementDueAt)
            .atZoneSameInstant(ZoneId.of("Asia/Ho_Chi_Minh"))
            .format(DateTimeFormatter.ofPattern("HH:mm 'ngày' dd/MM/yyyy"))
    }.getOrDefault(item.acknowledgementDueAt)
    val more = if (requirement.pendingCount > 1) " Bạn còn ${requirement.pendingCount} phiếu chưa xác nhận." else ""
    return if (requirement.mustAcknowledge || item.overdue) {
        "⛔ Phiếu lương $periodLabel đã quá hạn xác nhận. Hãy mở phiếu, kiểm tra và bấm Xác nhận để tiếp tục sử dụng ứng dụng.$more"
    } else {
        "📄 Phiếu lương $periodLabel đã phát hành. Vui lòng xem và xác nhận trước $dueLabel.$more"
    }
}

/**
 * Phiếu lương chưa xác nhận luôn là câu đầu tiên của thanh gõ chữ. Nếu để lời chào đứng trước,
 * người dùng phải chờ hết cả chu kỳ gõ/giữ/xóa mới thấy nhắc lương và dễ tưởng là không có.
 */
internal fun prioritizedHomeTickerMessages(
    greeting: String,
    notices: List<String>,
    requirement: PayslipRequirement,
): List<String> {
    val reminder = payslipReminderLine(requirement)
    return buildList {
        if (reminder != null) add(reminder)
        add(greeting)
        addAll(notices)
    }.map(String::trim).filter(String::isNotEmpty).distinct()
}

/**
 * Lời nhắc GẮN SẴN trong app (không cần server): vài câu hữu ích luân phiên để dải luôn phong phú, kèm
 * một câu theo THỨ trong tuần (tính từ GIỜ MÁY CHỦ). Admin có thể thêm lời nhắc riêng qua trang Hệ thống
 * ([AppConfig.notices]); hai nguồn được gộp & khử trùng lặp ở [homeNotices].
 */
private fun builtInReminders(nowVietnam: ZonedDateTime): List<String> {
    val out = ArrayList<String>()
    weekendGreeting(nowVietnam)?.let(out::add)
    when (nowVietnam.dayOfWeek) {
        DayOfWeek.MONDAY -> out.add("Chào tuần mới! Lên kế hoạch cho một tuần suôn sẻ nào 🚀")
        DayOfWeek.FRIDAY -> out.add("Sắp hết tuần rồi — cố lên và nhớ tổng kết công việc nhé 💪")
        else -> {}
    }
    out.add("Nghỉ mắt và uống đủ nước sau mỗi giờ làm để giữ sức khoẻ 💧")
    out.add("Kiểm tra danh sách công việc được giao cho bạn hôm nay 📋")
    out.add("Đơn từ (nghỉ phép, tạm ứng…) nên nộp sớm để được duyệt kịp thời 📝")
    return out
}

/**
 * Lời chào theo buổi trong ngày (sáng/trưa/chiều/tối) tính từ GIỜ MÁY CHỦ ([ServerClock]) quy về múi
 * giờ Việt Nam — không lệ thuộc đồng hồ máy có thể bị chỉnh sai. Chưa đồng bộ được giờ máy chủ (mới mở
 * app/offline) thì tạm dùng giờ máy.
 */
internal fun weekendGreeting(nowVietnam: ZonedDateTime): String? {
    val isWeekend = nowVietnam.dayOfWeek == DayOfWeek.SUNDAY ||
        (nowVietnam.dayOfWeek == DayOfWeek.SATURDAY && nowVietnam.hour >= 12)
    if (!isWeekend) return null
    val lines = listOf(
        "Cuối tuần rồi — chúc bạn có thời gian thư giãn, vui vẻ bên gia đình 🌿",
        "Chúc bạn một cuối tuần thật nhiều niềm vui và năng lượng tích cực ☀️",
        "Tạm gác công việc, tận hưởng cuối tuần và nạp lại năng lượng nhé 😊",
    )
    return lines[nowVietnam.dayOfYear % lines.size]
}

private fun timeGreetingLine(name: String, nowVietnam: ZonedDateTime): String {
    val who = name.trim().ifEmpty { "bạn" }
    val (part, emoji) = when (nowVietnam.hour) {
        in 5..10 -> "buổi sáng" to "☀️"
        in 11..12 -> "buổi trưa" to "🌤️"
        in 13..17 -> "buổi chiều" to "🌇"
        else -> "buổi tối" to "🌙"
    }
    return "Chào $part, $who $emoji"
}

/**
 * Dải thông báo động với hiệu ứng máy đánh chữ: gõ từng ký tự → giữ → xoá dần → thông báo kế tiếp,
 * lặp vô hạn. Chiều cao thẻ cố định để nội dung dài/ngắn không làm các thẻ dưới nhảy vị trí; chữ luôn
 * căn giữa cả chiều ngang lẫn chiều dọc. Hiệu ứng tự dừng khi rời màn/nền và chạy lại khi quay lại,
 * không rò rỉ nhờ gắn với vòng đời & phạm vi hợp thành.
 */
@Composable
private fun NotificationTickerCard(
    messages: List<String>,
    onClick: (() -> Unit)? = null,
    typeDelayMs: Long = 48L,
    deleteDelayMs: Long = 26L,
    holdMs: Long = 4000L,
    pauseMs: Long = 400L,
) {
    // Tách theo cụm ký tự hiển thị (grapheme) để gõ đúng dấu tiếng Việt, emoji, ký tự ghép.
    val clusters = remember(messages) {
        messages.map { it.trim() }.filter { it.isNotEmpty() }.distinct().map { splitGraphemes(it) }
    }
    if (clusters.isEmpty()) return

    var shown by remember(clusters) { mutableStateOf("") }
    val lifecycleOwner = LocalLifecycleOwner.current
    LaunchedEffect(clusters, lifecycleOwner) {
        // repeatOnLifecycle(RESUMED): dừng khi màn nền/tắt, tự chạy lại khi quay về foreground.
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
            var index = 0
            while (true) {
                val g = clusters[index % clusters.size]
                for (i in 1..g.size) {
                    shown = g.subList(0, i).joinToString("")
                    delay(typeDelayMs)
                }
                delay(holdMs)
                for (i in g.size - 1 downTo 0) {
                    shown = g.subList(0, i).joinToString("")
                    delay(deleteDelayMs)
                }
                delay(pauseMs)
                index++
            }
        }
    }

    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier),
        shape = RoundedCornerShape(20.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
        shadowElevation = 1.dp,
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 88.dp)
                .padding(horizontal = 14.dp, vertical = 12.dp),
            contentAlignment = Alignment.Center,
        ) {
            Box(
                modifier = Modifier
                    .align(Alignment.CenterStart)
                    .size(34.dp)
                    .clip(CircleShape)
                    .background(InfoBlue.copy(alpha = 0.12f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Filled.Info, contentDescription = null, tint = InfoBlue, modifier = Modifier.size(20.dp))
            }
            Text(
                text = shown,
                modifier = Modifier
                    .align(Alignment.Center)
                    .padding(horizontal = 40.dp),
                style = MaterialTheme.typography.titleSmall,
                color = MaterialTheme.colorScheme.onSurface,
                textAlign = TextAlign.Center,
                maxLines = 3,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

/** Chia chuỗi thành các cụm ký tự hiển thị (grapheme cluster) để gõ/xoá đúng emoji & dấu tiếng Việt. */
private fun splitGraphemes(s: String): List<String> {
    val it = java.text.BreakIterator.getCharacterInstance()
    it.setText(s)
    val out = ArrayList<String>()
    var start = it.first()
    var end = it.next()
    while (end != java.text.BreakIterator.DONE) {
        out.add(s.substring(start, end))
        start = end
        end = it.next()
    }
    return out
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

/** Tất cả tác vụ trong app, có chế độ chỉnh sửa và lưu thứ tự ngay trên thiết bị. */
@Composable
private fun ActionsCard(
    actions: List<HrDestination>,
    badgeCount: (HrDestination) -> Int,
    onSelect: (HrDestination) -> Unit,
) {
    var editing by rememberSaveable { mutableStateOf(false) }
    val savedOrder = AppPersonalization.homeActionOrder
    val ordered = remember(actions, savedOrder) { orderHomeActions(actions, savedOrder) }

    fun saveVisibleOrder(next: List<HrDestination>) {
        val visibleNames = next.map { it.name }
        // Giữ lại thứ tự các tác vụ thuộc quyền khác để đổi tài khoản không làm mất cấu hình cũ.
        val hiddenNames = savedOrder.filterNot(visibleNames::contains)
        AppPersonalization.updateHomeActionOrder(visibleNames + hiddenNames)
    }

    HrCard {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text("Tác vụ", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, modifier = Modifier.weight(1f))
            TextButton(onClick = { editing = !editing }) {
                Icon(if (editing) Icons.Filled.Done else Icons.Filled.Edit, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(6.dp))
                Text(if (editing) "Xong" else "Sắp xếp", fontWeight = FontWeight.Bold)
            }
        }
        if (editing) {
            Text(
                "Dùng mũi tên trên từng tác vụ để đổi vị trí. Thứ tự được tự động lưu.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        ordered.chunked(4).forEachIndexed { rowIndex, rowActions ->
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                rowActions.forEachIndexed { columnIndex, destination ->
                    val index = rowIndex * 4 + columnIndex
                    ActionTile(
                        destination = destination,
                        accent = homeActionColor(destination),
                        badge = badgeCount(destination),
                        editing = editing,
                        canMoveBack = index > 0,
                        canMoveForward = index < ordered.lastIndex,
                        modifier = Modifier.weight(1f),
                        onMoveBack = {
                            saveVisibleOrder(ordered.toMutableList().apply {
                                add(index - 1, removeAt(index))
                            })
                        },
                        onMoveForward = {
                            saveVisibleOrder(ordered.toMutableList().apply {
                                add(index + 1, removeAt(index))
                            })
                        },
                        onClick = { onSelect(destination) },
                    )
                }
                repeat(4 - rowActions.size) { Spacer(Modifier.weight(1f)) }
            }
        }
        if (editing && savedOrder.isNotEmpty()) {
            TextButton(
                onClick = { AppPersonalization.updateHomeActionOrder(emptyList()) },
                modifier = Modifier.align(Alignment.End),
            ) { Text("Khôi phục mặc định") }
        }
    }
}

@Composable
private fun ActionTile(
    destination: HrDestination,
    accent: Color,
    badge: Int,
    editing: Boolean,
    canMoveBack: Boolean,
    canMoveForward: Boolean,
    modifier: Modifier = Modifier,
    onMoveBack: () -> Unit,
    onMoveForward: () -> Unit,
    onClick: () -> Unit,
) {
    Column(
        modifier = modifier
            .clip(RoundedCornerShape(14.dp))
            .clickable(enabled = !editing, onClick = onClick)
            .padding(vertical = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Box(
            modifier = Modifier
                .size(52.dp)
                .clip(RoundedCornerShape(16.dp))
                .background(accent.copy(alpha = 0.14f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(destination.icon, contentDescription = null, tint = accent, modifier = Modifier.size(26.dp))
            if (badge > 0) {
                Box(
                    modifier = Modifier
                        .align(Alignment.TopEnd)
                        .size(19.dp)
                        .clip(CircleShape)
                        .background(MaterialTheme.colorScheme.error),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        if (badge > 9) "9+" else badge.toString(),
                        color = MaterialTheme.colorScheme.onError,
                        fontSize = 9.sp,
                        fontWeight = FontWeight.Bold,
                    )
                }
            }
        }
        Text(homeActionLabel(destination), style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 2, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center)
        if (editing) {
            Row(horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                IconButton(onClick = onMoveBack, enabled = canMoveBack, modifier = Modifier.size(36.dp)) {
                    Icon(Icons.AutoMirrored.Filled.KeyboardArrowLeft, contentDescription = "Đưa ${destination.title} về trước", modifier = Modifier.size(20.dp))
                }
                IconButton(onClick = onMoveForward, enabled = canMoveForward, modifier = Modifier.size(36.dp)) {
                    Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = "Đưa ${destination.title} ra sau", modifier = Modifier.size(20.dp))
                }
            }
        }
    }
}

internal fun orderHomeActions(actions: List<HrDestination>, savedOrder: List<String>): List<HrDestination> {
    val rank = savedOrder.withIndex().associate { it.value to it.index }
    val fallback = actions.withIndex().associate { it.value to it.index }
    return actions.sortedWith(compareBy({ rank[it.name] ?: Int.MAX_VALUE }, { fallback[it] ?: Int.MAX_VALUE }))
}

private fun homeActionLabel(destination: HrDestination): String = when (destination) {
    HrDestination.Requests -> "Tạo đơn"
    HrDestination.Tasks -> "Công việc"
    HrDestination.MyPayslips -> "Phiếu lương"
    else -> destination.label
}

private fun homeActionColor(destination: HrDestination): Color = when (destination) {
    HrDestination.Scan, HrDestination.Requests, HrDestination.Penalty -> BrandRed
    HrDestination.Timesheet, HrDestination.MyPayslips, HrDestination.Payroll -> Success
    HrDestination.Tasks, HrDestination.Approval, HrDestination.Audit -> Warning
    else -> InfoBlue
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

/** Bỏ đuôi ".0" cho số ngày công (18.0 → 18, 18.5 → 18.5). */
private fun trimNum(v: Double): String =
    if (v == v.toLong().toDouble()) v.toLong().toString() else v.toString()

private fun weekRangeLabel(monday: LocalDate): String {
    val fmt = DateTimeFormatter.ofPattern("dd/MM")
    return "${monday.format(fmt)} - ${monday.plusDays(6).format(fmt)}"
}

@Composable
fun ProfileScreen(state: HomeUiState, onCapturePortrait: () -> Unit = {}) {
    val e = state.employee
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = screenPadding(16.dp, 16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        if (e == null) {
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
                    avatar = e.avatar,
                    onCapturePortrait = onCapturePortrait,
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
            if (e.avatar.isNullOrBlank()) {
                item {
                    HrCard {
                        Text(
                            "Bạn chưa có ảnh chân dung",
                            style = MaterialTheme.typography.titleSmall,
                            color = MaterialTheme.colorScheme.onSurface,
                            fontWeight = FontWeight.Bold,
                        )
                        Spacer(Modifier.height(6.dp))
                        Text(
                            "Mỗi nhân viên cần chụp một ảnh chân dung. Đưa mặt vào giữa khung và giữ yên, ứng dụng sẽ tự chụp.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        Spacer(Modifier.height(12.dp))
                        Button(
                            onClick = onCapturePortrait,
                            modifier = Modifier.fillMaxWidth().height(48.dp),
                            shape = RoundedCornerShape(14.dp),
                        ) {
                            Icon(Icons.Filled.CameraAlt, contentDescription = null, modifier = Modifier.size(20.dp))
                            Spacer(Modifier.width(8.dp))
                            Text("Chụp ảnh chân dung", fontWeight = FontWeight.Bold)
                        }
                    }
                }
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
    payEstimate: PayEstimateUiState,
    dayLog: DayLogUiState,
    username: String,
    onMonthOffset: (Int) -> Unit,
    onSelectMonth: (String) -> Unit,
    onSelectDay: (String?) -> Unit,
    onShiftSwap: (String?) -> Unit,
    onForgotCheckin: (String?) -> Unit,
    onLoadSalary: () -> Unit,
) {
    val period = state.month.take(7)
    val ts = state.timesheet?.takeIf { it.period.take(7) == period }
    var selectedDate by rememberSaveable(period) { mutableStateOf<String?>(null) }
    var pickerOpen by rememberSaveable { mutableStateOf(false) }
    var weekMode by rememberSaveable { mutableStateOf(false) }
    // Lương che sẵn; chỉ hiện sau khi xác thực PIN/vân tay. Dùng remember (không saveable) để rời tab
    // là che lại ngay — tránh lộ lương khi người khác cầm máy.
    var salaryRevealed by remember { mutableStateOf(false) }
    var salaryPinOpen by remember { mutableStateOf(false) }
    val currentStart = timesheetMonthStart(period)
    // Chọn ô ngày nào thì kéo nhật ký của đúng ngày đó về (việc đã làm, phạt, ứng tiền, phiếu chi).
    LaunchedEffect(selectedDate) { onSelectDay(selectedDate) }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = screenPadding(),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHeader(Icons.Filled.CalendarMonth, "Bảng công", formatTimesheetPeriod(period), Tone.Neutral) }
        timesheetSalarySection(
            state = payEstimate,
            revealed = salaryRevealed,
            onRequestReveal = { salaryPinOpen = true },
            onHide = { salaryRevealed = false },
        )
        val daysByDate = ts?.days?.associateBy { it.date.take(10) }.orEmpty()
        item {
            TimesheetCalendarCard(
                period = period,
                daysByDate = daysByDate,
                daysForPeriod = { key ->
                    state.neighbors[key]?.days?.associateBy { it.date.take(10) }.orEmpty()
                },
                selectedDate = selectedDate,
                weekOnly = weekMode,
                loading = state.loading,
                onSelectDate = { selectedDate = it },
                onMonthOffset = onMonthOffset,
                onPrev = { onMonthOffset(-1) },
                onNext = { onMonthOffset(1) },
                onPick = { pickerOpen = true },
                onWeekModeChange = { weekMode = it },
            )
        }
        if (ts == null) {
            if (!state.loading && state.error != null) {
                item { EmptyState("Không tải được bảng công", state.error) }
            }
        } else {
            val selectedDay = selectedDate?.let { daysByDate[it] }
            if (selectedDate != null) {
                item { TimesheetDayDetailCard(selectedDate.orEmpty(), selectedDay, onShiftSwap, onForgotCheckin) }
                item { TimesheetDayLogCard(selectedDate.orEmpty(), dayLog) }
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

    // Xác thực để bỏ che phần lương (giống mở phiếu lương). Thành công → hiện + tải lại số liệu mới nhất.
    AppPinGate(
        visible = salaryPinOpen,
        username = username,
        purpose = "Xác thực để xem lương của bạn.",
        onDismiss = { salaryPinOpen = false },
        onUnlocked = {
            salaryPinOpen = false
            salaryRevealed = true
            onLoadSalary()
        },
    )
}

/**
 * Phần "Lương của tôi" nhúng trong tab Bảng công. Khi CHƯA mở, nó chỉ là MỘT THANH MỎNG một dòng —
 * bảng công mới là nội dung chính của màn này, không việc gì để một thẻ lương che mất nửa màn hình.
 * Bấm con mắt + xác thực PIN/vân tay mới bung đủ chi tiết (thực nhận, ngày công, khoản cộng/trừ).
 */
private fun LazyListScope.timesheetSalarySection(
    state: PayEstimateUiState,
    revealed: Boolean,
    onRequestReveal: () -> Unit,
    onHide: () -> Unit,
) {
    val est = state.data
    item {
        Surface(
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(14.dp),
            color = MaterialTheme.colorScheme.surface,
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
        ) {
            Row(
                modifier = Modifier.padding(start = 12.dp, end = 4.dp, top = 4.dp, bottom = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Icon(Icons.Filled.Payments, contentDescription = null, tint = Success, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(8.dp))
                Text(
                    "Lương của tôi",
                    style = MaterialTheme.typography.bodyMedium,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface,
                )
                Spacer(Modifier.weight(1f))
                Text(
                    if (revealed && est != null) formatMoney(est.netPay) else "•••••••",
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.ExtraBold,
                    color = MaterialTheme.colorScheme.primary,
                    maxLines = 1,
                )
                IconButton(onClick = { if (revealed) onHide() else onRequestReveal() }, modifier = Modifier.size(40.dp)) {
                    Icon(
                        if (revealed) Icons.Filled.VisibilityOff else Icons.Filled.Visibility,
                        contentDescription = if (revealed) "Ẩn lương" else "Xem lương",
                        tint = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(20.dp),
                    )
                }
            }
        }
    }
    if (!revealed) return

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
                Text(
                    "Thực nhận dự tính · ${formatTimesheetPeriod(est.period)}",
                    style = MaterialTheme.typography.titleSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
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

/**
 * Thanh chọn tháng: hai mũi tên ở hai đầu (lùi/tiến tháng), ở giữa hiện tháng đang xem
 * (vd "Tháng 7/2026") — bấm vào giữa để mở bộ chọn tháng/năm.
 */
@Composable
private fun MonthSelectorBar(
    basePeriod: String,
    dragFraction: Float,
    weekOnly: Boolean,
    selectedDate: String?,
    onPrev: () -> Unit,
    onNext: () -> Unit,
    onPick: () -> Unit,
) {
    var labelWidthPx by remember { mutableFloatStateOf(0f) }
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
            Box(
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(12.dp))
                    .clickable(onClick = onPick)
                    .clipToBounds()
                    .height(44.dp)
                    .onSizeChanged { labelWidthPx = it.width.toFloat() },
                contentAlignment = Alignment.Center,
            ) {
                listOf(-1, 0, 1).forEach { slot ->
                    val slotPeriod = shiftTimesheetPeriod(basePeriod, slot)
                    val distance = (dragFraction + slot).absoluteValue.coerceIn(0f, 1f)
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .graphicsLayer {
                                translationX = (dragFraction + slot) * labelWidthPx
                                alpha = 1f - distance * 0.55f
                                val scale = 1f - distance * 0.10f
                                scaleX = scale
                                scaleY = scale
                            },
                        horizontalArrangement = Arrangement.Center,
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Text(
                            if (weekOnly) timesheetWeekPeriodLabel(slotPeriod, selectedDate)
                            else formatTimesheetPeriod(slotPeriod),
                            style = MaterialTheme.typography.titleMedium,
                            color = MaterialTheme.colorScheme.onSurface,
                            fontWeight = FontWeight.Bold,
                            maxLines = 1,
                        )
                        Spacer(Modifier.width(4.dp))
                        Icon(
                            Icons.Filled.ArrowDropDown,
                            contentDescription = "Chọn tháng/năm",
                            tint = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
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
    daysForPeriod: (String) -> Map<String, TimesheetDay>,
    selectedDate: String?,
    weekOnly: Boolean,
    loading: Boolean,
    onSelectDate: (String) -> Unit,
    onMonthOffset: (Int) -> Unit,
    onPrev: () -> Unit,
    onNext: () -> Unit,
    onPick: () -> Unit,
    onWeekModeChange: (Boolean) -> Unit,
) {
    // Băng chuyền 3 trang: tháng trước | tháng đang xem | tháng sau. Kéo tới đâu thấy tháng liền kề tới đó.
    var pagerWidthPx by remember { mutableFloatStateOf(0f) }
    val pageGapPx = with(LocalDensity.current) { 12.dp.toPx() }
    val dragOffset = remember { Animatable(0f) }
    var basePeriod by remember { mutableStateOf(period) }
    val minimumSwipePx = with(LocalDensity.current) { 56.dp.toPx() }
    val scope = rememberCoroutineScope()
    val pageStridePx = pagerWidthPx + pageGapPx

    // Đổi tháng bằng nút mũi tên / bộ chọn tháng: cho tháng mới trượt vào từ phía tương ứng.
    // (Vuốt tay thì trang kề đã nằm đúng chỗ, xử lý ngay trong onDragStopped nên nhánh này bỏ qua.)
    LaunchedEffect(period) {
        val from = basePeriod
        if (period == from) return@LaunchedEffect
        basePeriod = period
        if (pageStridePx <= 0f) {
            dragOffset.snapTo(0f)
        } else {
            dragOffset.snapTo(if (period > from) pageStridePx else -pageStridePx)
            dragOffset.animateTo(0f, tween(durationMillis = 220, easing = FastOutSlowInEasing))
        }
    }

    val dragState = rememberDraggableState { delta ->
        if (pageStridePx > 0f) {
            scope.launch {
                dragOffset.snapTo((dragOffset.value + delta).coerceIn(-pageStridePx, pageStridePx))
            }
        }
    }

    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        MonthSelectorBar(
            basePeriod = basePeriod,
            dragFraction = if (pageStridePx > 0f) (dragOffset.value / pageStridePx).coerceIn(-1f, 1f) else 0f,
            weekOnly = weekOnly,
            selectedDate = selectedDate,
            onPrev = onPrev,
            onNext = onNext,
            onPick = onPick,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
            if (weekOnly) OutlinedButton({ onWeekModeChange(false) }, Modifier.weight(1f)) { Text("Tháng") }
            else Button({ onWeekModeChange(false) }, Modifier.weight(1f)) { Text("Tháng") }
            if (weekOnly) Button({ onWeekModeChange(true) }, Modifier.weight(1f)) { Text("Tuần") }
            else OutlinedButton({ onWeekModeChange(true) }, Modifier.weight(1f)) { Text("Tuần") }
        }
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .clipToBounds()
                .testTag("timesheet-calendar")
                .onSizeChanged { pagerWidthPx = it.width.toFloat() }
                .draggable(
                    state = dragState,
                    orientation = Orientation.Horizontal,
                    onDragStopped = { velocity ->
                        val monthOffset = timesheetMonthOffsetForSwipe(
                            dragDistancePx = dragOffset.value,
                            thresholdPx = maxOf(minimumSwipePx, pagerWidthPx * 0.16f),
                        )
                        if (monthOffset == null) {
                            dragOffset.animateTo(
                                targetValue = 0f,
                                initialVelocity = velocity,
                                animationSpec = spring(dampingRatio = 0.85f, stiffness = 520f),
                            )
                        } else {
                            // Lịch và tiêu đề kỳ dùng chung dragOffset nên hoàn tất chuyển trang cùng lúc.
                            dragOffset.animateTo(
                                targetValue = if (monthOffset > 0) -pageStridePx else pageStridePx,
                                initialVelocity = velocity,
                                animationSpec = spring(dampingRatio = 0.9f, stiffness = 420f),
                            )
                            basePeriod = shiftTimesheetPeriod(basePeriod, monthOffset)
                            dragOffset.snapTo(0f)
                            onMonthOffset(monthOffset)
                        }
                    },
                ),
        ) {
            listOf(-1, 0, 1).forEach { slot ->
                val slotPeriod = shiftTimesheetPeriod(basePeriod, slot)
                val slotDays = if (slot == 0) daysByDate else daysForPeriod(slotPeriod)
                TimesheetCalendarPage(
                    period = slotPeriod,
                    daysByDate = slotDays,
                    selectedDate = if (slot == 0) selectedDate else null,
                    weekOnly = weekOnly,
                    loading = loading && slot == 0,
                    current = slot == 0,
                    onSelectDate = onSelectDate,
                    modifier = Modifier.graphicsLayer {
                        val stride = size.width + pageGapPx
                        translationX = dragOffset.value + slot * stride
                        // Trang càng xa giữa càng thu nhỏ + mờ đi: kéo tới đâu tháng kề "phóng to" vào tới đó.
                        val distance = if (stride > 0f) (translationX / stride).absoluteValue.coerceIn(0f, 1f) else 1f
                        val scale = 1f - 0.25f * distance
                        scaleX = scale
                        scaleY = scale
                        alpha = 1f - 0.45f * distance
                    },
                )
            }
        }
    }
}

private fun timesheetWeekPeriodLabel(period: String, selectedDate: String?): String {
    val monthStart = timesheetMonthStart(period)
    val anchor = selectedDate
        ?.takeIf { it.startsWith(period.take(7)) }
        ?.let { runCatching { LocalDate.parse(it) }.getOrNull() }
        ?: LocalDate.now().takeIf { it.toString().startsWith(period.take(7)) }
        ?: monthStart
    val monday = anchor.minusDays((anchor.dayOfWeek.value - 1).toLong())
    val sunday = monday.plusDays(6)
    val start = monday.format(DateTimeFormatter.ofPattern("dd/MM"))
    val end = sunday.format(DateTimeFormatter.ofPattern("dd/MM/yyyy"))
    return "Tuần $start – $end"
}

/** Một trang lịch (một tháng) trong băng chuyền vuốt. */
@Composable
private fun TimesheetCalendarPage(
    period: String,
    daysByDate: Map<String, TimesheetDay>,
    selectedDate: String?,
    weekOnly: Boolean,
    loading: Boolean,
    current: Boolean,
    onSelectDate: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val allRows = timesheetCalendarCells(period).chunked(7)
    val anchor = selectedDate ?: LocalDate.now().takeIf { it.toString().startsWith(period.take(7)) }?.toString()
    val rows = if (weekOnly) allRows.filter { week -> week.any { it.dateKey == anchor } }.ifEmpty { allRows.take(1) } else allRows

    HrCard(modifier = modifier.fillMaxWidth()) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text("Lịch tháng", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
                Text(
                    "Chọn một ngày để mở chi tiết. Vuốt trái/phải để đổi tháng.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            StatusChip(formatTimesheetPeriod(period), Tone.Neutral)
        }

        if (loading) {
            LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
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
                                // Trang kề bên chỉ để xem trước, bấm vào không mở chi tiết nhầm tháng.
                                onClick = if (current) ({ onSelectDate(cell.dateKey) }) else ({}),
                            )
                        }
                    }
                }
            }
        }

        if (loading && daysByDate.isEmpty()) {
            Text(
                "Đang tải dữ liệu tháng này…",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        } else if (daysByDate.isEmpty()) {
            Text(
                if (current) "Tháng này chưa có chấm công hoặc phân ca. Các ô sẽ đổi màu khi có dữ liệu."
                else "Thả tay để mở ${formatTimesheetPeriod(period).lowercase()}.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

/** Dịch khóa tháng "yyyy-MM" đi `offset` tháng. */
private fun shiftTimesheetPeriod(period: String, offset: Int): String =
    timesheetMonthStart(period).plusMonths(offset.toLong()).toString().take(7)

/** Vuốt trái mở tháng sau, vuốt phải quay về tháng trước. */
internal fun timesheetMonthOffsetForSwipe(dragDistancePx: Float, thresholdPx: Float): Int? = when {
    dragDistancePx <= -thresholdPx -> 1
    dragDistancePx >= thresholdPx -> -1
    else -> null
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
private fun TimesheetDayDetailCard(
    dateKey: String,
    day: TimesheetDay?,
    onShiftSwap: (String?) -> Unit,
    onForgotCheckin: (String?) -> Unit,
) {
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
                "Ngày này chưa có dữ liệu chấm công. Nếu bạn có đi làm, bấm \"Báo quên chấm\" để được bù công.",
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
            if (day.shiftStart.isNotBlank() || day.shiftEnd.isNotBlank()) {
                TimesheetDetailMetric("Khung giờ ca", "${day.shiftStart.ifBlank { "--:--" }} – ${day.shiftEnd.ifBlank { "--:--" }}")
            }
        }
        // Thao tác cho NGÀY ĐANG CHỌN — luôn hiện (kể cả ngày chưa có log, để còn báo quên chấm).
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
            OutlinedButton(
                onClick = { onShiftSwap(dateKey) },
                modifier = Modifier.weight(1f),
                contentPadding = PaddingValues(horizontal = 6.dp),
            ) { Text("Đổi / nhận ca", maxLines = 1) }
            Button(
                onClick = { onForgotCheckin(dateKey) },
                modifier = Modifier.weight(1f),
                contentPadding = PaddingValues(horizontal = 6.dp),
            ) { Text("Báo quên chấm", maxLines = 1) }
        }
    }
}

/**
 * NHẬT KÝ NGÀY ĐANG CHỌN — phần "chuyện gì đã xảy ra hôm đó" bên cạnh giờ vào/ra: đã làm những việc
 * gì (lấy từ Việc cần làm), có bị phạt/kỷ luật không, xin ứng tiền hay được kế toán chi tiền không.
 * Mọi mốc đều ghi đủ ngày/tháng/giờ/phút + trạng thái để đối chiếu khi thắc mắc lương.
 */
@Composable
private fun TimesheetDayLogCard(dateKey: String, state: DayLogUiState) {
    val log = state.data?.takeIf { it.date == dateKey }
    HrCard {
        CardHeader("Nhật ký ngày ${formatIsoDate(dateKey)}")
        when {
            state.loading && log == null -> LoadingBlock()
            state.error != null && log == null -> ErrorText(state.error)
            log == null || log.isEmpty -> Text(
                "Ngày này không có việc, án phạt, đơn ứng tiền hay phiếu chi nào được ghi nhận.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            else -> {
                if (log.tasks.isNotEmpty()) {
                    DayLogGroup("Công việc") {
                        log.tasks.forEach { task ->
                            DayLogRow(
                                time = formatIsoDateTimeFull(task.at),
                                title = "${task.taskNo} · ${task.title}",
                                subtitle = listOf(
                                    task.kindLabel.replaceFirstChar(Char::uppercase),
                                    task.note,
                                ).filter { it.isNotBlank() }.joinToString(" — "),
                                status = task.statusLabel,
                                tone = dayLogTaskTone(task.status),
                            )
                        }
                    }
                }
                if (log.penalties.isNotEmpty()) {
                    DayLogGroup("Phạt / kỷ luật") {
                        log.penalties.forEach { penalty ->
                            DayLogRow(
                                time = formatIsoDateTimeFull(penalty.at),
                                title = "${penalty.code} · ${penalty.typeLabel}" +
                                    if (penalty.amount > 0) " · ${formatMoney(penalty.amount)}" else "",
                                subtitle = listOf(
                                    "Ngày vi phạm ${formatIsoDate(penalty.penaltyDate)}",
                                    penalty.reason,
                                ).filter { it.isNotBlank() }.joinToString(" — "),
                                status = penalty.statusLabel,
                                tone = if (penalty.status == "Active") Tone.Danger else Tone.Muted,
                            )
                        }
                    }
                }
                if (log.requests.isNotEmpty()) {
                    DayLogGroup("Ứng tiền & đề nghị thanh toán") {
                        log.requests.forEach { request ->
                            DayLogRow(
                                time = formatIsoDateTimeFull(request.at),
                                title = "${request.code} · ${request.typeLabel}" +
                                    if (request.amount > 0) " · ${formatMoney(request.amount)}" else "",
                                subtitle = request.title,
                                status = request.statusLabel,
                                tone = dayLogRequestTone(request.status),
                                steps = request.steps,
                            )
                        }
                    }
                }
                if (log.payouts.isNotEmpty()) {
                    DayLogGroup("Kế toán chi tiền") {
                        log.payouts.forEach { payout ->
                            DayLogRow(
                                time = formatIsoDateTimeFull(payout.at),
                                title = "${payout.code} · ${formatMoney(payout.amount)}" +
                                    if (payout.category.isNotBlank()) " · ${payout.category}" else "",
                                subtitle = payout.reason,
                                status = payout.statusLabel,
                                tone = dayLogPayoutTone(payout.status),
                                steps = payout.steps,
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun DayLogGroup(title: String, content: @Composable ColumnScope.() -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.fillMaxWidth()) {
        Text(
            title,
            style = MaterialTheme.typography.labelLarge,
            fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        content()
    }
}

/** Một dòng nhật ký: giờ phút đầy đủ + nội dung + trạng thái, kèm các mốc duyệt/chi nếu có. */
@Composable
private fun DayLogRow(
    time: String,
    title: String,
    subtitle: String,
    status: String,
    tone: Tone,
    steps: List<DayLogStep> = emptyList(),
) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(12.dp),
        color = toneColor(tone).copy(alpha = 0.07f),
    ) {
        Column(modifier = Modifier.padding(10.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(time, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Spacer(Modifier.weight(1f))
                if (status.isNotBlank()) StatusChip(status, tone)
            }
            Text(title, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
            if (subtitle.isNotBlank()) {
                Text(subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            steps.filter { !it.at.isNullOrBlank() }.forEach { step ->
                Text(
                    "• ${step.label}: ${formatIsoDateTimeFull(step.at)}" +
                        (step.statusLabel.takeIf { it.isNotBlank() }?.let { " · $it" } ?: "") +
                        (step.by.takeIf { it.isNotBlank() }?.let { " · $it" } ?: "") +
                        (step.note.takeIf { it.isNotBlank() }?.let { " — $it" } ?: ""),
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

private fun dayLogTaskTone(status: String): Tone = when (status) {
    "accepted", "completed" -> Tone.Success
    "rejected" -> Tone.Danger
    "submitted" -> Tone.Warning
    "cancelled" -> Tone.Muted
    else -> Tone.Info
}

private fun dayLogRequestTone(status: String): Tone = when (status) {
    "Approved" -> Tone.Success
    "Rejected" -> Tone.Danger
    "Cancelled" -> Tone.Muted
    else -> Tone.Warning
}

private fun dayLogPayoutTone(status: String): Tone = when (status) {
    "Paid" -> Tone.Success
    "Rejected", "Cancelled" -> Tone.Danger
    "Approved", "Confirmed" -> Tone.Info
    else -> Tone.Warning
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

internal enum class TimesheetCalendarTone { Worked, Leave, Business, Absent, Overtime, Warning, Holiday, Off, Empty }

private data class TimesheetCalendarCell(
    val key: String,
    val day: Int? = null,
    val dateKey: String? = null,
)

private data class TimesheetLegendItem(val tone: TimesheetCalendarTone, val label: String)

private val timesheetLegendItems = listOf(
    TimesheetLegendItem(TimesheetCalendarTone.Worked, "Đi làm"),
    TimesheetLegendItem(TimesheetCalendarTone.Leave, "Nghỉ phép"),
    TimesheetLegendItem(TimesheetCalendarTone.Business, "Công tác"),
    TimesheetLegendItem(TimesheetCalendarTone.Overtime, "Tăng ca"),
    TimesheetLegendItem(TimesheetCalendarTone.Absent, "Vắng"),
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

internal fun formatTimesheetPeriod(period: String?): String {
    if (period.isNullOrBlank()) return "Tháng hiện tại"
    return runCatching {
        val start = timesheetMonthStart(period)
        "Tháng ${start.monthValue.toString().padStart(2, '0')}/${start.year}"
    }.getOrElse { period }
}

internal fun timesheetCalendarTone(day: TimesheetDay?): TimesheetCalendarTone {
    if (day == null) return TimesheetCalendarTone.Empty
    if (day.eventType == "leave") return TimesheetCalendarTone.Leave
    if (day.eventType == "business_trip") return TimesheetCalendarTone.Business
    if (day.eventType == "overtime") return TimesheetCalendarTone.Overtime
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

internal fun timesheetCalendarLabel(day: TimesheetDay?): String = when (timesheetCalendarTone(day)) {
    TimesheetCalendarTone.Worked -> "Đi làm"
    TimesheetCalendarTone.Leave -> "Nghỉ phép"
    TimesheetCalendarTone.Business -> "Công tác"
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
    TimesheetCalendarTone.Leave -> Color(0xFF2563EB)
    TimesheetCalendarTone.Business -> Color(0xFF0D9488)
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
        contentPadding = screenPadding(),
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

// ── Phiếu lương của tôi ───────────────────────────────────────────────────────
/**
 * Kho phiếu lương đã phát hành của chính nhân viên. Danh sách chỉ hiện kỳ và trạng thái; toàn bộ số
 * tiền được đặt sau AppPinGate để người đứng cạnh không đọc được lương từ màn hình danh sách.
 */
@Composable
fun MyPayslipsScreen(
    state: PayslipsUiState,
    openPeriod: String?,
    username: String,
    onOpen: (String) -> Unit,
    onClose: () -> Unit,
    onOpenConfirmation: (String) -> Unit,
    onInquiry: (String, String, String) -> Unit,
    onDownload: (PayslipItem) -> Unit,
) {
    var pendingPeriod by remember { mutableStateOf<String?>(null) }
    val opened = openPeriod?.let { p -> state.items.find { it.period == p } }
    // Đang mở chi tiết một phiếu → Back lùi về danh sách thẻ tháng trước, chưa rời tab. Bám theo
    // openPeriod chứ không phải `opened`, để phiếu đã mở nhưng chưa có trong danh sách vẫn lùi được.
    BackHandler(enabled = openPeriod != null) { onClose() }
    if (opened != null) {
        val previous = state.items.getOrNull(state.items.indexOf(opened)+1)
        PayslipDetailView(opened, previous, onClose, onOpenConfirmation, onInquiry, onDownload)
        return
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = screenPadding(),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        if (state.loading && state.items.isEmpty()) item { LoadingBlock() }
        if (state.items.isEmpty() && !state.loading) {
            item {
                EmptyState(
                    "Chưa có phiếu lương",
                    state.error ?: "Khi quản trị phát hành phiếu lương, các tháng sẽ hiện ở đây.",
                )
            }
        }
        if (state.items.isNotEmpty()) {
            item { PayslipArchiveHeader(state.items.size) }
        }
        items(state.items, key = { it.period }) { p -> PayslipMonthCard(p) { period ->
            pendingPeriod = period
        } }
    }
    AppPinGate(
        visible = pendingPeriod != null,
        username = username,
        purpose = "Xác thực để mở chi tiết phiếu lương.",
        onDismiss = { pendingPeriod = null },
        onUnlocked = {
            val period = pendingPeriod
            pendingPeriod = null
            if (period != null) onOpen(period)
        },
    )
}

@Composable
private fun PayslipArchiveHeader(count: Int) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(22.dp),
        color = MaterialTheme.colorScheme.primaryContainer,
    ) {
        Row(
            modifier = Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .clip(RoundedCornerShape(15.dp))
                    .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.14f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Filled.Lock, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
            }
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(
                    "Kho phiếu lương bảo mật",
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                    fontWeight = FontWeight.ExtraBold,
                )
                Text(
                    "$count kỳ lương đã phát hành · Xác thực để xem số tiền",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.78f),
                )
            }
        }
    }
}

/** Thẻ kỳ lương: ưu tiên trạng thái xử lý, không làm lộ số lương trước khi xác thực. */
@Composable
private fun PayslipMonthCard(p: PayslipItem, onOpen: (String) -> Unit) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
        shadowElevation = 1.dp,
        onClick = { onOpen(p.period) },
    ) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Box(
                    modifier = Modifier
                        .size(46.dp)
                        .clip(RoundedCornerShape(14.dp))
                        .background(MaterialTheme.colorScheme.primaryContainer),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(Icons.Filled.ReceiptLong, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(24.dp))
                }
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(formatTimesheetPeriod(p.period), fontSize = 17.sp, fontWeight = FontWeight.ExtraBold, color = MaterialTheme.colorScheme.onSurface)
                    Text(
                        if (p.publishedAt.isBlank() && p.createdAt.isBlank()) "Phiếu đã phát hành"
                        else "Phát hành ${formatPayslipLocalDate(p.publishedAt.ifBlank { p.createdAt })}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                val status = when {
                    p.acknowledgedAt != null -> "Đã xác nhận" to Tone.Success
                    p.acknowledgementOverdue -> "Quá hạn" to Tone.Danger
                    else -> "Chờ xác nhận" to Tone.Warning
                }
                StatusChip(status.first, status.second)
            }
            HorizontalDivider(color = MaterialTheme.colorScheme.outline)
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Filled.Lock, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(16.dp))
                Text(
                    "  Chạm để xem lương, tăng ca và khấu trừ",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.weight(1f),
                )
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
            }
        }
    }
}

/** Chi tiết một kỳ lương theo cấu trúc đối soát: tổng thu − tổng trừ = thực nhận. */
@Composable
private fun PayslipDetailView(
    p: PayslipItem,
    previous: PayslipItem?,
    onClose: () -> Unit,
    onOpenConfirmation: (String) -> Unit,
    onInquiry: (String, String, String) -> Unit,
    onDownload: (PayslipItem) -> Unit,
) {
    val context = LocalContext.current
    var inquiryLine by remember { mutableStateOf<String?>(null) }
    var inquiryText by remember { mutableStateOf("") }
    DisposableEffect(Unit) {
        val activity = generateSequence<Context>(context) { (it as? ContextWrapper)?.baseContext }
            .filterIsInstance<Activity>()
            .firstOrNull()
        activity?.window?.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        onDispose { activity?.window?.clearFlags(WindowManager.LayoutParams.FLAG_SECURE) }
    }
    val earnings = payslipDisplayEarnings(p)
    val deductions = payslipDisplayDeductions(p)
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = screenPadding(),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        item {
            PayslipDetailTopBar(p, onClose)
        }
        item {
            PayslipNetHero(p, previous)
        }
        item { SectionTitle("Đối soát phiếu lương", modifier = Modifier.padding(start = 4.dp)) }
        item { PayslipEquationCard(p) }
        item { SectionTitle("Dữ liệu công", modifier = Modifier.padding(start = 4.dp)) }
        item {
            PayslipWorkSummary(p)
        }
        item { SectionTitle("Tăng ca đã duyệt", modifier = Modifier.padding(start = 4.dp)) }
        item { PayslipOvertimeCard(p) { inquiryLine = "Tăng ca" } }
        item { SectionTitle("Lương & các khoản thu nhập", modifier = Modifier.padding(start = 4.dp)) }
        item {
            PayslipLinesCard(
                lines = earnings,
                emptyText = "Không có dữ liệu khoản thu nhập.",
                totalLabel = "Tổng thu nhập",
                total = p.totalEarnings,
                amountTone = Success,
                negative = false,
                onInquiry = { inquiryLine = it },
            )
        }
        item { SectionTitle("Thuế, bảo hiểm & khấu trừ", modifier = Modifier.padding(start = 4.dp)) }
        item {
            PayslipLinesCard(
                lines = deductions,
                emptyText = "Kỳ này không có khoản khấu trừ.",
                totalLabel = "Tổng khấu trừ",
                total = p.totalDeductions,
                amountTone = Danger,
                negative = true,
                onInquiry = { inquiryLine = it },
            )
        }
        if (p.note.isNotBlank()) {
            item { SectionTitle("Ghi chú từ bộ phận lương", modifier = Modifier.padding(start = 4.dp)) }
            item {
                HrCard {
                    Row(verticalAlignment = Alignment.Top, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Icon(Icons.Filled.Info, contentDescription = null, tint = InfoBlue, modifier = Modifier.size(20.dp))
                        Text(p.note, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
                    }
                }
            }
        }
        item { SectionTitle("Xác nhận & chứng từ", modifier = Modifier.padding(start = 4.dp)) }
        item { PayslipAcknowledgementCard(p) }
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                OutlinedButton(onClick = { onDownload(p) }, modifier = Modifier.weight(1f)) {
                    Icon(Icons.Filled.Description, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(6.dp))
                    Text("Tải PDF")
                }
                if (p.acknowledgedAt == null) {
                    Button(onClick = { onOpenConfirmation(p.id) }, modifier = Modifier.weight(1f)) {
                        Icon(Icons.Filled.CheckCircle, contentDescription = null, modifier = Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                        Text("Mở màn xác nhận")
                    }
                }
            }
        }
        item {
            Text(
                "Nếu số liệu chưa đúng, chọn “Thắc mắc” tại đúng khoản cần kiểm tra. Bộ phận lương sẽ nhận được tên khoản và nội dung bạn gửi.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 4.dp),
            )
        }
        item { Spacer(Modifier.height(8.dp)) }
    }
    inquiryLine?.let { line ->
        AlertDialog(
            onDismissRequest = { inquiryLine = null },
            title = { Text("Thắc mắc về $line") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Mô tả điểm bạn cần bộ phận lương kiểm tra.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    OutlinedTextField(
                        value = inquiryText,
                        onValueChange = { inquiryText = it },
                        placeholder = { Text("Ví dụ: Số giờ hoặc đơn giá chưa đúng…") },
                        minLines = 4,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            },
            confirmButton = {
                Button(
                    enabled = inquiryText.isNotBlank(),
                    onClick = {
                        onInquiry(p.id, line, inquiryText.trim())
                        inquiryLine = null
                        inquiryText = ""
                    },
                ) { Text("Gửi thắc mắc") }
            },
            dismissButton = { TextButton(onClick = { inquiryLine = null }) { Text("Hủy") } },
        )
    }
}

@Composable
private fun PayslipDetailTopBar(p: PayslipItem, onClose: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Surface(
            shape = CircleShape,
            color = MaterialTheme.colorScheme.surface,
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
            onClick = onClose,
        ) {
            Icon(
                Icons.AutoMirrored.Filled.KeyboardArrowLeft,
                contentDescription = "Quay lại",
                tint = MaterialTheme.colorScheme.onSurface,
                modifier = Modifier.padding(9.dp).size(22.dp),
            )
        }
        Column(modifier = Modifier.weight(1f)) {
            Text("Chi tiết phiếu lương", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
            Text(formatTimesheetPeriod(p.period), style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        val status = when {
            p.acknowledgedAt != null -> "Đã xác nhận" to Tone.Success
            p.acknowledgementOverdue -> "Quá hạn" to Tone.Danger
            else -> "Chờ xác nhận" to Tone.Warning
        }
        StatusChip(status.first, status.second)
    }
}

@Composable
private fun PayslipNetHero(p: PayslipItem, previous: PayslipItem?) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(24.dp),
        color = MaterialTheme.colorScheme.primaryContainer,
    ) {
        Column(modifier = Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f)) {
                    Text("THỰC NHẬN", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.72f), fontWeight = FontWeight.ExtraBold)
                    Text(
                        formatMoney(p.netPay),
                        style = MaterialTheme.typography.headlineMedium,
                        color = MaterialTheme.colorScheme.onPrimaryContainer,
                        fontWeight = FontWeight.ExtraBold,
                    )
                }
                Box(
                    modifier = Modifier
                        .size(48.dp)
                        .clip(CircleShape)
                        .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.14f)),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(Icons.Filled.Payments, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(26.dp))
                }
            }
            previous?.let { prev ->
                val diff = p.netPay - prev.netPay
                val tone = if (diff >= 0) Success else Danger
                Text(
                    "${if (diff >= 0) "Tăng" else "Giảm"} ${formatMoney(diff.absoluteValue)} so với ${formatTimesheetPeriod(prev.period).lowercase()}",
                    style = MaterialTheme.typography.bodySmall,
                    color = tone,
                    fontWeight = FontWeight.SemiBold,
                )
            }
            Text(
                "Số tiền sau toàn bộ khoản thu nhập, thuế, bảo hiểm và khấu trừ.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.78f),
            )
        }
    }
}

@Composable
private fun PayslipEquationCard(p: PayslipItem) {
    val difference = payslipBalanceDifference(p)
    val balanced = difference.absoluteValue <= 1.0
    HrCard {
        PayrollEquationRow("Tổng thu nhập", formatMoney(p.totalEarnings), Success)
        PayrollEquationRow("Tổng khấu trừ", "− ${formatMoney(p.totalDeductions)}", Danger)
        HorizontalDivider(color = MaterialTheme.colorScheme.outline)
        PayrollEquationRow("Thực nhận", formatMoney(p.netPay), MaterialTheme.colorScheme.primary, emphasized = true)
        Surface(
            shape = RoundedCornerShape(12.dp),
            color = (if (balanced) Success else Warning).copy(alpha = 0.10f),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 9.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Icon(if (balanced) Icons.Filled.CheckCircle else Icons.Filled.Info, contentDescription = null, tint = if (balanced) Success else Warning, modifier = Modifier.size(18.dp))
                Text(
                    if (balanced) "Số liệu đã khớp: tổng thu − tổng trừ = thực nhận."
                    else "Có chênh lệch ${formatMoney(difference.absoluteValue)} cần bộ phận lương kiểm tra.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.weight(1f),
                )
            }
        }
    }
}

@Composable
private fun PayrollEquationRow(label: String, value: String, color: Color, emphasized: Boolean = false) {
    Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(label, style = if (emphasized) MaterialTheme.typography.titleMedium else MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f))
        Text(value, style = if (emphasized) MaterialTheme.typography.titleLarge else MaterialTheme.typography.titleSmall, color = color, fontWeight = FontWeight.ExtraBold, textAlign = TextAlign.End)
    }
}

@Composable
private fun PayslipWorkSummary(p: PayslipItem) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
            PayrollWorkMetric(Icons.Filled.EventAvailable, "Ngày công", trimNum(p.workedDays.toDouble()), Success, Modifier.weight(1f))
            PayrollWorkMetric(Icons.Filled.Timer, "Giờ làm", if (p.totalWorkedHours > 0) "${trimNum(p.totalWorkedHours)}h" else "--", Color(0xFF0D9488), Modifier.weight(1f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
            PayrollWorkMetric(Icons.Filled.PersonOff, "Ngày vắng", p.absentDays.toString(), Danger, Modifier.weight(1f))
            PayrollWorkMetric(Icons.Filled.Schedule, "Đi muộn", "${p.lateDays} ngày", Warning, Modifier.weight(1f))
        }
    }
}

@Composable
private fun PayrollWorkMetric(icon: ImageVector, label: String, value: String, accent: Color, modifier: Modifier = Modifier) {
    Surface(modifier = modifier, shape = RoundedCornerShape(18.dp), color = MaterialTheme.colorScheme.surface, border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline)) {
        Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(7.dp)) {
            Icon(icon, contentDescription = null, tint = accent, modifier = Modifier.size(21.dp))
            Text(value, style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.ExtraBold)
            Text(label, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun PayslipOvertimeCard(p: PayslipItem, onInquiry: () -> Unit) {
    val rate = payslipResolvedOvertimeRate(p)
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Box(modifier = Modifier.size(42.dp).clip(RoundedCornerShape(13.dp)).background(InfoBlue.copy(alpha = 0.12f)), contentAlignment = Alignment.Center) {
                Icon(Icons.Filled.Schedule, contentDescription = null, tint = InfoBlue, modifier = Modifier.size(22.dp))
            }
            Column(modifier = Modifier.weight(1f)) {
                Text("${trimNum(p.overtimeHours)} giờ tăng ca", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.ExtraBold)
                Text(if (rate > 0) "Đơn giá ${formatMoney(rate)}/giờ" else "Chưa có thông tin đơn giá", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Text(formatMoney(p.overtimePay), style = MaterialTheme.typography.titleMedium, color = InfoBlue, fontWeight = FontWeight.ExtraBold)
        }
        if (p.overtimeDays.isEmpty()) {
            HorizontalDivider(color = MaterialTheme.colorScheme.outline)
            Text(
                if (p.overtimeHours > 0) "Phiếu cũ chỉ lưu tổng giờ tăng ca, chưa có chi tiết từng ngày."
                else "Kỳ này không có ngày tăng ca được duyệt.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        } else {
            HorizontalDivider(color = MaterialTheme.colorScheme.outline)
            p.overtimeDays.forEachIndexed { index, day ->
                if (index > 0) HorizontalDivider(color = MaterialTheme.colorScheme.outline.copy(alpha = 0.7f))
                PayslipOvertimeDayRow(day, rate)
            }
        }
        TextButton(onClick = onInquiry, modifier = Modifier.align(Alignment.End)) { Text("Thắc mắc về tăng ca") }
    }
}

@Composable
private fun PayslipOvertimeDayRow(day: PayslipOvertimeDay, rate: Double) {
    Row(modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(formatIsoDate(day.date), style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.SemiBold)
            Text(
                "${payrollClock(day.checkIn)} → ${payrollClock(day.checkOut)} · ${formatMinutes(day.minutes)}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        if (rate > 0) {
            Text(formatMoney(rate * day.minutes / 60.0), style = MaterialTheme.typography.bodyMedium, color = InfoBlue, fontWeight = FontWeight.Bold)
        }
    }
}

@Composable
private fun PayslipLinesCard(
    lines: List<PayLine>,
    emptyText: String,
    totalLabel: String,
    total: Double,
    amountTone: Color,
    negative: Boolean,
    onInquiry: (String) -> Unit,
) {
    HrCard {
        if (lines.isEmpty()) {
            Text(emptyText, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        } else {
            lines.forEachIndexed { index, line ->
                if (index > 0) HorizontalDivider(color = MaterialTheme.colorScheme.outline.copy(alpha = 0.7f))
                Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.Top, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(1.dp)) {
                        Text(line.label.ifBlank { "Khoản khác" }, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.SemiBold)
                        TextButton(onClick = { onInquiry(line.label.ifBlank { totalLabel }) }) { Text("Thắc mắc") }
                    }
                    Text(
                        (if (negative) "− " else "+ ") + formatMoney(line.amount.absoluteValue),
                        style = MaterialTheme.typography.bodyMedium,
                        color = amountTone,
                        fontWeight = FontWeight.Bold,
                        textAlign = TextAlign.End,
                    )
                }
            }
        }
        HorizontalDivider(color = MaterialTheme.colorScheme.outline)
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(totalLabel, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
            Text((if (negative) "− " else "") + formatMoney(total), style = MaterialTheme.typography.titleMedium, color = amountTone, fontWeight = FontWeight.ExtraBold)
        }
    }
}

@Composable
private fun PayslipAcknowledgementCard(p: PayslipItem) {
    val acknowledged = p.acknowledgedAt != null
    val accent = when {
        acknowledged -> Success
        p.acknowledgementOverdue -> Danger
        else -> Warning
    }
    Surface(modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(18.dp), color = accent.copy(alpha = 0.10f)) {
        Row(modifier = Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Icon(if (acknowledged) Icons.Filled.CheckCircle else Icons.Filled.Info, contentDescription = null, tint = accent, modifier = Modifier.size(24.dp))
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(
                    when {
                        acknowledged -> "Bạn đã xác nhận nhận phiếu"
                        p.acknowledgementOverdue -> "Phiếu đã quá hạn xác nhận"
                        else -> "Phiếu đang chờ bạn xác nhận"
                    },
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurface,
                    fontWeight = FontWeight.Bold,
                )
                Text(
                    if (acknowledged) "Xác nhận lúc ${formatIsoDateTime(p.acknowledgedAt)}"
                    else buildString {
                        val published = p.publishedAt.ifBlank { p.createdAt }
                        append("Phát hành")
                        if (published.isNotBlank()) append(" ngày ${formatPayslipLocalDate(published)}")
                        if (p.acknowledgementDueAt.isNotBlank())
                            append(" · Hạn ${formatPayslipDeadline(p.acknowledgementDueAt)}")
                        append(". Hãy kiểm tra trước khi xác nhận.")
                    },
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

private fun formatPayslipDeadline(value: String): String = runCatching {
    OffsetDateTime.parse(value)
        .atZoneSameInstant(ZoneId.of("Asia/Ho_Chi_Minh"))
        .format(DateTimeFormatter.ofPattern("HH:mm dd/MM/yyyy"))
}.getOrDefault(formatIsoDateTime(value))

private fun formatPayslipLocalDate(value: String): String = runCatching {
    OffsetDateTime.parse(value)
        .atZoneSameInstant(ZoneId.of("Asia/Ho_Chi_Minh"))
        .format(DateTimeFormatter.ofPattern("dd/MM/yyyy"))
}.getOrDefault(formatIsoDate(value))

internal fun payslipDisplayEarnings(p: PayslipItem): List<PayLine> {
    if (p.earnings.isNotEmpty()) return p.earnings
    return buildList {
        if (p.baseSalary != 0.0) add(PayLine("Lương cơ bản", p.baseSalary))
        if (p.allowance != 0.0) add(PayLine("Phụ cấp", p.allowance))
        if (p.overtimePay != 0.0) add(PayLine("Tăng ca (${trimNum(p.overtimeHours)} giờ)", p.overtimePay))
    }
}

internal fun payslipDisplayDeductions(p: PayslipItem): List<PayLine> = when {
    p.deductions.isNotEmpty() -> p.deductions
    p.totalDeductions != 0.0 -> listOf(PayLine("Tổng khấu trừ", p.totalDeductions))
    else -> emptyList()
}

internal fun payslipBalanceDifference(p: PayslipItem): Double =
    p.totalEarnings - p.totalDeductions - p.netPay

internal fun payslipResolvedOvertimeRate(p: PayslipItem): Double = when {
    p.overtimeRate > 0 -> p.overtimeRate
    p.overtimeHours > 0 && p.overtimePay > 0 -> p.overtimePay / p.overtimeHours
    else -> 0.0
}

private fun payrollClock(value: String): String = when {
    value.isBlank() -> "--"
    value.contains('T') -> formatIsoTimeLocal(value)
    value.length >= 5 -> value.take(5)
    else -> value
}
