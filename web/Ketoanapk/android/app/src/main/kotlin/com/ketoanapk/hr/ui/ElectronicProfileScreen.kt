package com.ketoanapk.hr.ui

import android.content.Context
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.core.content.FileProvider
import com.ketoanapk.hr.data.EmployeeDocument
import com.ketoanapk.hr.ui.theme.Warning
import java.io.File
import java.time.LocalDate
import java.time.temporal.ChronoUnit

@Composable
fun ElectronicProfileScreen(vm: HrViewModel) {
    var documentsTab by rememberSaveable { mutableStateOf(false) }
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.padding(horizontal = 16.dp, vertical = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            if (documentsTab) OutlinedButton({ documentsTab = false }, Modifier.weight(1f)) { Text("Thông tin") }
            else Button({ documentsTab = false }, Modifier.weight(1f)) { Text("Thông tin") }
            if (documentsTab) Button({ documentsTab = true; vm.loadProfileDocuments() }, Modifier.weight(1f)) { Text("Giấy tờ") }
            else OutlinedButton({ documentsTab = true; vm.loadProfileDocuments() }, Modifier.weight(1f)) { Text("Giấy tờ") }
        }
        if (documentsTab) ProfileDocuments(vm) else ProfileScreen(vm.homeState, vm::startPortraitCapture)
    }
}

@Composable
private fun ProfileDocuments(vm: HrViewModel) {
    val context = LocalContext.current
    val username = (vm.authState as? AuthState.SignedIn)?.user?.username.orEmpty()
    var unlocked by rememberSaveable { mutableStateOf(false) }
    var showAdd by remember { mutableStateOf(false) }
    var showPin by remember { mutableStateOf(false) }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = screenPadding(16.dp, 16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item { PageHeader(Icons.Filled.Folder, "Hồ sơ điện tử", "CCCD, hợp đồng, bằng cấp và chứng chỉ", Tone.Info) }
        item {
            OutlinedButton(onClick = {
                showPin = true
            }, modifier = Modifier.fillMaxWidth(), enabled = username.isNotBlank() && !unlocked) {
                Icon(Icons.Filled.Lock, null)
                Spacer(Modifier.width(8.dp))
                Text(if (unlocked) "Đã mở dữ liệu nhạy cảm" else "Mở bằng mã bảo mật ứng dụng")
            }
        }
        if (vm.profileDocumentsLoading && vm.profileDocuments.isEmpty()) item { LoadingBlock() }
        if (!vm.profileDocumentsLoading && vm.profileDocuments.isEmpty()) item { EmptyState("Chưa có giấy tờ", "Thêm CCCD, hợp đồng, bằng cấp, chứng chỉ hoặc liên hệ khẩn cấp.") }
        items(vm.profileDocuments, key = { it.id }) { DocumentCard(it, unlocked) }
        item { Button(onClick = { showAdd = true }, modifier = Modifier.fillMaxWidth()) { Text("Thêm / cập nhật hồ sơ") } }
    }
    if (showAdd) AddDocumentDialog(vm, context) { showAdd = false }
    AppPinGate(
        visible = showPin,
        username = username,
        purpose = "Xác thực để xem đầy đủ số giấy tờ và dữ liệu nhạy cảm.",
        onDismiss = { showPin = false },
        onUnlocked = { unlocked = true; showPin = false },
    )
}

@Composable
private fun DocumentCard(doc: EmployeeDocument, unlocked: Boolean) {
    val days = doc.expiresAt?.let { runCatching { ChronoUnit.DAYS.between(LocalDate.now(), LocalDate.parse(it.take(10))) }.getOrNull() }
    HrCard {
        Text(doc.title.ifBlank { doc.docType }, fontWeight = FontWeight.Bold)
        Text("Trạng thái: ${if (doc.approvalStatus == "pending") "Chờ HR duyệt" else "Đã duyệt"}", style = MaterialTheme.typography.bodySmall)
        if (doc.docNumber.isNotBlank()) LabelValue("Số giấy tờ", if (unlocked) doc.docNumber else maskDocumentNumber(doc.docNumber))
        if (doc.issuedBy.isNotBlank()) LabelValue("Nơi cấp", doc.issuedBy)
        doc.expiresAt?.let { LabelValue("Hết hạn", formatIsoDate(it)) }
        if (days != null && days <= 60) Text(if (days < 0) "Đã hết hạn ${-days} ngày" else "Sắp hết hạn sau $days ngày", color = Warning, fontWeight = FontWeight.Bold)
        if (doc.fileName.isNotBlank()) Text("Tệp: ${doc.fileName}", style = MaterialTheme.typography.bodySmall)
    }
}

internal fun maskDocumentNumber(value: String): String = if (value.length <= 4) "••••" else "•".repeat(value.length - 4) + value.takeLast(4)

@Composable
private fun AddDocumentDialog(vm: HrViewModel, context: Context, close: () -> Unit) {
    var type by remember { mutableStateOf("cccd") }; var title by remember { mutableStateOf("") }
    var number by remember { mutableStateOf("") }; var expiry by remember { mutableStateOf("") }; var issuedBy by remember { mutableStateOf("") }
    var uri by remember { mutableStateOf<android.net.Uri?>(null) }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri = it }
    val camera = rememberLauncherForActivityResult(ActivityResultContracts.TakePicturePreview()) { bitmap ->
        if (bitmap != null) {
            val dir = File(context.cacheDir, "profile").apply { mkdirs() }
            val file = File(dir, "profile-${System.currentTimeMillis()}.jpg")
            file.outputStream().use { bitmap.compress(android.graphics.Bitmap.CompressFormat.JPEG, 88, it) }
            uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
        }
    }
    AlertDialog(onDismissRequest = close, title = { Text("Thêm hồ sơ") }, text = {
        Column(Modifier.heightIn(max = 520.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                listOf("cccd" to "CCCD", "contract" to "HĐ", "degree" to "Bằng", "certificate" to "Chứng chỉ", "emergency_contact" to "Khẩn cấp").forEach { (v,l) -> FilterChip(type==v,{type=v},{Text(l)}) }
            }
            OutlinedTextField(title,{title=it},label={Text("Tên hồ sơ / người liên hệ")},modifier=Modifier.fillMaxWidth())
            OutlinedTextField(number,{number=it},label={Text("Số giấy tờ / số điện thoại")},modifier=Modifier.fillMaxWidth())
            OutlinedTextField(issuedBy,{issuedBy=it},label={Text("Nơi cấp / quan hệ")},modifier=Modifier.fillMaxWidth())
            DateField("Ngày hết hạn", expiry, { expiry = it }, Modifier.fillMaxWidth(), supportingText = "Để trống nếu giấy tờ không có hạn", placeholder = "Không có hạn")
            Row { TextButton({ picker.launch(arrayOf("image/*","application/pdf")) }) { Text("Tải tệp") }; TextButton({ camera.launch(null) }) { Text("Chụp ảnh") } }
            uri?.let { Text("Đã chọn tệp", style = MaterialTheme.typography.bodySmall) }
        }
    }, confirmButton = { Button(enabled=title.isNotBlank(),onClick={ vm.uploadProfileDocument(uri,type,title,number,expiry,issuedBy); close() }) { Text("Gửi HR duyệt") } }, dismissButton = { TextButton(close) { Text("Hủy") } })
}
