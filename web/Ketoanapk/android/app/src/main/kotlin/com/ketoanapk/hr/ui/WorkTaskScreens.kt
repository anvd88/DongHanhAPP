package com.ketoanapk.hr.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.AssignmentTurnedIn
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Send
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.filled.StarBorder
import androidx.compose.material.icons.filled.Undo
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.WorkTask
import com.ketoanapk.hr.data.WorkTaskMeta
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter

// ── Nhãn & tông màu trạng thái/ưu tiên ──
private fun statusLabel(s: String) = when (s) {
    "assigned" -> "Chờ nhận"
    "in_progress" -> "Đang làm"
    "submitted" -> "Chờ nghiệm thu"
    "accepted" -> "Đã nghiệm thu"
    "rejected" -> "Bị trả lại"
    "cancelled" -> "Đã huỷ"
    else -> s
}
private fun statusTone(s: String) = when (s) {
    "assigned" -> Tone.Info
    "in_progress" -> Tone.Neutral
    "submitted" -> Tone.Warning
    "accepted" -> Tone.Success
    "rejected" -> Tone.Danger
    else -> Tone.Muted
}
private fun priorityLabel(p: String) = when (p) {
    "low" -> "Thấp"; "high" -> "Cao"; "urgent" -> "Khẩn"; else -> "Bình thường"
}
private fun priorityTone(p: String) = when (p) {
    "low" -> Tone.Muted; "high" -> Tone.Warning; "urgent" -> Tone.Danger; else -> Tone.Info
}
private val PRIORITIES = listOf("low", "normal", "high", "urgent")

private fun parseInstant(v: String?): Instant? {
    val s = v ?: return null
    return runCatching { OffsetDateTime.parse(s).toInstant() }
        .recoverCatching { Instant.parse(s) }
        .getOrNull()
}
private fun fmtDateTime(v: String?): String {
    val i = parseInstant(v) ?: return ""
    return DateTimeFormatter.ofPattern("dd/MM/yyyy HH:mm").withZone(ZoneId.systemDefault()).format(i)
}
private fun fmtDate(v: String?): String {
    val i = parseInstant(v) ?: return ""
    return DateTimeFormatter.ofPattern("dd/MM/yyyy").withZone(ZoneId.systemDefault()).format(i)
}
private fun fmtDateInput(v: String?): String {
    val i = parseInstant(v) ?: return ""
    return DateTimeFormatter.ofPattern("yyyy-MM-dd").withZone(ZoneId.systemDefault()).format(i)
}

/** Thẻ lối vào "Giao việc" trên Trang chủ. */
@Composable
fun WorkTaskEntryCard(badge: Int, canAssign: Boolean, onClick: () -> Unit) {
    Surface(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = if (badge > 0) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
    ) {
        Row(
            modifier = Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Icon(Icons.Filled.AssignmentTurnedIn, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text("Giao việc", fontWeight = FontWeight.Bold)
                Text(
                    when {
                        badge > 0 -> "$badge việc cần bạn xử lý"
                        canAssign -> "Giao việc cho nhân viên & nghiệm thu"
                        else -> "Việc được giao cho bạn"
                    },
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            if (badge > 0) StatusChip("$badge", Tone.Warning)
        }
    }
}

@Composable
fun WorkTaskScreen(vm: HrViewModel) {
    val state = vm.workTasksState
    var tab by remember { mutableIntStateOf(0) } // 0 = việc của tôi, 1 = việc tôi giao
    var formOpen by remember { mutableStateOf(false) }
    var editing by remember { mutableStateOf<WorkTask?>(null) }

    LaunchedEffect(state.canAssign) { if (!state.canAssign) tab = 0 }

    val rows = if (tab == 0) state.inbox else state.outbox

    Column(Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 14.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            SegTab("Việc của tôi", state.summary.inboxActionable, tab == 0, Modifier.weight(1f)) { tab = 0 }
            if (state.canAssign) SegTab("Việc tôi giao", state.summary.outboxReview, tab == 1, Modifier.weight(1f)) { tab = 1 }
        }

        if (state.canAssign) {
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

        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(14.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            if (state.loading && rows.isEmpty()) {
                item { LoadingBlock() }
            } else if (rows.isEmpty()) {
                item {
                    EmptyState(
                        if (tab == 0) "Chưa có việc được giao" else "Bạn chưa giao việc nào",
                        state.error ?: if (tab == 1 && state.canAssign) "Bấm \"Giao việc mới\" để bắt đầu." else "Việc được giao cho bạn sẽ hiện ở đây.",
                    )
                }
                if (state.error != null) item {
                    Button(onClick = { vm.loadWorkTasks() }, modifier = Modifier.fillMaxWidth()) { Text("Thử lại") }
                }
            } else {
                items(rows, key = { it.id }) { task ->
                    WorkTaskCard(task = task, isInbox = tab == 0) { vm.openWorkTask(task.id) }
                }
            }
        }
    }

    if (vm.workTaskDetail.id != null) {
        WorkTaskDetailDialog(vm, onEdit = { task -> vm.closeWorkTaskDetail(); editing = task; formOpen = true })
    }
    if (formOpen) {
        WorkTaskFormDialog(vm = vm, meta = state.meta, editing = editing) { formOpen = false; editing = null }
    }
}

@Composable
private fun SegTab(label: String, badge: Int, selected: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Surface(
        onClick = onClick,
        modifier = modifier,
        shape = RoundedCornerShape(14.dp),
        color = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
    ) {
        Row(
            modifier = Modifier.padding(vertical = 10.dp, horizontal = 8.dp),
            horizontalArrangement = Arrangement.Center,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                label,
                fontWeight = FontWeight.Bold,
                color = if (selected) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurface,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (badge > 0) {
                Spacer(Modifier.width(6.dp))
                StatusChip("$badge", if (selected) Tone.Muted else Tone.Warning)
            }
        }
    }
}

@Composable
private fun WorkTaskCard(task: WorkTask, isInbox: Boolean, onOpen: () -> Unit) {
    Surface(
        onClick = onOpen,
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, toneColor(statusTone(task.status)).copy(alpha = 0.4f)),
    ) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(task.taskNo, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                StatusChip(priorityLabel(task.priority), priorityTone(task.priority))
                Spacer(Modifier.weight(1f))
                StatusChip(statusLabel(task.status), statusTone(task.status))
            }
            Text(task.title, fontWeight = FontWeight.Bold, maxLines = 2, overflow = TextOverflow.Ellipsis)
            Text(
                if (isInbox) "Người giao: ${task.assignerName}" else "Người nhận: ${task.assigneeName}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (task.dueAt != null) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    Icon(
                        Icons.Filled.CalendarMonth,
                        contentDescription = null,
                        modifier = Modifier.width(15.dp),
                        tint = if (task.overdue) toneColor(Tone.Danger) else MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Text(
                        "Hạn ${fmtDate(task.dueAt)}" + if (task.overdue) " · quá hạn" else "",
                        style = MaterialTheme.typography.bodySmall,
                        color = if (task.overdue) toneColor(Tone.Danger) else MaterialTheme.colorScheme.onSurfaceVariant,
                        fontWeight = if (task.overdue) FontWeight.Bold else FontWeight.Normal,
                    )
                }
            }
            if (task.status == "in_progress" || task.status == "submitted" || task.progress > 0) {
                LinearProgressIndicator(
                    progress = { (if (task.status == "accepted") 100 else task.progress) / 100f },
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }
    }
}

@Composable
private fun WorkTaskDetailDialog(vm: HrViewModel, onEdit: (WorkTask) -> Unit) {
    val ds = vm.workTaskDetail
    val detail = ds.detail
    var note by remember(ds.id) { mutableStateOf("") }
    var progress by remember(detail?.task?.progress) { mutableFloatStateOf((detail?.task?.progress ?: 0).toFloat()) }
    var rating by remember(ds.id) { mutableIntStateOf(0) }

    AlertDialog(
        onDismissRequest = { if (!vm.workTaskBusy) vm.closeWorkTaskDetail() },
        confirmButton = {},
        dismissButton = { TextButton(onClick = { vm.closeWorkTaskDetail() }) { Text("Đóng") } },
        title = { Text(detail?.task?.let { "${it.taskNo} · ${it.title}" } ?: "Chi tiết công việc", maxLines = 2, overflow = TextOverflow.Ellipsis) },
        text = {
            if (detail == null) {
                Box(Modifier.fillMaxWidth().padding(20.dp), contentAlignment = Alignment.Center) {
                    if (ds.error != null) Text(ds.error) else LoadingBlock()
                }
            } else {
                val task = detail.task
                val flags = detail.flags
                val id = task.id
                Column(
                    Modifier
                        .fillMaxWidth()
                        .heightIn(max = 460.dp)
                        .verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                        StatusChip(statusLabel(task.status), statusTone(task.status))
                        StatusChip(priorityLabel(task.priority), priorityTone(task.priority))
                        task.rating?.let { StatusChip("$it★", Tone.Warning) }
                    }
                    LabelValue("Người giao", task.assignerName)
                    LabelValue("Người nhận", task.assigneeName)
                    LabelValue("Ngày giao", fmtDateTime(task.createdAt))
                    if (task.dueAt != null) LabelValue("Hạn hoàn thành", fmtDateTime(task.dueAt) + if (task.overdue) " (quá hạn)" else "")
                    if (task.description.isNotBlank()) LabelValue("Mô tả", task.description)

                    LinearProgressIndicator(
                        progress = { (if (task.status == "accepted") 100 else task.progress) / 100f },
                        modifier = Modifier.fillMaxWidth(),
                    )

                    if (task.submitNote.isNotBlank()) LabelValue("Ghi chú khi nộp", task.submitNote)
                    if (task.reviewNote.isNotBlank())
                        LabelValue(if (task.status == "rejected") "Lý do trả lại" else "Ý kiến nghiệm thu", task.reviewNote)

                    // Lịch sử
                    if (detail.events.isNotEmpty()) {
                        Text("Lịch sử", fontWeight = FontWeight.Bold, style = MaterialTheme.typography.labelLarge)
                        detail.events.forEach { ev ->
                            Column(verticalArrangement = Arrangement.spacedBy(1.dp)) {
                                Text("${ev.actorName} · ${eventLabel(ev.kind)}", style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.SemiBold)
                                if (ev.note.isNotBlank()) Text(ev.note, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                Text(fmtDateTime(ev.createdAt), style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        }
                    }

                    // Thao tác của người nhận
                    if (flags.mine && task.status != "accepted" && task.status != "cancelled" && task.status != "submitted") {
                        Spacer(Modifier.width(0.dp))
                        Text("Bạn là người thực hiện", fontWeight = FontWeight.Bold)
                        if (flags.canStart) {
                            OutlinedButton(onClick = { vm.startWorkTask(id) }, enabled = !vm.workTaskBusy, modifier = Modifier.fillMaxWidth()) {
                                Text("Bắt đầu làm")
                            }
                        }
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Slider(value = progress, onValueChange = { progress = it }, valueRange = 0f..100f, steps = 9, modifier = Modifier.weight(1f))
                            Text("${progress.toInt()}%", modifier = Modifier.width(46.dp))
                        }
                        OutlinedButton(
                            onClick = { vm.updateWorkTaskProgress(id, progress.toInt(), note) },
                            enabled = !vm.workTaskBusy,
                            modifier = Modifier.fillMaxWidth(),
                        ) { Text("Lưu tiến độ") }
                        OutlinedTextField(
                            value = note, onValueChange = { note = it },
                            label = { Text("Ghi chú kết quả (khi nộp)") },
                            modifier = Modifier.fillMaxWidth(),
                        )
                        Button(onClick = { vm.submitWorkTask(id, note) }, enabled = !vm.workTaskBusy, modifier = Modifier.fillMaxWidth()) {
                            Icon(Icons.Filled.Send, contentDescription = null); Spacer(Modifier.width(8.dp)); Text("Nộp để nghiệm thu")
                        }
                    }

                    // Nghiệm thu của người giao
                    if (flags.canReview && task.status == "submitted") {
                        Text("Nghiệm thu công việc", fontWeight = FontWeight.Bold)
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text("Đánh giá:", style = MaterialTheme.typography.bodySmall)
                            (1..5).forEach { n ->
                                IconButton(onClick = { rating = n }) {
                                    Icon(
                                        if (n <= rating) Icons.Filled.Star else Icons.Filled.StarBorder,
                                        contentDescription = "$n sao",
                                        tint = toneColor(Tone.Warning),
                                    )
                                }
                            }
                        }
                        OutlinedTextField(
                            value = note, onValueChange = { note = it },
                            label = { Text("Nhận xét (bắt buộc khi trả lại)") },
                            modifier = Modifier.fillMaxWidth(),
                        )
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Button(
                                onClick = { vm.acceptWorkTask(id, note, rating.takeIf { it > 0 }) },
                                enabled = !vm.workTaskBusy,
                                modifier = Modifier.weight(1f),
                                colors = ButtonDefaults.buttonColors(containerColor = toneColor(Tone.Success)),
                            ) { Icon(Icons.Filled.CheckCircle, contentDescription = null); Spacer(Modifier.width(6.dp)); Text("Đạt") }
                            OutlinedButton(
                                onClick = { if (note.isBlank()) vm.showActionMessage("Nhập lý do trả lại.") else vm.rejectWorkTask(id, note) },
                                enabled = !vm.workTaskBusy,
                                modifier = Modifier.weight(1f),
                            ) { Icon(Icons.Filled.Undo, contentDescription = null); Spacer(Modifier.width(6.dp)); Text("Trả lại") }
                        }
                    }

                    // Quản lý của người giao
                    if (flags.assignedByMe && (flags.canEdit || flags.canCancel)) {
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (flags.canEdit) OutlinedButton(onClick = { onEdit(task) }, modifier = Modifier.weight(1f)) { Text("Sửa") }
                            if (flags.canCancel) OutlinedButton(
                                onClick = { vm.cancelWorkTask(id, note) },
                                enabled = !vm.workTaskBusy,
                                modifier = Modifier.weight(1f),
                            ) { Text("Huỷ việc") }
                        }
                    }
                }
            }
        },
    )
}

private fun eventLabel(kind: String) = when (kind) {
    "assigned" -> "đã giao việc"
    "reassigned" -> "đã chuyển người nhận"
    "updated" -> "đã cập nhật"
    "started" -> "đã bắt đầu làm"
    "progress" -> "cập nhật tiến độ"
    "submitted" -> "đã nộp nghiệm thu"
    "accepted" -> "đã nghiệm thu đạt"
    "rejected" -> "đã trả lại"
    "cancelled" -> "đã huỷ việc"
    "comment" -> "trao đổi"
    else -> kind
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun WorkTaskFormDialog(vm: HrViewModel, meta: WorkTaskMeta?, editing: WorkTask?, onDismiss: () -> Unit) {
    var title by remember { mutableStateOf(editing?.title ?: "") }
    var description by remember { mutableStateOf(editing?.description ?: "") }
    var assignee by remember { mutableStateOf(editing?.assigneeUsername ?: "") }
    var priority by remember { mutableStateOf(editing?.priority ?: "normal") }
    var dueText by remember { mutableStateOf(fmtDateInput(editing?.dueAt)) }

    val assignees = meta?.assignees ?: emptyList()
    val assigneeLabel = assignees.firstOrNull { it.username == assignee }
        ?.let { it.fullName + if (it.department.isNotBlank()) " · ${it.department}" else "" }
        ?: editing?.assigneeName ?: ""

    fun save() {
        if (title.isBlank()) { vm.showActionMessage("Nhập tên công việc."); return }
        if (assignee.isBlank()) { vm.showActionMessage("Chọn người nhận việc."); return }
        val dueIso = dueText.trim().takeIf { it.isNotBlank() }?.let {
            runCatching { LocalDate.parse(it).atTime(17, 0).atZone(ZoneId.systemDefault()).toInstant().toString() }.getOrNull()
        }
        if (editing != null) vm.updateWorkTask(editing.id, title, description, assignee, priority, dueIso, onDismiss)
        else vm.createWorkTask(title, description, assignee, priority, dueIso, onDismiss)
    }

    AlertDialog(
        onDismissRequest = { if (!vm.workTaskBusy) onDismiss() },
        confirmButton = { Button(onClick = { save() }, enabled = !vm.workTaskBusy) { Text(if (editing != null) "Lưu" else "Giao việc") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Huỷ") } },
        title = { Text(if (editing != null) "Sửa công việc" else "Giao việc mới") },
        text = {
            Column(
                Modifier
                    .fillMaxWidth()
                    .heightIn(max = 460.dp)
                    .verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                OutlinedTextField(value = title, onValueChange = { title = it }, label = { Text("Tên công việc") }, modifier = Modifier.fillMaxWidth())
                OutlinedTextField(value = description, onValueChange = { description = it }, label = { Text("Mô tả / tiêu chí nghiệm thu") }, modifier = Modifier.fillMaxWidth())

                // Người nhận
                var openAssignee by remember { mutableStateOf(false) }
                ExposedDropdownMenuBox(expanded = openAssignee, onExpandedChange = { openAssignee = it }) {
                    OutlinedTextField(
                        value = assigneeLabel, onValueChange = {}, readOnly = true,
                        label = { Text("Người nhận việc") },
                        trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = openAssignee) },
                        modifier = Modifier.fillMaxWidth().menuAnchor(),
                    )
                    ExposedDropdownMenu(expanded = openAssignee, onDismissRequest = { openAssignee = false }) {
                        assignees.forEach { a ->
                            DropdownMenuItem(
                                text = { Text(a.fullName + if (a.department.isNotBlank()) " · ${a.department}" else "") },
                                onClick = { assignee = a.username; openAssignee = false },
                            )
                        }
                    }
                }

                // Ưu tiên
                var openPriority by remember { mutableStateOf(false) }
                ExposedDropdownMenuBox(expanded = openPriority, onExpandedChange = { openPriority = it }) {
                    OutlinedTextField(
                        value = priorityLabel(priority), onValueChange = {}, readOnly = true,
                        label = { Text("Mức ưu tiên") },
                        trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = openPriority) },
                        modifier = Modifier.fillMaxWidth().menuAnchor(),
                    )
                    ExposedDropdownMenu(expanded = openPriority, onDismissRequest = { openPriority = false }) {
                        PRIORITIES.forEach { p ->
                            DropdownMenuItem(text = { Text(priorityLabel(p)) }, onClick = { priority = p; openPriority = false })
                        }
                    }
                }

                OutlinedTextField(
                    value = dueText, onValueChange = { dueText = it },
                    label = { Text("Hạn hoàn thành (yyyy-MM-dd, không bắt buộc)") },
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
    )
}
