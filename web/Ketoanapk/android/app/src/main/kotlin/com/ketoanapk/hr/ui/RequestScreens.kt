package com.ketoanapk.hr.ui

import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.AccountBalanceWallet
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.BeachAccess
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Cancel
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.EditCalendar
import androidx.compose.material.icons.filled.Fingerprint
import androidx.compose.material.icons.filled.Flight
import androidx.compose.material.icons.filled.Gavel
import androidx.compose.material.icons.filled.HourglassEmpty
import androidx.compose.material.icons.filled.MedicalServices
import androidx.compose.material.icons.filled.MeetingRoom
import androidx.compose.material.icons.filled.MoreTime
import androidx.compose.material.icons.filled.Payments
import androidx.compose.material.icons.filled.ReceiptLong
import androidx.compose.material.icons.filled.AttachFile
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.filled.ShoppingCart
import androidx.compose.material.icons.filled.SwapHoriz
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ketoanapk.hr.data.Penalty
import com.ketoanapk.hr.data.RequestApproval
import com.ketoanapk.hr.data.RequestHead
import com.ketoanapk.hr.data.RequestListItem
import com.ketoanapk.hr.data.RequestType
import com.ketoanapk.hr.data.RequestDraftStore
import com.ketoanapk.hr.ui.theme.Warning
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import java.time.temporal.ChronoUnit

/**
 * Trang "Đơn từ" của nhân viên trên app native. Ba trạng thái con:
 *  1. Danh sách đơn của tôi + nút "Tạo đơn mới" nổi bật.
 *  2. Luồng tạo đơn: chọn loại (thẻ to, icon, mô tả dễ hiểu) → điền form động (chọn ngày/giờ bằng
 *     lịch/đồng hồ, tự tính số ngày nghỉ, gợi ý từng ô) → gửi.
 *  3. Chi tiết một đơn: trạng thái nổi bật + tiến trình phê duyệt từng bước.
 * Thiết kế ưu tiên nút to, chữ rõ, hướng dẫn đời thường để nhân viên ít rành công nghệ dùng được.
 */
@Composable
fun RequestsScreen(vm: HrViewModel) {
    val detail = vm.requestDetailState
    var creating by rememberSaveable { mutableStateOf(false) }

    // Mở thẳng luồng tạo đơn khi có "nháp khiếu nại" từ màn Kỷ luật (bấm đề nghị trên một quyết định phạt).
    val appealDraft = vm.appealDraft
    val requestDraftType = vm.requestDraftType
    LaunchedEffect(appealDraft, requestDraftType) { if (appealDraft != null || requestDraftType != null) creating = true }
    // Khi rời luồng tạo đơn thì bỏ nháp để lần sau vào "Tạo đơn mới" bình thường (không dính án phạt cũ).
    fun leaveCreate() { creating = false; vm.consumeAppealDraft(); vm.consumeRequestDraft() }

    when {
        detail.id != null -> {
            BackHandler { vm.closeRequestDetail() }
            RequestDetailView(
                state = detail,
                onBack = vm::closeRequestDetail,
                onCancel = vm::cancelFromDetail,
                onCopy = vm::copyRequest,
                onEdit = vm::editRequest,
                onRemind = vm::remindRequest,
            )
        }
        creating -> {
            BackHandler { leaveCreate() }
            // Một deep-link mới có thể tới khi state lưu của màn Đơn từ vẫn giữ form trước đó. Key theo
            // dữ liệu nháp để Compose dựng lại form và áp dụng chính xác ngày/hướng chấm công mới.
            androidx.compose.runtime.key(requestDraftType, vm.requestDraftValues, appealDraft, vm.requestDraftNonce) {
                CreateRequestFlow(
                    types = vm.homeState.requestTypes,
                    penalties = vm.homeState.penalties,
                    saving = vm.creatingRequest,
                    initialDraft = appealDraft,
                    initialType = requestDraftType,
                    initialValues = vm.requestDraftValues,
                    draftAccountId = vm.currentAccountId,
                    restoreSavedDraft = vm.requestDraftRestoreSaved,
                    onClose = { leaveCreate() },
                    onSubmit = { type, title, payload, attachments ->
                        vm.submitRequest(type, title, payload, attachments) { ok -> if (ok) leaveCreate() }
                    },
                )
            }
        }
        else -> RequestListView(
            state = vm.homeState,
            hub = vm.hubFor(HrDestination.Requests),
            badgeCount = vm::badgeCount,
            onSelect = vm::select,
            onCreate = { creating = true },
            onOpen = vm::openRequestDetail,
        )
    }
}

// ────────────────────────────── 1. Danh sách đơn của tôi ──────────────────────────────

@Composable
private fun RequestListView(
    state: HomeUiState,
    hub: List<HrDestination>,
    badgeCount: (HrDestination) -> Int,
    onSelect: (HrDestination) -> Unit,
    onCreate: () -> Unit,
    onOpen: (String) -> Unit,
) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { CreateRequestButton(onCreate) }
        // Đơn chờ duyệt / Kỷ luật thuộc cùng họ với Đơn từ nên nằm ngay đây (thay ngăn kéo cũ).
        if (hub.isNotEmpty()) {
            item { HrCard { HubList(destinations = hub, badgeCount = badgeCount, onSelect = onSelect) } }
        }
        item {
            Text(
                "Đơn của tôi",
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurface,
                modifier = Modifier.padding(top = 4.dp, start = 4.dp),
            )
        }
        if (state.loading && state.requests.isEmpty()) {
            item { LoadingBlock() }
        } else if (state.requests.isEmpty()) {
            item { EmptyState("Chưa có đơn nào", "Bấm \"Tạo đơn mới\" ở trên để gửi đơn đầu tiên của bạn.") }
        } else {
            items(state.requests, key = { it.id }) { req -> RequestListCard(req, onOpen) }
        }
    }
}

/** Nút tạo đơn to, màu thương hiệu, dễ thấy — điểm nhấn chính của trang. */
@Composable
private fun CreateRequestButton(onCreate: () -> Unit) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(22.dp),
        color = MaterialTheme.colorScheme.primary,
        shadowElevation = 3.dp,
        onClick = onCreate,
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 18.dp, vertical = 18.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .clip(CircleShape)
                    .background(Color.White.copy(alpha = 0.22f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Filled.Add, contentDescription = null, tint = Color.White, modifier = Modifier.size(30.dp))
            }
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text("Tạo đơn mới", fontSize = 19.sp, fontWeight = FontWeight.ExtraBold, color = Color.White)
                Text(
                    "Nghỉ phép, tăng ca, tạm ứng, quên chấm công…",
                    fontSize = 13.sp,
                    color = Color.White.copy(alpha = 0.9f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = Color.White)
        }
    }
}

@Composable
private fun RequestListCard(req: RequestListItem, onOpen: (String) -> Unit) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
        shadowElevation = 1.dp,
        onClick = { onOpen(req.id) },
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            RequestTypeIcon(req.type, size = 46)
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(
                    req.typeLabel.ifBlank { req.title },
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.onSurface,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    "${req.requestNo} · ${formatIsoDateTime(req.createdAt)}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (req.status.equals("Pending", true) && req.totalSteps > 0) {
                    Text(
                        "Đang chờ duyệt · bước ${req.currentStep}/${req.totalSteps}",
                        style = MaterialTheme.typography.bodySmall,
                        color = toneColor(Tone.Warning),
                    )
                }
            }
            StatusChip(requestStatusLabel(req.status), requestTone(req.status))
        }
    }
}

// ────────────────────────────── 2. Luồng tạo đơn ──────────────────────────────

@Composable
private fun CreateRequestFlow(
    types: List<RequestType>,
    penalties: List<Penalty>,
    saving: Boolean,
    initialDraft: AppealDraft?,
    initialType: String?,
    initialValues: Map<String, String>,
    draftAccountId: String,
    restoreSavedDraft: Boolean,
    onClose: () -> Unit,
    onSubmit: (type: String, title: String, payload: kotlinx.serialization.json.JsonObject, attachments: List<android.net.Uri>) -> Unit,
) {
    // Có nháp khiếu nại → mở thẳng form "penalty_appeal", bỏ qua bước chọn loại.
    var selectedType by rememberSaveable { mutableStateOf(initialDraft?.let { "penalty_appeal" } ?: initialType) }
    val chosen = types.find { it.type == selectedType }

    // Giá trị điền sẵn cho form khiếu nại đến từ án phạt được chọn ở màn Kỷ luật.
    val draftValues: Map<String, String> = if (chosen?.type == "penalty_appeal" && initialDraft != null) {
        mapOf(
            "penaltyNo" to initialDraft.penaltyNo,
            "penaltyType" to initialDraft.penaltyTypeLabel,
            "penaltyAmount" to (if (initialDraft.amount > 0) initialDraft.amount.toLong().toString() else ""),
        )
    } else initialValues

    if (chosen == null) {
        RequestTypePicker(types = types, onBack = onClose, onPick = { selectedType = it.type })
    } else {
        // Đổi loại đơn → dựng lại form với state sạch.
        androidx.compose.runtime.key(chosen.type) {
            RequestFormStep(
                type = chosen,
                penalties = penalties,
                saving = saving,
                initialValues = draftValues,
                draftAccountId = draftAccountId,
                restoreSavedDraft = restoreSavedDraft,
                // Từ nháp khiếu nại: nút quay lại thoát hẳn luồng (không có bước chọn loại để lùi về).
                onBack = { if (initialDraft != null || initialType != null) onClose() else selectedType = null },
                onSubmit = { title, payload, attachments -> onSubmit(chosen.type, title, payload, attachments) },
            )
        }
    }
}

/** Bước 1: chọn loại đơn — nhóm theo danh mục, mỗi loại là một thẻ to có icon + mô tả dễ hiểu. */
@Composable
private fun RequestTypePicker(
    types: List<RequestType>,
    onBack: () -> Unit,
    onPick: (RequestType) -> Unit,
) {
    // Gom nhóm theo danh mục, giữ thứ tự xuất hiện đầu tiên.
    val grouped = LinkedHashMap<String, MutableList<RequestType>>()
    types.forEach { grouped.getOrPut(it.category.ifBlank { "Khác" }) { mutableListOf() }.add(it) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item { FlowHeader(title = "Bạn muốn tạo đơn gì?", subtitle = "Chọn một loại đơn bên dưới", onBack = onBack) }
        if (types.isEmpty()) {
            item { EmptyState("Đang tải danh sách", "Vui lòng kéo xuống để làm mới nếu chờ lâu.") }
        }
        grouped.forEach { (category, list) ->
            item(key = "cat-$category") { SectionTitle(category, modifier = Modifier.padding(start = 4.dp, top = 4.dp)) }
            items(list, key = { it.type }) { t -> RequestTypeCard(t, onPick) }
        }
    }
}

@Composable
private fun RequestTypeCard(type: RequestType, onPick: (RequestType) -> Unit) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
        shadowElevation = 1.dp,
        onClick = { onPick(type) },
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            RequestTypeIcon(type.type, size = 48)
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(type.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
                Text(
                    requestTypeDescription(type.type),
                    fontSize = 13.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

/** Bước 2: điền form động cho loại đơn đã chọn. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun RequestFormStep(
    type: RequestType,
    penalties: List<Penalty>,
    saving: Boolean,
    initialValues: Map<String, String>,
    draftAccountId: String,
    restoreSavedDraft: Boolean,
    onBack: () -> Unit,
    onSubmit: (title: String, payload: kotlinx.serialization.json.JsonObject, attachments: List<android.net.Uri>) -> Unit,
) {
    val allFields = fieldsForType(type.type)
    var showErrors by remember { mutableStateOf(false) }
    val auto = daysAutoSynced(type.type)

    // Khiếu nại án phạt: chỉ cho chọn án phạt TIỀN còn hiệu lực của chính nhân viên (nạp sẵn ở home).
    val isPenaltyAppeal = type.type == "penalty_appeal"
    val activePenalties = remember(penalties) {
        penalties.filter { it.status.equals("Active", true) && it.penaltyType == "fine" }
    }

    // Điền sẵn giá trị (nếu đến từ nháp khiếu nại) và mặc định hình thức đề nghị = "Bỏ phạt".
    val values = remember {
        mutableStateMapOf<String, String>().apply {
            putAll(initialValues)
            if (isPenaltyAppeal && this["appealKind"].isNullOrBlank()) this["appealKind"] = "dispute"
        }
    }
    val context = LocalContext.current
    val draftStore = remember(draftAccountId) { RequestDraftStore(context, draftAccountId) }
    val draftScope = rememberCoroutineScope()
    var draftLoaded by remember { mutableStateOf(false) }
    val attachmentUris = remember { mutableStateListOf<android.net.Uri>() }
    val attachmentPicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        attachmentUris.clear()
        attachmentUris.addAll(uris.take(8))
    }
    val signaturePoints = remember { mutableStateListOf<Offset>() }
    LaunchedEffect(type.type, draftAccountId, restoreSavedDraft) {
        if (restoreSavedDraft) {
            val saved = draftStore.load(type.type)
            saved.forEach { (key, value) -> if (values[key].isNullOrBlank()) values[key] = value }
        }
        draftLoaded = true
    }
    LaunchedEffect(values.toMap(), draftLoaded) {
        if (draftLoaded) { delay(500); draftStore.save(type.type, values.toMap()) }
    }

    // Với khiếu nại: chỉ hiện ô "Số tiền đề nghị" khi Giảm tiền, ô "Số tháng" khi Trả góp.
    val appealKind = values["appealKind"].orEmpty()
    val fields = if (isPenaltyAppeal) {
        allFields.filter { f ->
            when (f.key) {
                "requestedAmount" -> appealKind == "reduce"
                "requestedMonths" -> appealKind == "installment"
                else -> true
            }
        }
    } else {
        allFields
    }

    fun isAutoDays(key: String) = auto && key == "days"

    fun setField(key: String, value: String) {
        values[key] = value
        if (auto && (key == "fromDate" || key == "toDate")) {
            val d = inclusiveDays(values["fromDate"], values["toDate"])
            values["days"] = d?.toString() ?: ""
        }
    }

    val missing = fields.filter { it.required && !isAutoDays(it.key) && values[it.key].isNullOrBlank() }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .imePadding()
            .navigationBarsPadding()
            .padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        FlowHeader(title = "Điền thông tin đơn", subtitle = null, onBack = onBack)

        // Thẻ tóm tắt loại đơn đang tạo + đổi loại.
        Surface(
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(18.dp),
            color = MaterialTheme.colorScheme.primaryContainer,
        ) {
            Row(
                modifier = Modifier.padding(14.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                RequestTypeIcon(type.type, size = 44)
                Column(modifier = Modifier.weight(1f)) {
                    Text(type.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onPrimaryContainer)
                    Text(requestTypeDescription(type.type), fontSize = 12.sp, color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.85f))
                }
                TextButton(onClick = onBack) { Text("Đổi loại") }
            }
        }

        fields.forEach { field ->
            if (isPenaltyAppeal && field.key == "penaltyNo") {
                PenaltyPickerField(
                    field = field,
                    value = values[field.key].orEmpty(),
                    isError = showErrors && field.required && values[field.key].isNullOrBlank(),
                    penalties = activePenalties,
                    onPicked = { p ->
                        // Chọn 1 quyết định phạt → tự điền cả mã, hình thức và số tiền phạt.
                        setField("penaltyNo", p.penaltyNo)
                        setField("penaltyType", p.penaltyTypeLabel.ifBlank { p.penaltyType })
                        setField("penaltyAmount", if (p.amount > 0) p.amount.toLong().toString() else "")
                    },
                )
            } else {
                // Hình thức phạt & số tiền phạt được điền tự động theo quyết định → chỉ đọc.
                val autoFilled = isPenaltyAppeal && (field.key == "penaltyType" || field.key == "penaltyAmount")
                FieldEditor(
                    field = field,
                    value = values[field.key].orEmpty(),
                    readOnly = isAutoDays(field.key) || autoFilled,
                    isError = showErrors && field.required && !isAutoDays(field.key) && values[field.key].isNullOrBlank(),
                    onChange = { setField(field.key, it) },
                )
            }
        }

        Surface(shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.surfaceVariant, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Tệp đính kèm", fontWeight = FontWeight.Bold)
                Text("Ảnh, PDF hoặc tài liệu; tối đa 8 tệp, 15 MB/tệp.", style = MaterialTheme.typography.bodySmall)
                attachmentUris.forEach { uri -> Text("• ${requestFileName(context, uri)}", style = MaterialTheme.typography.bodySmall) }
                TextButton(onClick = { attachmentPicker.launch(arrayOf("image/*", "application/pdf", "text/*", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")) }) {
                    Icon(Icons.Filled.AttachFile, contentDescription = null); Text("Chọn tệp")
                }
            }
        }

        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text("Ký xác nhận trên màn hình (không bắt buộc)", fontWeight = FontWeight.Bold)
            Canvas(
                Modifier.fillMaxWidth().height(150.dp).background(Color.White, RoundedCornerShape(12.dp))
                    .border(1.dp, MaterialTheme.colorScheme.outline, RoundedCornerShape(12.dp))
                    .pointerInput(Unit) {
                        detectDragGestures(
                            onDragStart = { signaturePoints.add(it) },
                            onDrag = { change, _ -> signaturePoints.add(change.position); change.consume() },
                        )
                    },
            ) {
                signaturePoints.zipWithNext().forEach { (a, b) -> drawLine(Color.Black, a, b, strokeWidth = 4f) }
            }
            TextButton(onClick = { signaturePoints.clear() }) { Text("Xóa chữ ký") }
        }

        if (showErrors && missing.isNotEmpty()) {
            Text(
                "Vui lòng điền: " + missing.joinToString(", ") { it.label },
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.error,
            )
        }

        Button(
            onClick = {
                if (missing.isNotEmpty()) {
                    showErrors = true
                } else {
                    val payload = buildJsonObject {
                        fields.forEach { f ->
                            val raw = values[f.key]?.trim().orEmpty()
                            if (raw.isNotEmpty()) put(f.key, jsonValueFor(f, raw))
                        }
                        if (signaturePoints.isNotEmpty()) {
                            put("requesterSignature", JsonPrimitive(signaturePoints.joinToString(";") { "${it.x.toInt()},${it.y.toInt()}" }))
                        }
                    }
                    draftScope.launch { draftStore.clear(type.type) }
                    onSubmit("", payload, attachmentUris.toList())
                }
            },
            enabled = !saving,
            modifier = Modifier
                .fillMaxWidth()
                .height(54.dp),
            shape = RoundedCornerShape(16.dp),
        ) {
            if (saving) {
                CircularProgressIndicator(Modifier.size(22.dp), MaterialTheme.colorScheme.onPrimary, 2.dp)
            } else {
                Icon(Icons.AutoMirrored.Filled.Send, contentDescription = null, modifier = Modifier.size(20.dp))
                Spacer(Modifier.width(8.dp))
                Text("Gửi đơn", fontSize = 17.sp, fontWeight = FontWeight.ExtraBold)
            }
        }
        Spacer(Modifier.height(8.dp))
    }
}

private fun requestFileName(context: android.content.Context, uri: android.net.Uri): String {
    context.contentResolver.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
        if (c.moveToFirst()) c.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME).takeIf { it >= 0 }?.let { return c.getString(it) ?: "Tệp đính kèm" }
    }
    return uri.lastPathSegment ?: "Tệp đính kèm"
}

/** Một ô nhập trong form, tự chọn kiểu hiển thị theo loại trường. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun FieldEditor(
    field: RequestField,
    value: String,
    readOnly: Boolean,
    isError: Boolean,
    onChange: (String) -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row {
            Text(field.label, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
            if (field.required) Text(" *", color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.Bold)
        }

        when (field.type) {
            RfType.Date -> PickerBox(
                display = if (value.isBlank()) "Chọn ngày" else formatIsoDate(value),
                placeholder = value.isBlank(),
                icon = Icons.Filled.CalendarMonth,
                isError = isError,
                enabled = !readOnly,
            ) { open -> DatePickerPopup(value, onPicked = onChange, control = open) }

            RfType.Time -> PickerBox(
                display = if (value.isBlank()) "Chọn giờ" else value,
                placeholder = value.isBlank(),
                icon = Icons.Filled.Schedule,
                isError = isError,
                enabled = true,
            ) { open -> TimePickerPopup(value, onPicked = onChange, control = open) }

            RfType.Segmented -> Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                field.options.forEach { (optValue, optLabel) ->
                    ChoiceChip(
                        label = optLabel,
                        selected = value == optValue,
                        modifier = Modifier.weight(1f),
                    ) { onChange(optValue) }
                }
            }

            RfType.Money -> OutlinedTextField(
                value = groupThousands(value),
                onValueChange = { onChange(it.filter { c -> c.isDigit() }) },
                modifier = Modifier.fillMaxWidth(),
                readOnly = readOnly,
                singleLine = true,
                isError = isError,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                trailingIcon = { Text("₫", fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurfaceVariant) },
                shape = RoundedCornerShape(14.dp),
            )

            RfType.Number -> OutlinedTextField(
                value = value,
                onValueChange = { onChange(it.filter { c -> c.isDigit() }) },
                modifier = Modifier.fillMaxWidth(),
                readOnly = readOnly,
                singleLine = true,
                isError = isError,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                shape = RoundedCornerShape(14.dp),
            )

            RfType.Textarea -> OutlinedTextField(
                value = value,
                onValueChange = onChange,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(110.dp),
                isError = isError,
                shape = RoundedCornerShape(14.dp),
            )

            RfType.Text -> OutlinedTextField(
                value = value,
                onValueChange = onChange,
                modifier = Modifier.fillMaxWidth(),
                readOnly = readOnly,
                singleLine = true,
                isError = isError,
                shape = RoundedCornerShape(14.dp),
            )
        }

        if (field.hint.isNotBlank()) {
            Text(field.hint, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

/**
 * Ô chọn "Mã quyết định phạt" cho đơn khiếu nại: nhân viên bấm để mở danh sách các án phạt TIỀN còn
 * hiệu lực của chính mình rồi chọn một mã (không gõ tay để tránh sai mã). Hình thức phạt và số tiền
 * phạt tự điền theo quyết định đã chọn.
 */
@Composable
private fun PenaltyPickerField(
    field: RequestField,
    value: String,
    isError: Boolean,
    penalties: List<Penalty>,
    onPicked: (Penalty) -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row {
            Text(field.label, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
            if (field.required) Text(" *", color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.Bold)
        }

        val selected = penalties.find { it.penaltyNo == value }
        PickerBox(
            display = when {
                selected != null -> "${selected.penaltyNo} · ${selected.penaltyTypeLabel.ifBlank { selected.penaltyType }}"
                value.isNotBlank() -> value
                else -> "Chọn án phạt tiền còn hiệu lực"
            },
            placeholder = selected == null && value.isBlank(),
            icon = Icons.Filled.Gavel,
            isError = isError,
            enabled = true,
        ) { control -> PenaltyPickerDialog(penalties, onPicked = onPicked, control = control) }

        val hint = if (penalties.isEmpty()) "Bạn không có án phạt tiền còn hiệu lực để khiếu nại." else field.hint
        if (hint.isNotBlank()) {
            Text(hint, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

/** Hộp thoại liệt kê các án phạt tiền còn hiệu lực để nhân viên chọn (mã · hình thức · số tiền · lý do). */
@Composable
private fun PenaltyPickerDialog(
    penalties: List<Penalty>,
    onPicked: (Penalty) -> Unit,
    control: MutableControl,
) {
    AlertDialog(
        onDismissRequest = control.dismiss,
        confirmButton = {},
        dismissButton = { TextButton(onClick = control.dismiss) { Text("Đóng") } },
        title = { Text("Chọn án phạt") },
        text = {
            if (penalties.isEmpty()) {
                Text("Bạn không có án phạt tiền còn hiệu lực để khiếu nại.")
            } else {
                Column(
                    modifier = Modifier
                        .heightIn(max = 360.dp)
                        .verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    penalties.forEach { p ->
                        Surface(
                            modifier = Modifier.fillMaxWidth(),
                            shape = RoundedCornerShape(12.dp),
                            color = MaterialTheme.colorScheme.surface,
                            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
                            onClick = { onPicked(p); control.dismiss() },
                        ) {
                            Column(
                                modifier = Modifier.padding(12.dp),
                                verticalArrangement = Arrangement.spacedBy(2.dp),
                            ) {
                                Text(
                                    "${p.penaltyNo} · ${p.penaltyTypeLabel.ifBlank { p.penaltyType }}",
                                    fontWeight = FontWeight.Bold,
                                    color = MaterialTheme.colorScheme.onSurface,
                                )
                                if (p.amount > 0) {
                                    Text(
                                        "${formatMoney(p.amount)} ₫",
                                        fontSize = 13.sp,
                                        color = toneColor(Tone.Danger),
                                        fontWeight = FontWeight.Bold,
                                    )
                                }
                                if (p.reason.isNotBlank()) {
                                    Text(
                                        p.reason,
                                        fontSize = 13.sp,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                        maxLines = 2,
                                        overflow = TextOverflow.Ellipsis,
                                    )
                                }
                            }
                        }
                    }
                }
            }
        },
    )
}

/** Ô bấm mở lịch/đồng hồ, hiển thị giá trị đã chọn. */
@Composable
private fun PickerBox(
    display: String,
    placeholder: Boolean,
    icon: ImageVector,
    isError: Boolean,
    enabled: Boolean,
    dialog: @Composable (control: MutableControl) -> Unit,
) {
    var open by remember { mutableStateOf(false) }
    val borderColor = when {
        isError -> MaterialTheme.colorScheme.error
        else -> MaterialTheme.colorScheme.outline
    }
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(14.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, borderColor),
        onClick = { if (enabled) open = true },
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(22.dp))
            Text(
                display,
                modifier = Modifier.weight(1f),
                fontSize = 16.sp,
                color = if (placeholder) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface,
            )
        }
    }
    if (open) dialog(MutableControl { open = false })
}

/** Điều khiển nhỏ để hộp thoại tự đóng. */
private class MutableControl(val dismiss: () -> Unit)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DatePickerPopup(current: String, onPicked: (String) -> Unit, control: MutableControl) {
    val initMillis = runCatching {
        LocalDate.parse(current).atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
    }.getOrNull()
    val pickerState = rememberDatePickerState(initialSelectedDateMillis = initMillis)
    DatePickerDialog(
        onDismissRequest = control.dismiss,
        confirmButton = {
            TextButton(onClick = {
                pickerState.selectedDateMillis?.let { millis ->
                    val date = Instant.ofEpochMilli(millis).atZone(ZoneOffset.UTC).toLocalDate()
                    onPicked(date.toString())
                }
                control.dismiss()
            }) { Text("Chọn") }
        },
        dismissButton = { TextButton(onClick = control.dismiss) { Text("Hủy") } },
    ) {
        DatePicker(state = pickerState)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TimePickerPopup(current: String, onPicked: (String) -> Unit, control: MutableControl) {
    val parts = current.split(":")
    val initHour = parts.getOrNull(0)?.toIntOrNull() ?: 8
    val initMinute = parts.getOrNull(1)?.toIntOrNull() ?: 0
    val pickerState = rememberTimePickerState(initialHour = initHour, initialMinute = initMinute, is24Hour = true)
    AlertDialog(
        onDismissRequest = control.dismiss,
        confirmButton = {
            TextButton(onClick = {
                onPicked("%02d:%02d".format(pickerState.hour, pickerState.minute))
                control.dismiss()
            }) { Text("Chọn") }
        },
        dismissButton = { TextButton(onClick = control.dismiss) { Text("Hủy") } },
        title = { Text("Chọn giờ") },
        text = {
            Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                TimePicker(state = pickerState)
            }
        },
    )
}

/** Nút lựa chọn kiểu "viên thuốc" to, dễ bấm (dùng cho trường Segmented). */
@Composable
private fun ChoiceChip(label: String, selected: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val bg = if (selected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface
    val border = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outline
    val fg = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface
    Surface(
        modifier = modifier
            .height(50.dp)
            .semantics {
                role = Role.RadioButton
                this.selected = selected
                stateDescription = if (selected) "Đã chọn" else "Chưa chọn"
            },
        shape = RoundedCornerShape(14.dp),
        color = bg,
        border = BorderStroke(if (selected) 2.dp else 1.dp, border),
        onClick = onClick,
    ) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            Text(label, fontSize = 16.sp, fontWeight = FontWeight.Bold, color = fg)
        }
    }
}

// ────────────────────────────── 3. Chi tiết đơn ──────────────────────────────

@Composable
internal fun RequestDetailView(
    state: RequestDetailUiState,
    onBack: () -> Unit,
    onCancel: (String) -> Unit,
    onCopy: (com.ketoanapk.hr.data.RequestDetail) -> Unit = {},
    onEdit: (com.ketoanapk.hr.data.RequestDetail) -> Unit = {},
    onRemind: (String) -> Unit = {},
    onDecide: (String, Boolean, String) -> Unit = { _, _, _ -> },
) {
    var decision by rememberSaveable { mutableStateOf<Boolean?>(null) }
    var comment by rememberSaveable { mutableStateOf("") }

    decision?.let { approve ->
        AlertDialog(
            onDismissRequest = { if (!state.deciding) decision = null },
            title = { Text(if (approve) "Xác nhận duyệt đơn" else "Xác nhận từ chối") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text(if (approve) "Đơn sẽ chuyển sang bước tiếp theo hoặc hoàn tất." else "Người gửi sẽ thấy đơn bị từ chối cùng nhận xét của bạn.")
                    OutlinedTextField(
                        value = comment,
                        onValueChange = { comment = it },
                        label = { Text("Nhận xét") },
                        placeholder = { Text(if (approve) "Không bắt buộc" else "Nêu rõ lý do từ chối") },
                        enabled = !state.deciding,
                        minLines = 3,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            },
            confirmButton = {
                Button(
                    modifier = Modifier.testTag(if (approve) "confirm_approve" else "confirm_reject"),
                    enabled = !state.deciding && (approve || comment.isNotBlank()),
                    onClick = {
                        state.id?.let { onDecide(it, approve, comment) }
                        decision = null
                        comment = ""
                    },
                    colors = ButtonDefaults.buttonColors(
                        containerColor = if (approve) toneColor(Tone.Success) else MaterialTheme.colorScheme.error,
                    ),
                ) {
                    if (state.deciding) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                    else Text(if (approve) "Duyệt" else "Từ chối")
                }
            },
            dismissButton = { TextButton(enabled = !state.deciding, onClick = { decision = null }) { Text("Hủy") } },
        )
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        item { FlowHeader(title = "Chi tiết đơn", subtitle = null, onBack = onBack) }

        when {
            state.loading -> item { LoadingBlock() }
            state.error != null -> item { EmptyState("Không mở được đơn", state.error) }
            state.detail != null -> {
                val head = state.detail.request
                item { StatusBanner(head.status) }
                item { RequestInfoCard(head) }
                if (state.detail.attachments.isNotEmpty()) {
                    item {
                        HrCard {
                            Text("Tệp đính kèm", fontWeight = FontWeight.Bold)
                            state.detail.attachments.forEach { file ->
                                Text("• ${file.fileName} · ${file.fileSize / 1024} KB", style = MaterialTheme.typography.bodySmall)
                            }
                        }
                    }
                }
                item {
                    Text(
                        "Tiến trình phê duyệt",
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onSurface,
                        modifier = Modifier.padding(start = 4.dp, top = 4.dp),
                    )
                }
                items(state.detail.approvals, key = { it.stepNo }) { a -> ApprovalStepCard(a) }
                if (state.canCancel) {
                    item {
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                            OutlinedButton(onClick = { onCopy(state.detail) }, modifier = Modifier.weight(1f)) { Text("Sao chép") }
                            if (head.status.equals("Pending", true)) OutlinedButton(onClick = { onEdit(state.detail) }, modifier = Modifier.weight(1f)) { Text("Sửa đơn") }
                        }
                    }
                }

                if (state.decisionError != null) {
                    item { EmptyState("Không xử lý được đơn", state.decisionError) }
                }

                if (head.status.equals("Pending", true) && state.canDecide) {
                    item {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            Button(
                                onClick = { decision = false },
                                enabled = !state.deciding,
                                modifier = Modifier.weight(1f).height(50.dp),
                                shape = RoundedCornerShape(16.dp),
                                colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                            ) { Text("Từ chối", fontWeight = FontWeight.Bold) }
                            Button(
                                onClick = { decision = true },
                                enabled = !state.deciding,
                                modifier = Modifier.weight(1f).height(50.dp),
                                shape = RoundedCornerShape(16.dp),
                                colors = ButtonDefaults.buttonColors(containerColor = toneColor(Tone.Success)),
                            ) { Text("Duyệt", fontWeight = FontWeight.Bold) }
                        }
                    }
                }

                // Chỉ đơn của chính mình còn chờ duyệt mới cho hủy. Đơn của nhân sự khác là chỉ đọc.
                if (head.status.equals("Pending", true) && state.canCancel) {
                    item {
                        OutlinedButton(onClick = { onRemind(head.id) }, modifier = Modifier.fillMaxWidth()) { Text("Nhắc người duyệt") }
                    }
                    item {
                        Button(
                            onClick = { onCancel(head.id) },
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(50.dp),
                            shape = RoundedCornerShape(16.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                        ) {
                            Icon(Icons.Filled.Cancel, contentDescription = null, modifier = Modifier.size(20.dp))
                            Spacer(Modifier.width(8.dp))
                            Text("Hủy đơn này", fontWeight = FontWeight.Bold)
                        }
                    }
                }
            }
        }
    }
}

/** Băng trạng thái lớn, màu theo kết quả (chờ/duyệt/từ chối/hủy). */
@Composable
private fun StatusBanner(status: String) {
    val tone = requestTone(status)
    val color = toneColor(tone)
    val icon = when (status.lowercase()) {
        "approved" -> Icons.Filled.CheckCircle
        "rejected" -> Icons.Filled.Cancel
        "cancelled", "canceled" -> Icons.Filled.Cancel
        else -> Icons.Filled.HourglassEmpty
    }
    val message = when (status.lowercase()) {
        "approved" -> "Đơn của bạn đã được duyệt."
        "rejected" -> "Đơn của bạn đã bị từ chối."
        "cancelled", "canceled" -> "Đơn này đã được hủy."
        else -> "Đơn đang chờ người có thẩm quyền duyệt."
    }
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = color.copy(alpha = 0.12f),
        border = BorderStroke(1.dp, color.copy(alpha = 0.4f)),
    ) {
        Row(
            modifier = Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Icon(icon, contentDescription = null, tint = color, modifier = Modifier.size(34.dp))
            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(requestStatusLabel(status), fontSize = 18.sp, fontWeight = FontWeight.ExtraBold, color = color)
                Text(message, fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurface)
            }
        }
    }
}

@Composable
private fun RequestInfoCard(head: RequestHead) {
    val fields = fieldsForType(head.type)
    val payload = head.payload
    // Các khóa đã có trong định nghĩa (giữ thứ tự) + khóa lạ còn lại (nếu backend gửi thêm).
    val orderedKeys = fields.map { it.key }.filter { payload.containsKey(it) }
    val extraKeys = payload.keys.filter { it !in orderedKeys }

    HrCard {
        Text(head.title.ifBlank { head.typeLabel }, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
        Text("${head.requestNo} · gửi ${formatIsoDateTime(head.createdAt)}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        head.dueAt?.takeIf { it.isNotBlank() }?.let { Text("Hạn xử lý: ${formatIsoDateTime(it)}", style = MaterialTheme.typography.bodySmall, color = Warning) }
        HorizontalDivider(color = MaterialTheme.colorScheme.outline)
        LabelValue("Người gửi", head.employeeName + (if (head.employeeCode.isNotBlank()) " (${head.employeeCode})" else ""))
        if (head.departmentName.isNotBlank()) LabelValue("Phòng ban", head.departmentName)
        orderedKeys.forEach { key -> LabelValue(fieldLabel(head.type, key), payload.displayValue(head.type, key)) }
        extraKeys.forEach { key -> LabelValue(key, payload.readString(key)) }
    }
}

@Composable
private fun ApprovalStepCard(a: RequestApproval) {
    val tone = requestTone(a.status)
    val color = toneColor(tone)
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Box(
                modifier = Modifier
                    .size(32.dp)
                    .clip(CircleShape)
                    .background(color.copy(alpha = 0.15f)),
                contentAlignment = Alignment.Center,
            ) {
                Text("${a.stepNo}", fontWeight = FontWeight.ExtraBold, color = color)
            }
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        a.approverName.ifBlank { if (a.approverRole == "Admin") "Quản trị viên / HR" else a.approverUsername.ifBlank { "Người duyệt" } },
                        modifier = Modifier.weight(1f),
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    StatusChip(requestStatusLabel(a.status), tone)
                }
                if (!a.status.equals("Pending", true)) {
                    val who = a.decidedBy.ifBlank { "" }
                    val time = a.decidedAt?.let { formatIsoDateTime(it) } ?: ""
                    val line = listOf(who, time).filter { it.isNotBlank() }.joinToString(" · ")
                    if (line.isNotBlank()) {
                        Text(
                            line + (if (a.hasSignature) " · đã ký" else ""),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
                if (a.comment.isNotBlank()) {
                    Text("“${a.comment}”", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurface)
                }
            }
        }
    }
}

// ────────────────────────────── Dùng chung ──────────────────────────────

/** Thanh đầu trang có nút quay lại to, dễ bấm. */
@Composable
private fun FlowHeader(title: String, subtitle: String?, onBack: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Surface(
            shape = CircleShape,
            color = MaterialTheme.colorScheme.surface,
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
            onClick = onBack,
        ) {
            Icon(
                Icons.AutoMirrored.Filled.ArrowBack,
                contentDescription = "Quay lại",
                tint = MaterialTheme.colorScheme.onSurface,
                modifier = Modifier.padding(9.dp).size(22.dp),
            )
        }
        Column {
            Text(title, style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.onSurface)
            if (subtitle != null) Text(subtitle, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

/** Icon tròn theo loại đơn (nền đỏ nhạt thương hiệu). */
@Composable
private fun RequestTypeIcon(type: String, size: Int) {
    Box(
        modifier = Modifier
            .size(size.dp)
            .clip(CircleShape)
            .background(MaterialTheme.colorScheme.primaryContainer),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            requestTypeIcon(type),
            contentDescription = null,
            tint = MaterialTheme.colorScheme.primary,
            modifier = Modifier.size((size * 0.5f).dp),
        )
    }
}

private fun requestTypeIcon(type: String): ImageVector = when (type) {
    "leave" -> Icons.Filled.BeachAccess
    "sick" -> Icons.Filled.MedicalServices
    "business_trip" -> Icons.Filled.Flight
    "overtime" -> Icons.Filled.MoreTime
    "attendance_fix" -> Icons.Filled.EditCalendar
    "forgot_checkin" -> Icons.Filled.Fingerprint
    "shift_swap" -> Icons.Filled.SwapHoriz
    "payment" -> Icons.Filled.Payments
    "advance" -> Icons.Filled.AccountBalanceWallet
    "reimbursement" -> Icons.Filled.ReceiptLong
    "purchase" -> Icons.Filled.ShoppingCart
    "booking" -> Icons.Filled.MeetingRoom
    "penalty_appeal" -> Icons.Filled.Gavel
    else -> Icons.Filled.Description
}

private fun requestTypeDescription(type: String): String = when (type) {
    "leave" -> "Xin nghỉ có phép (phép năm, việc riêng)"
    "sick" -> "Xin nghỉ vì ốm đau, đi khám bệnh"
    "business_trip" -> "Báo đi công tác, làm việc xa"
    "overtime" -> "Đăng ký làm thêm ngoài giờ"
    "attendance_fix" -> "Sửa lại giờ vào/ra bị chấm sai"
    "forgot_checkin" -> "Báo quên chấm công để được bù"
    "shift_swap" -> "Đổi ca hoặc nhờ người nhận ca giúp"
    "payment" -> "Đề nghị công ty thanh toán một khoản"
    "advance" -> "Xin ứng trước một khoản tiền lương"
    "reimbursement" -> "Khai hóa đơn, số đã chi và số cần hoàn ứng"
    "purchase" -> "Đề nghị mua vật tư, đồ dùng"
    "booking" -> "Đăng ký dùng xe hoặc phòng họp"
    "penalty_appeal" -> "Khiếu nại khi bị phạt chưa hợp lý"
    else -> "Gửi yêu cầu tới quản lý"
}

/** Số ngày (tính cả 2 đầu) giữa hai ngày yyyy-MM-dd; null nếu thiếu/không hợp lệ/đảo ngược. */
private fun inclusiveDays(from: String?, to: String?): Int? {
    if (from.isNullOrBlank() || to.isNullOrBlank()) return null
    return runCatching {
        val a = LocalDate.parse(from)
        val b = LocalDate.parse(to)
        if (b.isBefore(a)) null else (ChronoUnit.DAYS.between(a, b).toInt() + 1)
    }.getOrNull()
}

/** Nhóm 3 chữ số cho tiền: "1500000" → "1.500.000". */
private fun groupThousands(digits: String): String {
    val n = digits.filter { it.isDigit() }.trimStart('0')
    if (n.isEmpty()) return ""
    return n.reversed().chunked(3).joinToString(".").reversed()
}
