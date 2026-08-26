package com.ketoanapk.hr.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.FactCheck
import androidx.compose.material.icons.filled.LocalShipping
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.WorkTask
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/**
 * Màn "Lịch sử công việc" — màn RIÊNG (không phải hộp thoại), vào từ nút Lịch sử ở màn Việc cần làm.
 *
 * Vì sao cần: màn Việc cần làm nay chỉ giữ việc còn phải làm — hết ngày là việc đã xong của hôm qua
 * rời màn hình. Thành tích không mất đi, nó nằm ở đây: lọc theo TUẦN hoặc THÁNG, và (với người giao
 * việc/quản trị) lọc tiếp theo TỪNG NHÂN VIÊN.
 *
 * Bố cục bám theo màn "Thu tiền": thẻ tổng ở đầu, hàng chip lọc, rồi danh sách thẻ HrCard.
 */
@Composable
fun TaskHistoryScreen(vm: HrViewModel) {
    val state = vm.taskHistoryState
    val result = state.result
    val items = result?.items.orEmpty()
    val people = result?.people.orEmpty()

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = screenPadding(),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            HrCard {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Surface(shape = RoundedCornerShape(12.dp), color = toneColor(Tone.Success).copy(alpha = .13f)) {
                        Icon(
                            Icons.Filled.FactCheck,
                            contentDescription = null,
                            tint = toneColor(Tone.Success),
                            modifier = Modifier.padding(10.dp).size(22.dp),
                        )
                    }
                    Spacer(Modifier.width(10.dp))
                    Column(Modifier.weight(1f)) {
                        Text("Việc đã hoàn thành", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        Text("${items.size} việc", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Black)
                        Text(
                            historyRangeLabel(state.range, state.from, state.to),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    IconButton(onClick = { vm.loadTaskHistory(silent = items.isNotEmpty()) }) {
                        Icon(Icons.Filled.Refresh, contentDescription = "Làm mới")
                    }
                }
            }
        }

        // Chọn tuần / tháng.
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                TaskHistoryRange.entries.forEach { range ->
                    FilterChip(
                        selected = state.range == range,
                        onClick = { vm.setTaskHistoryRange(range) },
                        label = { Text(range.label) },
                    )
                }
            }
        }

        // Lùi / tiến khoảng đang xem.
        item {
            Surface(shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = .5f)) {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 4.dp, vertical = 2.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    IconButton(onClick = { vm.shiftTaskHistory(-1) }) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Kỳ trước")
                    }
                    Text(
                        historyPeriodTitle(state.range, state.from, state.to),
                        modifier = Modifier.weight(1f),
                        textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                        fontWeight = FontWeight.Bold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    IconButton(onClick = { vm.shiftTaskHistory(1) }) {
                        Icon(Icons.AutoMirrored.Filled.ArrowForward, contentDescription = "Kỳ sau")
                    }
                }
            }
        }

        // Lọc theo từng nhân viên. Chỉ dựng khi thật sự có nhiều người trong kỳ — nhân viên thường
        // chỉ thấy việc của chính mình nên hàng chip này sẽ không xuất hiện.
        if (people.size > 1) {
            item {
                Row(
                    modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    FilterChip(
                        selected = state.assignee == null,
                        onClick = { vm.setTaskHistoryAssignee(null) },
                        label = { Text("Tất cả (${people.sumOf { it.count }})") },
                    )
                    people.forEach { person ->
                        FilterChip(
                            selected = state.assignee.equals(person.username, ignoreCase = true),
                            onClick = { vm.setTaskHistoryAssignee(person.username) },
                            label = { Text("${person.fullName.ifBlank { person.username }} (${person.count})") },
                        )
                    }
                }
            }
        }

        if (state.loading && items.isEmpty()) {
            item {
                Box(Modifier.fillMaxWidth().height(160.dp), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
            }
        }
        state.error?.let { error -> item { ErrorText(error) } }
        if (!state.loading && items.isEmpty() && state.error == null) {
            item {
                EmptyState(
                    "Chưa có việc nào hoàn thành",
                    "Không có việc nào được nghiệm thu trong ${historyPeriodTitle(state.range, state.from, state.to).lowercase()}.",
                )
            }
        }
        items(items, key = { it.id }) { task ->
            TaskHistoryCard(task) { vm.openWorkTask(task.id) }
        }
    }

    // Bấm vào một việc trong lịch sử vẫn mở đúng hộp thoại chi tiết như ở màn Việc cần làm.
    WorkTaskDialogs(vm = vm, formOpen = false, editing = null, onEdit = {}, onCloseForm = {})
}

@Composable
private fun TaskHistoryCard(task: WorkTask, onOpen: () -> Unit) {
    val done = task.reviewedAt ?: task.updatedAt
    HrCard(modifier = Modifier.clickable(onClick = onOpen)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(task.taskNo, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.weight(1f))
            StatusChip(if (task.status == "completed") "Đã hoàn thành" else "Đã nghiệm thu", Tone.Success)
        }
        Text(task.title, fontWeight = FontWeight.Bold, maxLines = 2, overflow = TextOverflow.Ellipsis)
        task.delivery?.let { delivery ->
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Icon(
                    Icons.Filled.LocalShipping,
                    contentDescription = null,
                    modifier = Modifier.size(15.dp),
                    tint = MaterialTheme.colorScheme.primary,
                )
                Text(
                    "Phiếu ${delivery.voucherNo}" + if (delivery.customerName.isNotBlank()) " · ${delivery.customerName}" else "",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        HorizontalDivider(Modifier.padding(vertical = 8.dp))
        HistoryInfoRow("Người làm", task.assigneeName)
        HistoryInfoRow("Người giao", task.assignerName)
        HistoryInfoRow("Hoàn thành lúc", historyDateTime(done), strong = true)
        if (task.dueAt != null) HistoryInfoRow("Hạn được giao", historyDate(task.dueAt))
        task.rating?.let { rating ->
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 4.dp)) {
                Text("Đánh giá", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Spacer(Modifier.weight(1f))
                repeat(rating) {
                    Icon(Icons.Filled.Star, contentDescription = null, tint = toneColor(Tone.Warning), modifier = Modifier.size(15.dp))
                }
            }
        }
        if (task.submitNote.isNotBlank()) {
            Text(
                "Kết quả: ${task.submitNote}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp),
            )
        }
        if (task.reviewNote.isNotBlank()) {
            Text(
                "Nhận xét: ${task.reviewNote}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun HistoryInfoRow(label: String, value: String, strong: Boolean = false) {
    Row(Modifier.fillMaxWidth().padding(vertical = 2.dp), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(label, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(
            value.ifBlank { "--" },
            style = MaterialTheme.typography.bodySmall,
            fontWeight = if (strong) FontWeight.Black else FontWeight.Medium,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

private val historyWeekdays = listOf("T2", "T3", "T4", "T5", "T6", "T7", "CN")

/** "Tuần 10/08 – 16/08/2026" hoặc "Tháng 8/2026" — chữ trên thanh lùi/tiến. */
internal fun historyPeriodTitle(range: TaskHistoryRange, from: LocalDate, to: LocalDate): String = when (range) {
    TaskHistoryRange.Week ->
        "Tuần %02d/%02d – %02d/%02d/%d".format(from.dayOfMonth, from.monthValue, to.dayOfMonth, to.monthValue, to.year)
    TaskHistoryRange.Month -> "Tháng ${from.monthValue}/${from.year}"
}

/** Dòng phụ dưới con số tổng: nói rõ đang đếm trong khoảng ngày nào. */
internal fun historyRangeLabel(range: TaskHistoryRange, from: LocalDate, to: LocalDate): String {
    val fromLabel = "%02d/%02d/%d".format(from.dayOfMonth, from.monthValue, from.year)
    val toLabel = "%02d/%02d/%d".format(to.dayOfMonth, to.monthValue, to.year)
    val prefix = if (range == TaskHistoryRange.Week) "${historyWeekdays[from.dayOfWeek.value - 1]} " else ""
    return "$prefix$fromLabel → $toLabel"
}

private fun historyInstant(value: String?): Instant? {
    val raw = value ?: return null
    return runCatching { OffsetDateTime.parse(raw).toInstant() }
        .recoverCatching { Instant.parse(raw) }
        .getOrNull()
}

private fun historyDateTime(value: String?): String {
    val instant = historyInstant(value) ?: return "--"
    return DateTimeFormatter.ofPattern("dd/MM/yyyy HH:mm").withZone(ZoneId.systemDefault()).format(instant)
}

private fun historyDate(value: String?): String {
    val instant = historyInstant(value) ?: return "--"
    return DateTimeFormatter.ofPattern("dd/MM/yyyy").withZone(ZoneId.systemDefault()).format(instant)
}
