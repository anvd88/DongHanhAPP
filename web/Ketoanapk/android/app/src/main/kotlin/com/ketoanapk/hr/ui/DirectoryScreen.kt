package com.ketoanapk.hr.ui

import android.content.Intent
import android.net.Uri
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
import androidx.compose.material.icons.filled.AccountTree
import androidx.compose.material.icons.filled.Contacts
import androidx.compose.material.icons.filled.Email
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.DirectoryContact

data class DirectoryUiState(
    val loading: Boolean = false,
    val query: String = "",
    val contacts: List<DirectoryContact> = emptyList(),
    val organizationMode: Boolean = false,
    val error: String? = null,
)

internal fun organizationContacts(contacts: List<DirectoryContact>): Pair<List<DirectoryContact>, List<DirectoryContact>> =
    contacts.filter { it.isDirectManager } to contacts.filter { it.sameDepartment && !it.isDirectManager }

@Composable
fun DirectoryScreen(vm: HrViewModel) {
    val state = vm.directoryState
    var selected by remember { mutableStateOf<DirectoryContact?>(null) }
    selected?.let { contact -> ContactProfileDialog(contact, onDismiss = { selected = null }) }

    LazyColumn(Modifier.fillMaxSize(), contentPadding = screenPadding(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            OutlinedTextField(
                value = state.query,
                onValueChange = vm::setDirectoryQuery,
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
                label = { Text("Tên, phòng ban hoặc chức vụ") },
            )
        }
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilterChip(selected = !state.organizationMode, onClick = { vm.setOrganizationMode(false) }, label = { Text("Danh sách") }, leadingIcon = { Icon(Icons.Filled.Contacts, null) })
                FilterChip(selected = state.organizationMode, onClick = { vm.setOrganizationMode(true) }, label = { Text("Sơ đồ tổ chức") }, leadingIcon = { Icon(Icons.Filled.AccountTree, null) })
            }
        }
        when {
            state.loading && state.contacts.isEmpty() -> item { LoadingBlock() }
            state.contacts.isEmpty() -> item {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    EmptyState(if (state.error != null) "Không tải được danh bạ" else "Không có kết quả", state.error ?: "Không tìm thấy đồng nghiệp phù hợp.")
                    Button(onClick = vm::refreshDirectory, modifier = Modifier.fillMaxWidth()) { Text("Thử lại") }
                }
            }
            state.organizationMode -> {
                val (managers, peers) = organizationContacts(state.contacts)
                if (managers.isNotEmpty()) {
                    item { DirectorySection("Quản lý trực tiếp") }
                    items(managers, key = { "manager-${it.username}" }) { ContactCard(it) { selected = it } }
                }
                item { DirectorySection("Cùng phòng ban") }
                if (peers.isEmpty()) item { EmptyState("Chưa có thành viên", "Không tìm thấy đồng nghiệp cùng phòng ban.") }
                items(peers, key = { "peer-${it.username}" }) { ContactCard(it) { selected = it } }
            }
            else -> items(state.contacts, key = { it.username }) { ContactCard(it) { selected = it } }
        }
    }
}

@Composable private fun DirectorySection(title: String) {
    Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 6.dp, start = 4.dp))
}

@Composable
private fun ContactCard(contact: DirectoryContact, onOpen: () -> Unit) {
    Surface(onClick = onOpen, modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(17.dp), border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline)) {
        Row(Modifier.padding(13.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            UserAvatar(contact.displayName, 44, avatar = contact.avatarUrl)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(contact.displayName, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(listOf(contact.position, contact.departmentName).filter { it.isNotBlank() }.joinToString(" · ").ifBlank { contact.role }, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            StatusChip(if (contact.isOnline) "Online" else "Offline", if (contact.isOnline) Tone.Success else Tone.Muted)
        }
    }
}

@Composable
private fun ContactProfileDialog(contact: DirectoryContact, onDismiss: () -> Unit) {
    val context = LocalContext.current
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(contact.displayName) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                LabelValue("Mã nhân viên", contact.employeeCode.ifBlank { "--" })
                LabelValue("Chức vụ", contact.position.ifBlank { "--" })
                LabelValue("Phòng ban", contact.departmentName.ifBlank { "--" })
                LabelValue("Quản lý", contact.managerName.ifBlank { "--" })
                LabelValue("Điện thoại", contact.phone.ifBlank { "Đã ẩn theo quyền riêng tư" })
                LabelValue("Email", contact.email.ifBlank { "Đã ẩn theo quyền riêng tư" })
                Row(horizontalArrangement = Arrangement.spacedBy(4.dp), modifier = Modifier.fillMaxWidth()) {
                    IconButton(
                        enabled = contact.email.isNotBlank(),
                        onClick = { context.startActivity(Intent(Intent.ACTION_SENDTO, Uri.parse("mailto:${Uri.encode(contact.email)}"))) },
                    ) { Icon(Icons.Filled.Email, "Email") }
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Đóng") } },
    )
}
