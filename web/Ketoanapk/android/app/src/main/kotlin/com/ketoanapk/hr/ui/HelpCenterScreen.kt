package com.ketoanapk.hr.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Poll
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

@Composable
fun HelpCenterScreen(vm: HrViewModel) {
    var report by remember { mutableStateOf("") }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = screenPadding(),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        // Khảo sát & phản hồi đã chuyển khỏi nhóm "Công ty" trên Trang chủ vào đúng trung tâm hỗ trợ.
        item {
            HrCard {
                Icon(Icons.Filled.Poll, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                Text("Khảo sát & phản hồi", fontWeight = FontWeight.Bold)
                Text(
                    "Tham gia khảo sát nội bộ hoặc gửi phản hồi cho công ty.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Button(
                    onClick = { vm.select(HrDestination.Feedback) },
                    modifier = Modifier.fillMaxWidth(),
                ) { Text("Mở khảo sát & phản hồi") }
            }
        }

        item { SectionTitle("Câu hỏi thường gặp") }
        listOf(
            "Không chấm công được?" to "Kiểm tra camera, ánh sáng, vị trí và kết nối máy chủ LAN.",
            "Không nhận thông báo?" to "Mở Cài đặt > Quyền ứng dụng và bật thông báo.",
            "Cuộc gọi không kết nối?" to "Chạy kiểm tra TURN bên dưới và thử đổi Wi‑Fi/4G.",
        ).forEach { (question, answer) ->
            item {
                HrCard {
                    Text(question, fontWeight = FontWeight.Bold)
                    Text(answer, style = MaterialTheme.typography.bodySmall)
                }
            }
        }

        item {
            HrCard {
                Text("Kiểm tra hệ thống", fontWeight = FontWeight.Bold)
                Button(onClick = vm::runDiagnostics, modifier = Modifier.fillMaxWidth()) { Text("Chạy lại") }
            }
        }
        items(vm.diagnostics.entries.toList(), key = { it.key }) { (key, value) ->
            HrCard { LabelValue(key, value) }
        }

        item {
            HrCard {
                Text("Gửi báo lỗi", fontWeight = FontWeight.Bold)
                OutlinedTextField(
                    value = report,
                    onValueChange = { report = it },
                    minLines = 4,
                    modifier = Modifier.fillMaxWidth(),
                    placeholder = { Text("Mô tả bước gây lỗi; không dán mật khẩu hoặc token") },
                )
                Text(
                    "Ứng dụng tự đính kèm phiên bản và loại thiết bị; log không gửi token/mật khẩu.",
                    style = MaterialTheme.typography.bodySmall,
                )
                Button(
                    enabled = report.isNotBlank(),
                    onClick = { vm.createSupportTicket(report); report = "" },
                    modifier = Modifier.fillMaxWidth(),
                ) { Text("Gửi báo lỗi") }
            }
        }

        item { SectionTitle("Yêu cầu hỗ trợ") }
        if (vm.supportTickets.isEmpty()) {
            item { EmptyState("Chưa có yêu cầu", "Mã và trạng thái sẽ hiển thị sau khi gửi.") }
        } else {
            items(vm.supportTickets, key = { it.id }) { ticket ->
                HrCard {
                    Text(ticket.code, fontWeight = FontWeight.Bold)
                    StatusChip(ticket.status, if (ticket.status == "resolved") Tone.Success else Tone.Warning)
                    Text(ticket.message)
                    if (ticket.response.isNotBlank()) Text(ticket.response)
                }
            }
        }
    }
}
