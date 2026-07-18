package com.ketoanapk.hr.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.AuditEntry

private val auditEntities = listOf(
    "" to "Tất cả",
    "Auth" to "Đăng nhập",
    "User" to "Tài khoản",
    "Request" to "Đơn từ",
    "ChamCong" to "Chấm công",
    "Payroll" to "Lương",
    "PortalPost" to "Cổng tin",
)

@Composable
fun AuditScreen(vm: HrViewModel) {
    val state = vm.auditState
    var selected by remember { mutableStateOf<AuditEntry?>(null) }

    selected?.let { entry ->
        AlertDialog(
            onDismissRequest = { selected = null },
            title = { Text(entry.action.ifBlank { "Chi tiết thay đổi" }) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    AuditValue("Người thao tác", entry.username)
                    AuditValue("Thời gian", formatIsoDateTime(entry.occurredAt))
                    AuditValue("Đối tượng", listOf(entry.entity, entry.entityName).filter { it.isNotBlank() }.joinToString(" · "))
                    AuditValue("Nội dung thay đổi", entry.details.ifBlank { "Không có nội dung bổ sung." })
                }
            },
            confirmButton = { TextButton(onClick = { selected = null }) { Text("Đóng") } },
        )
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            OutlinedTextField(
                value = state.query,
                onValueChange = vm::setAuditQuery,
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                label = { Text("Tìm người, hành động hoặc đối tượng") },
                leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                keyboardActions = KeyboardActions(onSearch = { vm.searchAudit() }),
            )
        }
        item {
            Row(
                modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                auditEntities.forEach { (key, label) ->
                    FilterChip(selected = state.entity == key, onClick = { vm.setAuditEntity(key) }, label = { Text(label) })
                }
            }
        }
        if (state.loading) {
            item { LoadingBlock() }
        } else if (state.items.isEmpty()) {
            item {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    EmptyState(
                        if (state.error != null) "Không tải được nhật ký" else "Không có dữ liệu",
                        state.error ?: "Không tìm thấy thao tác phù hợp với bộ lọc.",
                    )
                    Button(onClick = vm::searchAudit, modifier = Modifier.fillMaxWidth()) { Text("Thử lại") }
                }
            }
        } else {
            itemsIndexed(state.items, key = { index, item -> "${item.occurredAt}-${item.username}-${item.action}-$index" }) { _, entry ->
                AuditCard(entry) { selected = entry }
            }
            if (state.error != null) {
                item {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(state.error, color = MaterialTheme.colorScheme.error)
                        Button(onClick = vm::loadMoreAudit, modifier = Modifier.fillMaxWidth()) { Text("Thử tải thêm lại") }
                    }
                }
            } else if (state.hasMore) {
                item {
                    Button(onClick = vm::loadMoreAudit, enabled = !state.loadingMore, modifier = Modifier.fillMaxWidth()) {
                        if (state.loadingMore) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                        else Text("Tải thêm")
                    }
                }
            }
        }
    }
}

@Composable
private fun AuditCard(entry: AuditEntry, onOpen: () -> Unit) {
    Surface(
        onClick = onOpen,
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
    ) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(entry.action, modifier = Modifier.weight(1f), fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(formatIsoDateTime(entry.occurredAt), style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Text(entry.username.ifBlank { "Hệ thống" }, color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.bodyMedium)
            val target = listOf(entry.entity, entry.entityName).filter { it.isNotBlank() }.joinToString(" · ")
            if (target.isNotBlank()) Text(target, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (entry.details.isNotBlank()) Text(entry.details, maxLines = 2, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.bodySmall)
        }
    }
}

@Composable
private fun AuditValue(label: String, value: String) {
    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Text(label, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(value.ifBlank { "--" }, style = MaterialTheme.typography.bodyMedium)
    }
}
