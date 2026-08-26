package com.ketoanapk.hr.ui

import androidx.compose.foundation.background
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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Payments
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.PriceCheck
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import com.ketoanapk.hr.data.CashCollection
import com.ketoanapk.hr.data.CashCollectionCustomer
import com.ketoanapk.hr.data.CashCollectionDriver
import com.ketoanapk.hr.data.CreateCashCollectionBody
import java.time.LocalDate
import java.time.ZoneId

private val CashDenominations = listOf(500_000L, 200_000L, 100_000L, 50_000L, 20_000L, 10_000L, 5_000L, 2_000L, 1_000L, 500L, 200L, 100L)

@Composable
fun CashCollectionScreen(vm: HrViewModel) {
    val state = vm.cashCollectionState
    var showAll by remember { mutableStateOf(false) }
    var creating by remember { mutableStateOf(false) }
    var counting by remember { mutableStateOf<Pair<CashCollection, Boolean>?>(null) }
    var reasonTarget by remember { mutableStateOf<Pair<CashCollection, String>?>(null) }
    var resolving by remember { mutableStateOf<CashCollection?>(null) }
    val activeStatuses = setOf("Assigned", "Accepted", "PendingHandover", "Variance")
    val visible = state.items.filter { showAll || it.status in activeStatuses }
    val holding = state.items.filter { it.status in setOf("PendingHandover", "Variance") }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = screenPadding(),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        state.message?.let { item { CashNotice(it, success = true, vm::clearCashCollectionMessage) } }
        state.error?.let { item { CashNotice(it, success = false, vm::clearCashCollectionMessage) } }

        if (vm.canReadAllCollections) {
            item {
                HrCard {
                    Text("Tiền đang ở tài xế", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(formatMoney(holding.sumOf { it.collectedAmount ?: 0.0 }), style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Black)
                    Text("${holding.size} lệnh chờ bàn giao · ${holding.count { it.overdue }} quá hạn", style = MaterialTheme.typography.bodySmall, color = if (holding.any { it.overdue }) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }

        item {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                FilterChip(selected = !showAll, onClick = { showAll = false }, label = { Text("Đang xử lý") })
                FilterChip(selected = showAll, onClick = { showAll = true }, label = { Text("Tất cả") })
                Spacer(Modifier.weight(1f))
                IconButton(onClick = { vm.loadCashCollections(silent = state.items.isNotEmpty()) }) {
                    Icon(Icons.Filled.Refresh, contentDescription = "Làm mới")
                }
            }
        }

        if (vm.canCreateCashCollection) {
            item {
                Button(
                    onClick = { creating = true },
                    modifier = Modifier.fillMaxWidth(),
                    enabled = !state.busy,
                    shape = RoundedCornerShape(14.dp),
                ) {
                    Icon(Icons.Filled.Add, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(8.dp))
                    Text("Tạo lệnh thu tiền", fontWeight = FontWeight.Bold)
                }
            }
        }

        if (state.loading && state.items.isEmpty()) item {
            Box(Modifier.fillMaxWidth().height(160.dp), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
        }
        if (!state.loading && visible.isEmpty()) item {
            HrCard {
                Text(if (vm.canReadAllCollections) "Chưa có lệnh thu tiền phù hợp." else "Bạn chưa được giao lệnh thu tiền nào.", fontWeight = FontWeight.Bold)
                Text("Lệnh mới và thay đổi bàn giao sẽ tự cập nhật khi có kết nối.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        items(visible, key = { it.id }) { order ->
            CashCollectionCard(
                order = order,
                busy = state.busy,
                onAccept = { vm.acceptCashCollection(order) },
                onCollect = { counting = order to false },
                onReceive = { counting = order to true },
                onFail = { reasonTarget = order to "fail" },
                onCancel = { reasonTarget = order to "cancel" },
                onResolve = { resolving = order },
            )
        }
    }

    if (creating) {
        CreateCashCollectionDialog(
            customers = state.customers,
            drivers = state.drivers,
            busy = state.busy,
            onCreate = { body -> vm.createCashCollection(body) { creating = false } },
            onClose = { if (!state.busy) creating = false },
        )
    }

    counting?.let { (order, cashier) ->
        CashCountDialog(
            order = order,
            cashier = cashier,
            busy = state.busy,
            onConfirm = { quantities, reason ->
                if (cashier) vm.receiveCashCollection(order, quantities) { counting = null }
                else vm.collectCashCollection(order, quantities, reason) { counting = null }
            },
            onClose = { if (!state.busy) counting = null },
        )
    }

    reasonTarget?.let { (order, action) ->
        CashReasonDialog(
            title = if (action == "fail") "Không thu được tiền" else "Hủy ${order.orderNo}",
            label = if (action == "fail") "Lý do khách chưa thanh toán *" else "Lý do hủy lệnh *",
            confirmLabel = if (action == "fail") "Gửi kế toán" else "Hủy lệnh",
            onConfirm = { reason ->
                if (action == "fail") vm.failCashCollection(order, reason) else vm.cancelCashCollection(order, reason)
                reasonTarget = null
            },
            onClose = { reasonTarget = null },
        )
    }

    resolving?.let { order ->
        CashResolveDialog(
            order = order,
            busy = state.busy,
            onConfirm = { action, reason -> vm.resolveCashCollection(order, action, reason) { resolving = null } },
            onClose = { if (!state.busy) resolving = null },
        )
    }
}

@Composable
private fun CashCollectionCard(
    order: CashCollection,
    busy: Boolean,
    onAccept: () -> Unit,
    onCollect: () -> Unit,
    onReceive: () -> Unit,
    onFail: () -> Unit,
    onCancel: () -> Unit,
    onResolve: () -> Unit,
) {
    val (label, color) = cashStatus(order)
    HrCard {
        Row(verticalAlignment = Alignment.Top, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Surface(shape = RoundedCornerShape(12.dp), color = color.copy(alpha = .13f)) {
                Icon(Icons.Filled.PriceCheck, contentDescription = null, tint = color, modifier = Modifier.padding(10.dp).size(22.dp))
            }
            Column(Modifier.weight(1f)) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                    Text(order.orderNo, fontWeight = FontWeight.Black)
                    Surface(shape = RoundedCornerShape(99.dp), color = color.copy(alpha = .13f)) {
                        Text((if (order.overdue) "Quá hạn · " else "") + label, color = color, style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.Bold, modifier = Modifier.padding(horizontal = 9.dp, vertical = 5.dp))
                    }
                }
                Text(order.customerName, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                if (order.customerPhone.isNotBlank()) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Filled.Phone, contentDescription = null, modifier = Modifier.size(14.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        Spacer(Modifier.width(4.dp)); Text(order.customerPhone, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }
        HorizontalDivider(Modifier.padding(vertical = 10.dp))
        CashInfoRow("Tài xế", order.driverName)
        CashInfoRow("Ngày đi thu", formatIsoDate(order.scheduledDate))
        CashInfoRow("Hạn bàn giao", formatIsoDateTime(order.handoverDueAt))
        CashInfoRow("Số tiền dự kiến", formatMoney(order.expectedAmount), strong = true)
        order.collectedAmount?.let { CashInfoRow("Tài xế đang giữ", formatMoney(it), strong = true, color = if (order.overdue) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary) }
        order.receivedAmount?.let { CashInfoRow("Thủ quỹ kiểm đếm", formatMoney(it), strong = true, color = if (order.status == "Variance") MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary) }
        if (order.expectedVariance) Text("Số thực thu đang lệch so với số dự kiến và phải được Kế toán trưởng duyệt.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 6.dp))
        if (order.note.isNotBlank()) Text(order.note, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 6.dp))
        if (order.failureReason.isNotBlank()) Text("Lý do: ${order.failureReason}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error, modifier = Modifier.padding(top = 6.dp))

        if (order.canAccept || order.canCollect || order.canFail || order.canReceive || order.canCancel || order.canResolve) {
            Row(Modifier.fillMaxWidth().padding(top = 10.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (order.canAccept) Button(onClick = onAccept, enabled = !busy, modifier = Modifier.weight(1f)) { Text("Nhận lệnh") }
                if (order.canCollect) Button(onClick = onCollect, enabled = !busy, modifier = Modifier.weight(1f)) { Icon(Icons.Filled.Payments, null, Modifier.size(17.dp)); Spacer(Modifier.width(5.dp)); Text("Đã thu tiền") }
                if (order.canReceive) Button(onClick = onReceive, enabled = !busy, modifier = Modifier.weight(1f)) { Text("Kiểm đếm") }
                if (order.canResolve) Button(onClick = onResolve, enabled = !busy, modifier = Modifier.weight(1f)) { Text("Xử lý lệch") }
                if (order.canFail) OutlinedButton(onClick = onFail, enabled = !busy) { Text("Không thu được") }
                if (order.canCancel) OutlinedButton(onClick = onCancel, enabled = !busy) { Text("Hủy") }
            }
        }
    }
}

@Composable
private fun CashInfoRow(label: String, value: String, strong: Boolean = false, color: Color = MaterialTheme.colorScheme.onSurface) {
    Row(Modifier.fillMaxWidth().padding(vertical = 2.dp), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(label, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(value, style = MaterialTheme.typography.bodySmall, fontWeight = if (strong) FontWeight.Black else FontWeight.Medium, color = color)
    }
}

private fun cashStatus(order: CashCollection): Pair<String, Color> = when (order.status) {
    "Assigned" -> "Chờ nhận lệnh" to Color(0xFF2563EB)
    "Accepted" -> "Đã nhận lệnh" to Color(0xFF7C3AED)
    "PendingHandover" -> "Chờ bàn giao" to Color(0xFFD97706)
    "Variance" -> "Sai lệch tiền" to Color(0xFFE11D48)
    "Failed" -> "Không thu được" to Color(0xFF64748B)
    "Completed" -> "Đã nộp đủ tiền" to Color(0xFF059669)
    "Cancelled" -> "Đã hủy" to Color(0xFF64748B)
    else -> order.status to Color(0xFF64748B)
}

@Composable
private fun CashCountDialog(
    order: CashCollection,
    cashier: Boolean,
    busy: Boolean,
    onConfirm: (Map<Long, Int>, String) -> Unit,
    onClose: () -> Unit,
) {
    var quantities by remember(order.id, cashier) { mutableStateOf<Map<Long, String>>(emptyMap()) }
    var reason by remember(order.id, cashier) { mutableStateOf("") }
    val normalized = quantities.mapValues { it.value.toIntOrNull()?.coerceAtLeast(0) ?: 0 }
    val total = CashDenominations.sumOf { it * (normalized[it] ?: 0) }
    val driverTotal = (order.collectedAmount ?: 0.0).toLong()
    val difference = total - driverTotal
    val expectedDifference = total - order.expectedAmount.toLong()

    Dialog(onDismissRequest = onClose) {
        Surface(shape = RoundedCornerShape(24.dp), tonalElevation = 8.dp, modifier = Modifier.fillMaxWidth().heightIn(max = 760.dp)) {
            Column(Modifier.padding(18.dp)) {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(if (cashier) "Thủ quỹ kiểm đếm" else "Xác nhận đã thu tiền", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Black)
                        Text("${order.orderNo} · ${order.customerName}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    IconButton(onClick = onClose, enabled = !busy) { Icon(Icons.Filled.Close, "Đóng") }
                }
                if (cashier) {
                    Surface(shape = RoundedCornerShape(14.dp), color = MaterialTheme.colorScheme.tertiaryContainer, modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)) {
                        Column(Modifier.padding(12.dp)) {
                            Text("Tài xế xác nhận", style = MaterialTheme.typography.labelMedium)
                            Text(formatMoney(driverTotal.toDouble()), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Black)
                        }
                    }
                } else {
                    Surface(shape = RoundedCornerShape(14.dp), color = MaterialTheme.colorScheme.secondaryContainer, modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)) {
                        Column(Modifier.padding(12.dp)) {
                            Text("Số tiền dự kiến của lệnh", style = MaterialTheme.typography.labelMedium)
                            Text(formatMoney(order.expectedAmount), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Black)
                        }
                    }
                }
                Text("Nhập số tờ theo từng mệnh giá", style = MaterialTheme.typography.labelLarge, modifier = Modifier.padding(vertical = 8.dp))
                LazyColumn(Modifier.weight(1f, fill = false), verticalArrangement = Arrangement.spacedBy(7.dp)) {
                    items(CashDenominations, key = { it }) { denomination ->
                        val quantity = normalized[denomination] ?: 0
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            Column(Modifier.weight(1f)) {
                                Text(formatMoney(denomination.toDouble()), fontWeight = FontWeight.Bold)
                                Text(if (quantity == 0) "—" else formatMoney((denomination * quantity).toDouble()), style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            OutlinedTextField(
                                value = quantities[denomination].orEmpty(),
                                onValueChange = { raw -> quantities = quantities + (denomination to raw.filter(Char::isDigit).take(6)) },
                                singleLine = true,
                                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                                modifier = Modifier.width(92.dp),
                                label = { Text("Số tờ") },
                            )
                        }
                    }
                }
                Surface(shape = RoundedCornerShape(14.dp), color = MaterialTheme.colorScheme.primaryContainer, modifier = Modifier.fillMaxWidth().padding(top = 10.dp)) {
                    Row(Modifier.padding(13.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                        Text("Tổng đang đếm", fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                        Text(formatMoney(total.toDouble()), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Black)
                    }
                }
                if (cashier) {
                    Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                        Text(if (difference == 0L) "Đã khớp tiền tài xế" else "Chênh lệch", color = if (difference == 0L) Color(0xFF059669) else MaterialTheme.colorScheme.error, fontWeight = FontWeight.Bold)
                        Text((if (difference > 0) "+" else "") + formatMoney(difference.toDouble()), color = if (difference == 0L) Color(0xFF059669) else MaterialTheme.colorScheme.error, fontWeight = FontWeight.Black)
                    }
                }
                if (!cashier && total > 0 && expectedDifference != 0L) {
                    Text("Thực thu lệch ${(if (expectedDifference > 0) "+" else "") + formatMoney(expectedDifference.toDouble())} so với dự kiến.", color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 8.dp))
                    OutlinedTextField(
                        value = reason,
                        onValueChange = { reason = it.take(1000) },
                        label = { Text("Lý do chênh lệch *") },
                        minLines = 2,
                        modifier = Modifier.fillMaxWidth().padding(top = 6.dp),
                    )
                }
                Button(
                    onClick = { onConfirm(normalized, reason.trim()) },
                    modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
                    enabled = total > 0 && !busy && (cashier || expectedDifference == 0L || reason.isNotBlank()),
                ) {
                    if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                    else Icon(if (cashier && difference == 0L) Icons.Filled.CheckCircle else Icons.Filled.Payments, null, Modifier.size(18.dp))
                    Spacer(Modifier.width(7.dp))
                    Text(if (!cashier) "Xác nhận đã nhận từ khách" else if (difference == 0L && total == order.expectedAmount.toLong()) "Nhận đủ — đã nộp đủ tiền" else if (difference == 0L) "Ghi nhận và chờ duyệt" else "Ghi nhận sai lệch")
                }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun CreateCashCollectionDialog(
    customers: List<CashCollectionCustomer>,
    drivers: List<CashCollectionDriver>,
    busy: Boolean,
    onCreate: (CreateCashCollectionBody) -> Unit,
    onClose: () -> Unit,
) {
    var customer by remember(customers) { mutableStateOf(customers.firstOrNull()) }
    var driver by remember(drivers) { mutableStateOf(drivers.firstOrNull()) }
    var amount by remember { mutableStateOf("") }
    var scheduled by remember { mutableStateOf(LocalDate.now().toString()) }
    var dueDate by remember { mutableStateOf(LocalDate.now().plusDays(1).toString()) }
    var note by remember { mutableStateOf("") }
    var error by remember { mutableStateOf("") }
    val customerOptions = remember(customers) {
        customers.map { PickOption(id = it.id, label = it.name, sub = it.phone, keywords = it.phone) }
    }
    val driverOptions = remember(drivers) {
        drivers.map { PickOption(id = it.id, label = it.name, sub = it.position.ifBlank { it.employeeCode }, keywords = it.employeeCode) }
    }

    Dialog(onDismissRequest = onClose) {
        Surface(shape = RoundedCornerShape(24.dp), tonalElevation = 8.dp, modifier = Modifier.fillMaxWidth().heightIn(max = 720.dp)) {
            Column(Modifier.padding(18.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("Tạo lệnh thu tiền", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Black, modifier = Modifier.weight(1f))
                    IconButton(onClick = onClose, enabled = !busy) { Icon(Icons.Filled.Close, "Đóng") }
                }
                LazyColumn(Modifier.weight(1f, fill = false), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    item {
                        // Danh sách khách hàng dài → ô chọn có tìm kiếm (gõ tên/số điện thoại, không dấu vẫn ra).
                        SelectField(
                            label = "Khách hàng *",
                            selectedId = customer?.id,
                            options = customerOptions,
                            onPick = { picked -> customer = customers.firstOrNull { it.id == picked.id } },
                            searchHint = "Tìm theo tên hoặc số điện thoại",
                            emptyText = "Chưa có khách hàng nào để chọn.",
                            showAvatar = true,
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                    item {
                        SelectField(
                            label = "Tài xế *",
                            selectedId = driver?.id,
                            options = driverOptions,
                            onPick = { picked -> driver = drivers.firstOrNull { it.id == picked.id } },
                            searchHint = "Tìm theo tên hoặc mã nhân viên",
                            emptyText = "Chưa có tài xế nào để chọn.",
                            showAvatar = true,
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                    item {
                        MoneyField(
                            label = "Số tiền dự kiến *",
                            value = amount,
                            onChange = { amount = it },
                            supportingText = formatMoney(amount.toDoubleOrNull() ?: 0.0),
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                    item { DateField(label = "Ngày đi thu *", value = scheduled, onChange = { scheduled = it }, modifier = Modifier.fillMaxWidth()) }
                    item { DateField(label = "Hạn bàn giao *", value = dueDate, onChange = { dueDate = it }, supportingText = "Mặc định 10:00 ngày đã chọn", modifier = Modifier.fillMaxWidth()) }
                    item { OutlinedTextField(note, { note = it.take(1000) }, label = { Text("Nội dung / ghi chú") }, minLines = 2, modifier = Modifier.fillMaxWidth()) }
                    item { Text("Lệnh không lưu GPS và không lưu địa chỉ khách hàng.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
                }
                if (error.isNotBlank()) Text(error, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 8.dp))
                Button(
                    onClick = {
                        val money = amount.toDoubleOrNull() ?: 0.0
                        val scheduledDate = runCatching { LocalDate.parse(scheduled) }.getOrNull()
                        val due = runCatching { LocalDate.parse(dueDate).atTime(10, 0).atZone(ZoneId.systemDefault()).toInstant().toString() }.getOrNull()
                        when {
                            customer == null || driver == null -> error = "Chưa có khách hàng hoặc tài xế hợp lệ."
                            money <= 0 || scheduledDate == null || due == null -> error = "Số tiền hoặc ngày không hợp lệ."
                            else -> onCreate(CreateCashCollectionBody(customer!!.id, driver!!.id, money, scheduledDate.toString(), due, note.trim()))
                        }
                    },
                    enabled = !busy && customers.isNotEmpty() && drivers.isNotEmpty(),
                    modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
                ) {
                    if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp) else Icon(Icons.Filled.Add, null, Modifier.size(18.dp))
                    Spacer(Modifier.width(7.dp)); Text("Tạo và giao tài xế")
                }
            }
        }
    }
}

@Composable
private fun CashResolveDialog(
    order: CashCollection,
    busy: Boolean,
    onConfirm: (String, String) -> Unit,
    onClose: () -> Unit,
) {
    var action by remember(order.id) { mutableStateOf(if (order.cashVariance) "return_to_driver" else "approve_actual") }
    var reason by remember(order.id) { mutableStateOf("") }
    val cashierTotal = order.receivedAmount ?: 0.0

    Dialog(onDismissRequest = onClose) {
        Surface(shape = RoundedCornerShape(24.dp), tonalElevation = 8.dp, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text("Kế toán trưởng xử lý sai lệch", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Black)
                        Text("${order.orderNo} · ${order.customerName}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    IconButton(onClick = onClose, enabled = !busy) { Icon(Icons.Filled.Close, "Đóng") }
                }
                CashInfoRow("Số tiền dự kiến", formatMoney(order.expectedAmount), strong = true)
                CashInfoRow("Tài xế khai", formatMoney(order.collectedAmount ?: 0.0), strong = true)
                CashInfoRow("Thủ quỹ đếm", formatMoney(cashierTotal), strong = true, color = MaterialTheme.colorScheme.error)
                Text("Phương án xử lý", style = MaterialTheme.typography.labelLarge)
                FilterChip(
                    selected = action == "approve_actual",
                    onClick = { action = "approve_actual" },
                    label = { Text("Duyệt số thủ quỹ thực đếm") },
                )
                FilterChip(
                    selected = action == "return_to_driver",
                    onClick = { action = "return_to_driver" },
                    label = { Text("Trả tài xế kiểm đếm và khai lại") },
                )
                OutlinedTextField(
                    value = reason,
                    onValueChange = { reason = it.take(1000) },
                    label = { Text("Lý do xử lý *") },
                    minLines = 2,
                    modifier = Modifier.fillMaxWidth(),
                )
                Text(
                    if (action == "approve_actual") "Sau khi xác nhận, ${formatMoney(cashierTotal)} được ghi nhận là đã nộp đủ tiền."
                    else "Lệnh sẽ quay lại cho tài xế khai và bàn giao lại; chưa ghi nhận đã nộp đủ tiền.",
                    style = MaterialTheme.typography.bodySmall,
                    color = if (action == "approve_actual") MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary,
                    fontWeight = FontWeight.Bold,
                )
                Button(
                    onClick = { onConfirm(action, reason.trim()) },
                    enabled = reason.isNotBlank() && !busy,
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                    else Icon(Icons.Filled.CheckCircle, null, Modifier.size(18.dp))
                    Spacer(Modifier.width(7.dp)); Text("Xác nhận xử lý")
                }
            }
        }
    }
}

@Composable
private fun CashReasonDialog(title: String, label: String, confirmLabel: String, onConfirm: (String) -> Unit, onClose: () -> Unit) {
    var reason by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onClose,
        icon = { Icon(Icons.Filled.ErrorOutline, null) },
        title = { Text(title) },
        text = { OutlinedTextField(reason, { reason = it.take(1000) }, label = { Text(label) }, minLines = 2, modifier = Modifier.fillMaxWidth()) },
        confirmButton = { TextButton(onClick = { onConfirm(reason.trim()) }, enabled = reason.isNotBlank()) { Text(confirmLabel) } },
        dismissButton = { TextButton(onClick = onClose) { Text("Đóng") } },
    )
}

@Composable
private fun CashNotice(text: String, success: Boolean, onDismiss: () -> Unit) {
    Surface(shape = RoundedCornerShape(16.dp), color = if (success) Color(0xFF059669).copy(alpha = .12f) else MaterialTheme.colorScheme.errorContainer) {
        Row(Modifier.padding(start = 14.dp, end = 4.dp, top = 8.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(if (success) Icons.Filled.CheckCircle else Icons.Filled.ErrorOutline, null, tint = if (success) Color(0xFF059669) else MaterialTheme.colorScheme.error)
            Spacer(Modifier.width(8.dp)); Text(text, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.Medium)
            IconButton(onClick = onDismiss) { Icon(Icons.Filled.Close, "Đóng") }
        }
    }
}
