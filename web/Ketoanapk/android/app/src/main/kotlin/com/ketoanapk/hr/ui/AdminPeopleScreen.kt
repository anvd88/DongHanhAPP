package com.ketoanapk.hr.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.People
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

@Composable fun AdminPeopleScreen(vm:HrViewModel){val detail=vm.managedEmployee;if(detail!=null){BackHandler{vm.closeManagedEmployee()};ManagedEmployeeDetail(vm,detail);return};var query by remember{mutableStateOf("")};var dept by remember{mutableStateOf("")};var visible by remember(query,dept){mutableIntStateOf(20)};val rows=vm.managerState.employees.filter{(query.isBlank()||it.fullName.contains(query,true)||it.employeeCode.contains(query,true))&&(dept.isBlank()||it.departmentName==dept)}
    LazyColumn(Modifier.fillMaxSize(),contentPadding=PaddingValues(14.dp),verticalArrangement=Arrangement.spacedBy(10.dp)){
        // Dashboard / Bảng lương / Nhật ký thuộc cùng họ quản trị nên nằm ngay đây (thay ngăn kéo cũ).
        item{HrCard{HubList(destinations=vm.hubFor(HrDestination.People),onSelect=vm::select)}}
        item{OutlinedTextField(query,{query=it},label={Text("Tìm tên hoặc mã")},modifier=Modifier.fillMaxWidth())}
        item{Row(horizontalArrangement=Arrangement.spacedBy(6.dp)){FilterChip(dept.isBlank(),{dept=""},{Text("Tất cả")});vm.managerState.summary?.departments.orEmpty().take(4).forEach{d->FilterChip(dept==d.departmentName,{dept=d.departmentName},{Text(d.departmentName)})}}}
        if(vm.managerState.loading&&rows.isEmpty())item{LoadingBlock()};if(!vm.managerState.loading&&rows.isEmpty())item{EmptyState("Không có nhân viên","Thử đổi bộ lọc.")}
        items(rows.take(visible),key={it.id}){e->HrCard{Text(e.fullName,fontWeight=FontWeight.Bold);Text("${e.employeeCode} · ${e.departmentName}");Text("${e.position} · ${e.status}",style=MaterialTheme.typography.bodySmall);Button({vm.openManagedEmployee(e.id)},Modifier.fillMaxWidth()){Text("Mở chi tiết")}}}
        if(rows.size>visible)item{OutlinedButton({visible+=20},Modifier.fillMaxWidth()){Text("Tải thêm (${rows.size-visible} còn lại)")}}
    }}

@Composable private fun ManagedEmployeeDetail(vm:HrViewModel,e:com.ketoanapk.hr.data.EmployeeDetail){var position by remember(e.id){mutableStateOf(e.position)};var status by remember(e.id){mutableStateOf(e.status)};var dept by remember(e.id){mutableStateOf(e.departmentId.orEmpty())};var manager by remember(e.id){mutableStateOf(e.managerId)};var base by remember{mutableStateOf("")};var allowance by remember{mutableStateOf("")};var overtime by remember{mutableStateOf("")};var confirmProfile by remember{mutableStateOf(false)};var confirmSalary by remember{mutableStateOf(false)}
    LazyColumn(Modifier.fillMaxSize(),contentPadding=PaddingValues(14.dp),verticalArrangement=Arrangement.spacedBy(10.dp)){
        item{PageHeader(Icons.Filled.People,e.fullName,"${e.employeeCode} · ${e.departmentName}",Tone.Info)}
        item{HrCard{LabelValue("Điện thoại",e.phone);LabelValue("Email",e.email);OutlinedTextField(position,{position=it},label={Text("Chức vụ")},modifier=Modifier.fillMaxWidth());Row{listOf("Active","Inactive").forEach{x->FilterChip(status==x,{status=x},{Text(x)})}};Text("Phòng ban",fontWeight=FontWeight.Bold);vm.managerState.summary?.departments.orEmpty().forEach{d->FilterChip(dept==d.departmentId,{dept=d.departmentId.orEmpty()},{Text(d.departmentName)})};Text("Quản lý trực tiếp",fontWeight=FontWeight.Bold);FilterChip(manager==null,{manager=null},{Text("Không gán")});vm.managerState.employees.filter{it.id!=e.id}.take(8).forEach{m->FilterChip(manager==m.id,{manager=m.id},{Text(m.fullName)})};Button({confirmProfile=true},Modifier.fillMaxWidth()){Text("Lưu thay đổi hồ sơ")}}}
        item{HrCard{Text("Thiết lập lương",fontWeight=FontWeight.Bold);OutlinedTextField(base,{base=it.filter(Char::isDigit)},label={Text("Lương cơ bản")},modifier=Modifier.fillMaxWidth());OutlinedTextField(allowance,{allowance=it.filter(Char::isDigit)},label={Text("Phụ cấp")},modifier=Modifier.fillMaxWidth());OutlinedTextField(overtime,{overtime=it.filter(Char::isDigit)},label={Text("Đơn giá tăng ca")},modifier=Modifier.fillMaxWidth());Button({confirmSalary=true},Modifier.fillMaxWidth()){Text("Lưu cấu trúc lương")}}}
        item{HrCard{Text("Lịch sử thay đổi quan trọng",fontWeight=FontWeight.Bold);Text("Mọi thay đổi hồ sơ và lương được ghi vào Nhật ký hệ thống với người thao tác và thời gian.",style=MaterialTheme.typography.bodySmall)}}
    }
    if(confirmProfile)AlertDialog(onDismissRequest={confirmProfile=false},title={Text("Xác nhận thay đổi hồ sơ")},text={Text("Thao tác sẽ được ghi nhật ký hệ thống.")},confirmButton={Button({vm.updateManagedEmployee(e,dept.ifBlank{null},position,status,manager);confirmProfile=false}){Text("Xác nhận")}},dismissButton={TextButton({confirmProfile=false}){Text("Hủy")}})
    if(confirmSalary)AlertDialog(onDismissRequest={confirmSalary=false},title={Text("Xác nhận thay đổi lương")},text={Text("Đây là dữ liệu nhạy cảm và sẽ được ghi audit.")},confirmButton={Button({vm.updateManagedSalary(e.id,base.toDoubleOrNull()?:0.0,allowance.toDoubleOrNull()?:0.0,overtime.toDoubleOrNull()?:0.0);confirmSalary=false}){Text("Xác nhận")}},dismissButton={TextButton({confirmSalary=false}){Text("Hủy")}})
}
