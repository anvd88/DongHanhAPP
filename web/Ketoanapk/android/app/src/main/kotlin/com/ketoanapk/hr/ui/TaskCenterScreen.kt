package com.ketoanapk.hr.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Checklist
import androidx.compose.material.icons.filled.FactCheck
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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
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
import com.ketoanapk.hr.data.WorkTask
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

/**
 * Thẻ lối vào DUY NHẤT cho công việc trên Trang chủ (đã gộp thẻ "Giao việc" cũ vào đây). Số đếm bao
 * gồm cả việc được giao lẫn đơn/chấm công/hợp đồng cần xử lý.
 */
@Composable
fun TaskCenterEntryCard(count: Int, canAssign: Boolean, onClick: () -> Unit) {
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
                    when {
                        count > 0 -> "$count việc đang chờ bạn xử lý"
                        canAssign -> "Giao việc cho nhân viên & nghiệm thu"
                        else -> "Bạn đã xử lý hết công việc"
                    },
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            if (count > 0) StatusChip("$count", Tone.Warning)
        }
    }
}

/**
 * Màn "Việc cần làm" HỢP NHẤT: một chỗ duy nhất cho mọi việc của người đăng nhập — việc được giao
 * (giao việc & nghiệm thu) + đơn chờ duyệt, chấm công, hợp đồng sắp hết hạn.
 *
 * Tab "Việc tôi giao" mở khi có VIỆC MÌNH ĐÃ GIAO, còn nút "Giao việc mới" mới cần `canAssign`
 * (Thủ kho/Admin). Tách đôi vì kế toán gán phiếu xuất kho cho lái xe là đã thành người giao việc đó
 * và phải nghiệm thu nó, dù không có quyền tạo việc mới. Nhân viên thường không giao việc cho ai
 * nên vẫn không thấy tab này. Quyền thật vẫn do máy chủ chốt; đây chỉ là lớp giao diện.
 */
@Composable
fun TaskCenterScreen(vm: HrViewModel) {
    val tasks = vm.taskCenterItems
    val work = vm.workTasksState
    var approving by remember { mutableStateOf<TaskCenterItem?>(null) }
    var tab by remember { mutableIntStateOf(0) }          // 0 = việc cần làm, 1 = việc tôi giao
    var formOpen by remember { mutableStateOf(false) }
    var editing by remember { mutableStateOf<WorkTask?>(null) }

    // Có gì để hiện ở tab "Việc tôi giao" không: việc mình đã giao, hoặc quyền tạo việc mới.
    val hasOutbox = work.canAssign || work.outbox.isNotEmpty()
    // Tab đang đứng bỗng hết nội dung (bị thu quyền, việc cuối bị xoá) → kéo về tab của nhân viên.
    LaunchedEffect(hasOutbox) { if (!hasOutbox) tab = 0 }

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

    Column(Modifier.fillMaxSize()) {
        // Lối vào LỊCH SỬ: màn này chỉ giữ việc còn phải làm (việc xong của hôm qua tự rụng khi sang
        // ngày mới), nên phải có một cửa rõ ràng để xem lại việc đã hoàn thành.
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 14.dp, end = 14.dp, top = 10.dp),
            horizontalArrangement = Arrangement.End,
        ) {
            OutlinedButton(onClick = vm::openTaskHistory) {
                Icon(Icons.Filled.FactCheck, contentDescription = null)
                Spacer(Modifier.width(8.dp))
                Text("Lịch sử")
            }
        }
        // Có việc mình giao thì mới dựng thanh tab.
        if (hasOutbox) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 14.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                SegTab("Việc cần làm", tasks.size + work.summary.inboxActionable + work.summary.collectionsStandalone, tab == 0, Modifier.weight(1f)) { tab = 0 }
                // Số trên tab = việc CẦN BẤM: chờ nghiệm thu (việc thường) + chờ thu tờ phiếu (giao hàng).
                SegTab(
                    "Việc tôi giao",
                    work.summary.outboxReview + work.summary.outboxAwaitingVoucher,
                    tab == 1,
                    Modifier.weight(1f),
                ) { tab = 1 }
            }
            // Nút tạo việc mới thì vẫn phải có quyền giao việc thật.
            if (tab == 1 && work.canAssign) {
                Button(
                    onClick = { editing = null; formOpen = true },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 14.dp),
                ) {
                    Icon(Icons.Filled.Add, contentDescription = null)
                    Spacer(Modifier.width(8.dp))
                    Text("Giao việc mới")
                }
            }
        }

        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = screenPadding(),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            if (tab == 1) {
                // ── Việc tôi giao (chỉ Thủ kho/Admin) ──
                if (work.loading && work.outbox.isEmpty()) {
                    item { LoadingBlock() }
                } else if (work.outbox.isEmpty()) {
                    item { EmptyState("Bạn chưa giao việc nào", work.error ?: "Bấm \"Giao việc mới\" để bắt đầu.") }
                    if (work.error != null) item {
                        Button(onClick = { vm.loadWorkTasks() }, modifier = Modifier.fillMaxWidth()) { Text("Thử lại") }
                    }
                } else {
                    items(work.outbox, key = { "out-${it.id}" }) { task ->
                        WorkTaskCard(task = task, isInbox = false) { vm.openWorkTask(task.id) }
                    }
                }
            } else {
                val loading = vm.homeState.loading || work.loading
                val error = vm.homeState.error ?: work.error
                if (loading && tasks.isEmpty() && work.inbox.isEmpty() && work.collections.isEmpty()) {
                    item { LoadingBlock() }
                } else if (tasks.isEmpty() && work.inbox.isEmpty() && work.collections.isEmpty()) {
                    item { EmptyState("Đã xử lý hết", error ?: "Hiện không có công việc nào cần bạn xử lý.") }
                    if (error != null) item { Button(onClick = vm::refreshTasks, modifier = Modifier.fillMaxWidth()) { Text("Thử lại") } }
                } else {
                    // Việc được giao cho tôi lên trước: đó là việc có người chờ mình nghiệm thu.
                    if (work.inbox.isNotEmpty()) {
                        item(key = "header-assigned") { GroupHeader("Việc được giao cho bạn") }
                        items(work.inbox, key = { "in-${it.id}" }) { task ->
                            WorkTaskCard(task = task, isInbox = true) { vm.openWorkTask(task.id) }
                        }
                    }
                    // Lệnh thu tiền không đi kèm phiếu giao nào. Lệnh có phiếu đã được máy chủ gộp
                    // vào chính thẻ việc giao hàng ở trên nên không lặp lại ở đây.
                    if (work.collections.isNotEmpty()) {
                        item(key = "header-collections") { GroupHeader("Tiền cần thu") }
                        items(work.collections, key = { "col-${it.id}" }) { collection ->
                            StandaloneCollectionCard(collection) { vm.select(HrDestination.CashCollections) }
                        }
                    }
                    TaskBucket.entries.forEach { bucket ->
                        val group = tasks.filter { it.bucket == bucket }
                        if (group.isNotEmpty()) {
                            item(key = "header-$bucket") { GroupHeader(bucket.label) }
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
    }

    WorkTaskDialogs(
        vm = vm,
        formOpen = formOpen,
        editing = editing,
        onEdit = { task -> editing = task; formOpen = true },
        onCloseForm = { formOpen = false; editing = null },
    )
}

@Composable
private fun GroupHeader(label: String) {
    Text(
        label,
        style = MaterialTheme.typography.titleMedium,
        fontWeight = FontWeight.Bold,
        modifier = Modifier.padding(top = 6.dp, start = 4.dp),
    )
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
