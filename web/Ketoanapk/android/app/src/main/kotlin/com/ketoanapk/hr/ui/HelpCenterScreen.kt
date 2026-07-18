package com.ketoanapk.hr.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.HelpCenter
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

@Composable fun HelpCenterScreen(vm:HrViewModel){var report by remember{mutableStateOf("")};LazyColumn(Modifier.fillMaxSize(),contentPadding=PaddingValues(14.dp),verticalArrangement=Arrangement.spacedBy(10.dp)){
    item{SectionTitle("Câu hỏi thường gặp")};listOf("Không chấm công được?" to "Kiểm tra camera, ánh sáng, vị trí và kết nối máy chủ LAN.","Không nhận thông báo?" to "Mở Cài đặt > Quyền ứng dụng và bật thông báo.","Cuộc gọi không kết nối?" to "Chạy kiểm tra TURN bên dưới và thử đổi Wi‑Fi/4G.").forEach{(q,a)->item{HrCard{Text(q,fontWeight=FontWeight.Bold);Text(a,style=MaterialTheme.typography.bodySmall)}}}
    item{Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.SpaceBetween){SectionTitle("Kiểm tra hệ thống");TextButton({vm.runDiagnostics()}){Text("Chạy lại")}}};items(vm.diagnostics.entries.toList(),key={it.key}){(k,v)->HrCard{LabelValue(k,v)}}
    item{HrCard{Text("Gửi báo lỗi",fontWeight=FontWeight.Bold);OutlinedTextField(report,{report=it},minLines=4,modifier=Modifier.fillMaxWidth(),placeholder={Text("Mô tả bước gây lỗi; không dán mật khẩu hoặc token")});Text("Ứng dụng tự đính kèm phiên bản và loại thiết bị; log không gửi token/mật khẩu.",style=MaterialTheme.typography.bodySmall);Button(enabled=report.isNotBlank(),onClick={vm.createSupportTicket(report);report=""},modifier=Modifier.fillMaxWidth()){Text("Gửi báo lỗi")}}}
    item{SectionTitle("Yêu cầu hỗ trợ")};if(vm.supportTickets.isEmpty())item{EmptyState("Chưa có yêu cầu","Mã và trạng thái sẽ hiển thị sau khi gửi.")}else items(vm.supportTickets,key={it.id}){t->HrCard{Text(t.code,fontWeight=FontWeight.Bold);StatusChip(t.status,if(t.status=="resolved")Tone.Success else Tone.Warning);Text(t.message);if(t.response.isNotBlank())Text(t.response)}}
}}
