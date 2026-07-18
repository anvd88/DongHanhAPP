package com.ketoanapk.hr.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Checklist
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.ManagerHeadcount
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.Timesheet
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId

enum class TaskBucket(val label: String) {
    Today("Hôm nay"),
    DueSoon("Sắp hết hạn"),
    Overdue("Quá hạn"),
}

enum class TaskKind { Approval, Attendance, ExpiringContract }

data class TaskCenterItem(
    val id: String,
    val kind: TaskKind,
    val bucket: TaskBucket,
    val title: String,
    val subtitle: String,
    val dueLabel: String,
    val target: HrDestination,
    val entityId: String? = null,
)

internal fun buildTaskCenterItems(
    inbox: List<RequestListItem>,
    timesheet: Timesheet?,
    manager: ManagerHeadcount?,
    now: Instant = Instant.now(),
    zone: ZoneId = ZoneId.systemDefault(),
): List<TaskCenterItem> {
    val today = now.atZone(zone).toLocalDate()
    val tasks = mutableListOf<TaskCenterItem>()

    inbox.filter { it.status.equals("Pending", true) }.forEach { request ->
        val created = parseTaskInstant(request.createdAt) ?: now
        val due = created.plus(Duration.ofHours(24))
        val bucket = when {
            now.isAfter(due) -> TaskBucket.Overdue
            created.atZone(zone).toLocalDate() == today -> TaskBucket.Today
            else -> TaskBucket.DueSoon
        }
        val hours = Duration.between(now, due).toHours()
        tasks += TaskCenterItem(
            id = "approval:${request.id}",
            kind = TaskKind.Approval,
            bucket = bucket,
            title = request.typeLabel.ifBlank { request.title.ifBlank { "Đơn chờ duyệt" } },
            subtitle = listOf(request.employeeName.ifBlank { request.requesterUsername }, request.requestNo).filter { it.isNotBlank() }.joinToString(" · "),
            dueLabel = if (hours < 0) "Quá hạn ${-hours} giờ" else "Còn ${hours.coerceAtLeast(0)} giờ",
            target = HrDestination.Approval,
            entityId = request.id,
        )
    }

    val todayRow = timesheet?.days?.firstOrNull { it.date.take(10) == today.toString() }
    if (todayRow != null && todayRow.shiftName.isNotBlank() && todayRow.checkIn.isNullOrBlank()) {
        tasks += TaskCenterItem(
            id = "attendance:$today",
            kind = TaskKind.Attendance,
            bucket = TaskBucket.Today,
            title = "Chưa chấm công vào ca",
            subtitle = todayRow.shiftName,
            dueLabel = "Xử lý hôm nay",
            target = HrDestination.Scan,
        )
    }

    val expiring = manager?.expiringContracts ?: 0
    if (expiring > 0) {
        tasks += TaskCenterItem(
            id = "contracts:$today",
            kind = TaskKind.ExpiringContract,
            bucket = TaskBucket.DueSoon,
            title = "$expiring hợp đồng sắp hết hạn",
            subtitle = "Rà soát hồ sơ và kế hoạch gia hạn",
            dueLabel = "Trong 30 ngày",
            target = HrDestination.People,
        )
    }

    return tasks.sortedWith(compareBy<TaskCenterItem> { it.bucket.ordinal }.thenBy { it.id })
}

private fun parseTaskInstant(value: String): Instant? = runCatching { OffsetDateTime.parse(value).toInstant() }
    .recoverCatching { Instant.parse(value) }
    .getOrNull()

@Composable
fun TaskCenterEntryCard(count: Int, onClick: () -> Unit) {
    Surface(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = if (count > 0) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
    ) {
        Row(
            modifier = Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Icon(Icons.Filled.Checklist, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text("Việc cần làm", fontWeight = FontWeight.Bold)
                Text(
                    if (count > 0) "$count việc đang chờ bạn xử lý" else "Bạn đã xử lý hết công việc",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            if (count > 0) StatusChip("$count", Tone.Warning)
        }
    }
}

@Composable
fun TaskCenterScreen(vm: HrViewModel) {
    val tasks = vm.taskCenterItems
    var approving by remember { mutableStateOf<TaskCenterItem?>(null) }

    approving?.let { task ->
        AlertDialog(
            onDismissRequest = { if (vm.taskActionBusyId == null) approving = null },
            title = { Text("Xác nhận duyệt nhanh") },
            text = { Text("Duyệt ${task.title.lowercase()} của ${task.subtitle}?") },
            confirmButton = {
                Button(
                    enabled = vm.taskActionBusyId == null,
                    onClick = {
                        task.entityId?.let(vm::quickApproveRequest)
                        approving = null
                    },
                ) { Text("Duyệt") }
            },
            dismissButton = { TextButton(onClick = { approving = null }) { Text("Hủy") } },
        )
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        if (vm.homeState.loading && tasks.isEmpty()) {
            item { LoadingBlock() }
        } else if (tasks.isEmpty()) {
            item { EmptyState("Đã xử lý hết", vm.homeState.error ?: "Hiện không có công việc nào cần bạn xử lý.") }
            if (vm.homeState.error != null) item { Button(onClick = vm::refreshTasks, modifier = Modifier.fillMaxWidth()) { Text("Thử lại") } }
        } else {
            TaskBucket.entries.forEach { bucket ->
                val group = tasks.filter { it.bucket == bucket }
                if (group.isNotEmpty()) {
                    item(key = "header-$bucket") {
                        Text(bucket.label, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 6.dp, start = 4.dp))
                    }
                    items(group, key = { it.id }) { task ->
                        TaskCenterCard(
                            task = task,
                            busy = vm.taskActionBusyId == task.entityId,
                            onOpen = { vm.openTask(task) },
                            onApprove = { approving = task },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun TaskCenterCard(task: TaskCenterItem, busy: Boolean, onOpen: () -> Unit, onApprove: () -> Unit) {
    val tone = when (task.bucket) {
        TaskBucket.Today -> Tone.Info
        TaskBucket.DueSoon -> Tone.Warning
        TaskBucket.Overdue -> Tone.Danger
    }
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, toneColor(tone).copy(alpha = 0.45f)),
    ) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(9.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Icon(Icons.Filled.Schedule, contentDescription = null, tint = toneColor(tone))
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(task.title, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(task.subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2, overflow = TextOverflow.Ellipsis)
                }
                StatusChip(task.dueLabel, tone)
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                OutlinedButton(onClick = onOpen, modifier = Modifier.weight(1f)) {
                    Text(if (task.kind == TaskKind.Attendance) "Chấm công ngay" else "Mở chi tiết")
                }
                if (task.kind == TaskKind.Approval) {
                    Button(
                        onClick = onApprove,
                        enabled = !busy,
                        modifier = Modifier.weight(1f),
                        colors = ButtonDefaults.buttonColors(containerColor = toneColor(Tone.Success)),
                    ) { Text(if (busy) "Đang duyệt…" else "Duyệt nhanh") }
                }
            }
        }
    }
}
