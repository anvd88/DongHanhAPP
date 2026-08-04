package com.ketoanapk.hr.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material.icons.filled.Gavel
import androidx.compose.material.icons.filled.Groups
import androidx.compose.material.icons.filled.Inbox
import androidx.compose.material.icons.filled.Language
import androidx.compose.material.icons.filled.Payments
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.PersonOff
import androidx.compose.material.icons.filled.WatchLater
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.EmployeeCard
import com.ketoanapk.hr.data.AppPermissions
import com.ketoanapk.hr.data.HrUser
import com.ketoanapk.hr.data.ManagerDepartmentStatus
import com.ketoanapk.hr.data.Penalty
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.SalaryListItem
import com.ketoanapk.hr.ui.theme.Danger
import com.ketoanapk.hr.ui.theme.InfoBlue
import com.ketoanapk.hr.ui.theme.Success
import com.ketoanapk.hr.ui.theme.Warning as WarningColor

/** Màn xử lý đơn của nhân sự trực tiếp trên Android. */
@Composable
fun StaffRequestsScreen(vm: HrViewModel) {
    val detail = vm.requestDetailState
    if (detail.id != null) {
        BackHandler { vm.closeRequestDetail() }
        RequestDetailView(
            state = detail,
            onBack = vm::closeRequestDetail,
            onCancel = {},
            onDecide = vm::decideRequest,
        )
        return
    }

    val state = vm.homeState
    val pending = state.inbox.filter { it.status.equals("Pending", true) }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        if (state.loading && state.inbox.isEmpty()) item { LoadingBlock() }
        if (state.inbox.isEmpty()) {
            item { EmptyState("Không có đơn chờ", state.error ?: "Hiện không có đơn nào của nhân sự chờ bạn xử lý.") }
        } else {
            items(state.inbox, key = { it.id }) { req -> StaffRequestCard(req) { vm.openStaffDetail(req.id) } }
        }
    }
}

@Composable
private fun StaffRequestCard(req: RequestListItem, onOpen: () -> Unit) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
        shadowElevation = 1.dp,
        onClick = onOpen,
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(req.typeLabel.ifBlank { req.title }, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${req.employeeName} · ${req.employeeCode}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("${req.requestNo} · Bước ${req.currentStep}/${req.totalSteps} · ${formatIsoDateTime(req.createdAt)}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("Bấm để xem chi tiết", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary)
            }
            StatusChip(requestStatusLabel(req.status), requestTone(req.status))
            Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
fun PenaltyScreen(user: HrUser, state: HomeUiState, onAppeal: (Penalty) -> Unit) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        if (state.loading && state.penalties.isEmpty()) item { LoadingBlock() }
        if (state.penalties.isEmpty()) {
            item { EmptyState("Không có quyết định phạt", state.error ?: "Danh sách hiện đang trống.") }
        } else {
            items(state.penalties, key = { it.id }) { p ->
                // Chỉ nhân viên mới khiếu nại/đề nghị trên án phạt TIỀN còn hiệu lực của chính mình.
                // Admin xem toàn công ty (không phải của mình) nên không hiện nút này; họ xử lý trên web.
                val canAppeal = !user.can(AppPermissions.PenaltyManage) && p.status == "Active" && p.penaltyType == "fine"
                PenaltyCard(p, canAppeal = canAppeal, onAppeal = { onAppeal(p) })
            }
        }
    }
}

@Composable
private fun PenaltyCard(p: Penalty, canAppeal: Boolean, onAppeal: () -> Unit) {
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
            if (p.installments > 1) StatusChip("Chia ${p.installments} tháng", Tone.Muted)
            StatusChip(formatIsoDate(p.penaltyDate), Tone.Muted)
        }
        if (canAppeal) {
            OutlinedButton(
                onClick = onAppeal,
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(12.dp),
            ) {
                Icon(Icons.Filled.Gavel, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(8.dp))
                Text("Khiếu nại / xin giảm · trả góp", fontWeight = FontWeight.Bold)
            }
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
        item { PageHeader(Icons.Filled.People, "Quản lý nhân sự", "Quân số, phòng ban, nhân viên", Tone.Neutral) }
        if (state.loading && state.summary == null) item { LoadingBlock() }
        state.error?.let { item { EmptyState("Không tải được dữ liệu", it) } }

        item { WorkTodayCard(h) }
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                StatTile(Icons.Filled.Groups, "Quân số", "${h?.active ?: 0}", InfoBlue, Modifier.weight(1f))
                StatTile(Icons.Filled.CheckCircle, "Có mặt", "${h?.present ?: 0}", Success, Modifier.weight(1f))
            }
        }
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                StatTile(Icons.Filled.FlightTakeoff, "Nghỉ / công tác", "${(h?.leave ?: 0) + (h?.business ?: 0)}", WarningColor, Modifier.weight(1f))
                StatTile(Icons.Filled.PersonOff, "Vắng", "${h?.absent ?: 0}", Danger, Modifier.weight(1f))
            }
        }
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                StatTile(Icons.Filled.WatchLater, "Đi muộn", "${h?.late ?: 0}", WarningColor, Modifier.weight(1f))
                StatTile(Icons.Filled.Inbox, "Đơn chờ duyệt", "${h?.pendingApprovals ?: 0}", InfoBlue, Modifier.weight(1f))
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
        if (state.loading && state.salaries.isEmpty()) item { LoadingBlock() }
        if (state.salaries.isEmpty()) {
            item { EmptyState("Không có dữ liệu lương", state.error ?: "Chưa có cấu trúc lương hoặc không có quyền xem.") }
        } else {
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                    StatTile(Icons.Filled.People, "Nhân viên", "${state.salaries.size}", InfoBlue, Modifier.weight(1f))
                    StatTile(Icons.Filled.CheckCircle, "Đã thiết lập", "${state.salaries.count { it.hasSalary }}", Success, Modifier.weight(1f))
                }
            }
            item { SectionTitle("Danh sách lương") }
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
