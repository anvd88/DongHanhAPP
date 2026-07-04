package com.ketoanapk.hr.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Groups
import androidx.compose.material.icons.filled.Inbox
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.EmployeeCard
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.ManagerDepartmentStatus
import com.ketoanapk.hr.data.Penalty
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.SalaryListItem

@Composable
fun ApprovalScreen(state: HomeUiState, onApprove: (String) -> Unit, onReject: (String) -> Unit) {
    val pending = state.inbox.filter { it.status.equals("Pending", true) }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHead("Phê duyệt", "${pending.size} đơn chờ xử lý") }
        if (state.loading && state.inbox.isEmpty()) item { LoadingBlock() }
        if (state.inbox.isEmpty()) {
            item { EmptyState("Hộp thư trống", state.error ?: "Không có đơn nào cần bạn duyệt.") }
        } else {
            items(state.inbox, key = { it.id }) { req -> ApprovalCard(req, onApprove, onReject) }
        }
    }
}

@Composable
private fun ApprovalCard(req: RequestListItem, onApprove: (String) -> Unit, onReject: (String) -> Unit) {
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text(req.typeLabel.ifBlank { req.title }, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${req.employeeName} · ${req.employeeCode}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            StatusChip(requestStatusLabel(req.status), requestTone(req.status))
        }
        Text("${req.requestNo} · Bước ${req.currentStep}/${req.totalSteps} · ${formatIsoDateTime(req.createdAt)}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        if (req.status.equals("Pending", true)) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = { onApprove(req.id) }, modifier = Modifier.weight(1f)) { Text("Duyệt") }
                OutlinedButton(onClick = { onReject(req.id) }, modifier = Modifier.weight(1f)) { Text("Từ chối") }
            }
        }
    }
}

@Composable
fun PenaltyScreen(user: HrUser, state: HomeUiState) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHead("Phạt / kỷ luật", if (user.isAdmin) "Toàn công ty" else "Của tôi") }
        if (state.loading && state.penalties.isEmpty()) item { LoadingBlock() }
        if (state.penalties.isEmpty()) {
            item { EmptyState("Không có quyết định phạt", state.error ?: "Danh sách hiện đang trống.") }
        } else {
            items(state.penalties, key = { it.id }) { p -> PenaltyCard(p) }
        }
    }
}

@Composable
private fun PenaltyCard(p: Penalty) {
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text("${p.penaltyNo} · ${p.penaltyTypeLabel}", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${p.employeeName} · ${p.employeeCode}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            StatusChip(if (p.status == "Active") "Còn hiệu lực" else p.status, if (p.status == "Active") Tone.Danger else Tone.Muted)
        }
        Text(p.reason.ifBlank { p.note.ifBlank { "Không có ghi chú" } }, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 2, overflow = TextOverflow.Ellipsis)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            if (p.amount > 0) StatusChip(formatMoney(p.amount), Tone.Warning)
            StatusChip(formatIsoDate(p.penaltyDate), Tone.Muted)
        }
    }
}

@Composable
fun ManagerScreen(state: ManagerUiState) {
    val h = state.summary?.headcount
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHead("Quản lý nhân sự", "Quân số, phòng ban, nhân viên") }
        if (state.loading && state.summary == null) item { LoadingBlock() }
        state.error?.let { item { EmptyState("Không tải được dữ liệu", it) } }

        item { WorkTodayCard(h) }
        item {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    KpiCard(Icons.Filled.Groups, "Quân số", "${h?.active ?: 0}", "Đang làm việc", modifier = Modifier.weight(1f))
                    KpiCard(Icons.Filled.Face, "Có mặt", "${h?.present ?: 0}", "${h?.late ?: 0} đi muộn", Tone.Success, modifier = Modifier.weight(1f))
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    KpiCard(Icons.Filled.Inbox, "Chờ duyệt", "${h?.pendingApprovals ?: 0}", "Đơn cần xử lý", Tone.Warning, modifier = Modifier.weight(1f))
                    KpiCard(Icons.Filled.Warning, "Cảnh báo", "${h?.alerts ?: 0}", "Cần rà soát", if ((h?.alerts ?: 0) > 0) Tone.Danger else Tone.Neutral, modifier = Modifier.weight(1f))
                }
            }
        }

        item { SectionTitle("Theo phòng ban") }
        val depts = state.summary?.departments.orEmpty()
        if (depts.isEmpty()) {
            item { EmptyState("Chưa có phòng ban", "Danh sách phòng ban đang trống.") }
        } else {
            items(depts, key = { it.departmentId ?: it.departmentName }) { d -> DepartmentCard(d) }
        }

        item { SectionTitle("Nhân viên") }
        if (state.employees.isEmpty()) {
            item { EmptyState("Chưa có nhân viên", "Danh sách nhân viên đang trống.") }
        } else {
            items(state.employees.take(50), key = { it.id }) { e -> EmployeeRow(e) }
        }
    }
}

@Composable
private fun DepartmentCard(d: ManagerDepartmentStatus) {
    HrCard {
        Text(d.departmentName.ifBlank { "Chưa gán phòng ban" }, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
        Text("${d.present}/${d.total} có mặt", style = MaterialTheme.typography.bodyMedium, color = com.ketoanapk.hr.ui.theme.Success, fontWeight = FontWeight.Bold)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            StatusChip("${d.leave + d.business} nghỉ / công tác", Tone.Warning)
            StatusChip("${d.absent} vắng", Tone.Danger)
        }
    }
}

@Composable
private fun EmployeeRow(e: EmployeeCard) {
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            UserAvatar(e.fullName.ifBlank { e.username }, 42)
            Spacer(Modifier.width(10.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(e.fullName.ifBlank { e.username }, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${e.employeeCode} · ${e.departmentName.ifBlank { "Chưa gán" }}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            StatusChip(if (e.status == "Active") "Đang làm" else e.status, if (e.status == "Active") Tone.Success else Tone.Muted)
        }
    }
}

@Composable
fun PayrollScreen(state: HomeUiState) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { PageHead("Bảng lương", "Mức lương nhân viên") }
        if (state.loading && state.salaries.isEmpty()) item { LoadingBlock() }
        if (state.salaries.isEmpty()) {
            item { EmptyState("Không có dữ liệu lương", state.error ?: "Chưa có cấu trúc lương hoặc không có quyền xem.") }
        } else {
            items(state.salaries, key = { it.employeeId }) { s -> SalaryCard(s) }
        }
    }
}

@Composable
private fun SalaryCard(s: SalaryListItem) {
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            UserAvatar(s.employeeName, 42)
            Spacer(Modifier.width(10.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(s.employeeName, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${s.employeeCode} · ${s.departmentName}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            StatusChip(if (s.hasSalary) "Đã thiết lập" else "Chưa có", if (s.hasSalary) Tone.Success else Tone.Warning)
        }
        LabelValue("Lương cơ bản", formatMoney(s.baseSalary))
        LabelValue("Phụ cấp", formatMoney(s.allowance))
        LabelValue("Đơn giá tăng ca", formatMoney(s.overtimeRate))
    }
}
