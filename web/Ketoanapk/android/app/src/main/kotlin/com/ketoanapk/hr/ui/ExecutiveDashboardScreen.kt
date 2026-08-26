package com.ketoanapk.hr.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Dashboard
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.ui.theme.*
import java.time.LocalDate

@Composable
fun ExecutiveDashboardScreen(vm: HrViewModel) {
    val h = vm.managerState.summary?.headcount
    var dept by remember { mutableStateOf<String?>(null) }
    val selected = vm.dashboardStatus
    LazyColumn(Modifier.fillMaxSize(), contentPadding = screenPadding(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                OutlinedButton({ vm.refreshDashboard(selected, dept, LocalDate.parse(vm.dashboardDate).minusDays(1).toString()) }, Modifier.weight(1f)) { Text("Ngày trước") }
                Text(vm.dashboardDate, Modifier.padding(top = 12.dp))
                OutlinedButton({ vm.refreshDashboard(selected, dept, LocalDate.parse(vm.dashboardDate).plusDays(1).coerceAtMost(LocalDate.now()).toString()) }, Modifier.weight(1f)) { Text("Ngày sau") }
            }
        }
        item { Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) { FilterChip(dept == null, { dept=null;vm.refreshDashboard(selected,null) }, { Text("Toàn công ty") }); vm.managerState.summary?.departments.orEmpty().take(4).forEach { d -> FilterChip(dept==d.departmentId,{dept=d.departmentId;vm.refreshDashboard(selected,dept)},{Text(d.departmentName)}) } } }
        item { Row(horizontalArrangement=Arrangement.spacedBy(8.dp),modifier=Modifier.fillMaxWidth()){DashboardMetric("Quân số",h?.active?:0,InfoBlue){vm.refreshDashboard(null,dept)};DashboardMetric("Có mặt",h?.present?:0,Success){vm.refreshDashboard("present",dept)}} }
        item { Row(horizontalArrangement=Arrangement.spacedBy(8.dp),modifier=Modifier.fillMaxWidth()){DashboardMetric("Đi muộn",h?.late?:0,Warning){vm.refreshDashboard("late",dept)};DashboardMetric("Nghỉ",(h?.leave?:0)+(h?.business?:0),BrandRed){vm.refreshDashboard("leave",dept)}} }
        item { Row(horizontalArrangement=Arrangement.spacedBy(8.dp),modifier=Modifier.fillMaxWidth()){DashboardMetric("Vắng",h?.absent?:0,Danger){vm.refreshDashboard("absent",dept)};DashboardMetric("Đơn chờ",h?.pendingApprovals?:0,InfoBlue){vm.select(HrDestination.Approval)}} }
        if ((h?.alerts ?: 0) > 0) item { HrCard { Text("Cảnh báo bất thường",fontWeight=FontWeight.Bold,color=Danger);Text("Có ${h?.alerts} cảnh báo cần kiểm tra (vắng, đi muộn, hợp đồng hoặc phân ca).") } }
        item { SectionTitle("Xu hướng có mặt 7 ngày") }
        item { HrCard { vm.dashboardTrend.forEach { (day,count) -> Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.SpaceBetween){Text("Ngày $day");Text("$count",fontWeight=FontWeight.Bold,color=Success)} } } }
        item { SectionTitle("Biểu đồ theo phòng ban") }
        items(vm.managerState.summary?.departments.orEmpty(),key={it.departmentName}) { d -> HrCard { Text(d.departmentName,fontWeight=FontWeight.Bold);LinearProgressIndicator(progress={if(d.total==0)0f else d.present.toFloat()/d.total},modifier=Modifier.fillMaxWidth());Text("Có mặt ${d.present}/${d.total} · Vắng ${d.absent}",style=MaterialTheme.typography.bodySmall) } }
        item { SectionTitle("Danh sách chi tiết${selected?.let{" · $it"}?:""}") }
        if(vm.dashboardAttendance.isEmpty()) item { EmptyState("Không có dữ liệu","Không có nhân viên phù hợp bộ lọc.") }
        else items(vm.dashboardAttendance,key={it.employeeId}) { e -> HrCard { Text(e.employeeName,fontWeight=FontWeight.Bold);Text("${e.employeeCode} · ${e.departmentName}");Text("${e.statusLabel} · ${e.checkIn.ifBlank{"--:--"}} - ${e.checkOut.ifBlank{"--:--"}}",style=MaterialTheme.typography.bodySmall) } }
    }
}

@Composable private fun RowScope.DashboardMetric(label:String,value:Int,color:androidx.compose.ui.graphics.Color,onClick:()->Unit){Surface(onClick=onClick,modifier=Modifier.weight(1f),shape=androidx.compose.foundation.shape.RoundedCornerShape(16.dp),color=color.copy(alpha=.12f)){Column(Modifier.padding(12.dp)){Text(value.toString(),style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.Bold,color=color);Text(label)}}}
