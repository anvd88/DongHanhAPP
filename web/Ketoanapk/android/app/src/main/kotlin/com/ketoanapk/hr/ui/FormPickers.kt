package com.ketoanapk.hr.ui

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.gestures.snapping.rememberSnapFlingBehavior
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.OffsetMapping
import androidx.compose.ui.text.input.TransformedText
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import java.text.Normalizer
import java.time.LocalDate
import java.time.LocalTime
import java.time.YearMonth
import kotlin.math.abs

/* ==========================================================================================
 *  BỘ Ô NHẬP DÙNG CHUNG CHO MỌI FORM TRONG APP
 *
 *  Gom về một chỗ ba thứ người dùng chạm nhiều nhất, để mọi màn hình nhìn và dùng GIỐNG HỆT
 *  nhau (trước đây mỗi màn tự chế một kiểu: chỗ thì dropdown phải cuộn tay, chỗ thì bắt gõ
 *  "yyyy-MM-dd" bằng bàn phím):
 *
 *   1. [SelectField] — ô chọn có TÌM KIẾM, kiểu Data Validation List của Excel. Gõ vài chữ là
 *      lọc ngay; bỏ dấu vẫn ra ("nguyen" ⇒ "Nguyễn"). Dùng cho khách hàng, nhân viên, tài xế…
 *   2. [DateField] / [TimeField] — chọn ngày & giờ bằng BÁNH XE vuốt lên/xuống kiểu iOS. Giờ
 *      hiển thị 12 tiếng kèm Sáng/Chiều cho dễ đọc, nhưng giá trị vẫn là "HH:mm" 24h nên phần
 *      backend không phải đổi gì.
 *   3. [MoneyField] — ô nhập tiền tự chèn dấu chấm hàng nghìn y như bản web (1.500.000), con
 *      trỏ vẫn nhảy đúng chỗ vì dùng VisualTransformation chứ không sửa chuỗi trong state.
 * ========================================================================================== */

// ─────────────────────────── 1. Ô chọn có tìm kiếm (kiểu Excel) ───────────────────────────

/** Một dòng trong danh sách chọn. [sub] là dòng phụ mờ bên dưới, [extra] hiện ở mép phải. */
data class PickOption(
    val id: String,
    val label: String,
    val sub: String = "",
    val extra: String = "",
    /** Từ khóa phụ để tìm kiếm (mã nhân viên, số điện thoại…) mà không cần hiện lên màn hình. */
    val keywords: String = "",
    /**
     * Dòng CÓ HIỆN nhưng KHÔNG bấm được (vd. nhân viên chưa chấm công, đang nghỉ phép).
     * Cố ý không lọc bỏ khỏi danh sách: thấy tên kèm lý do thì người dùng hiểu ngay vì sao không
     * giao được, còn tên biến mất thì họ đi hỏi bộ phận kỹ thuật xem tài khoản có bị xoá không.
     * Lý do hiển thị đặt ở [sub] hoặc [extra].
     */
    val disabled: Boolean = false,
)

private val diacriticMarks = "\\p{Mn}+".toRegex()

/**
 * Chuẩn hóa chuỗi để tìm kiếm không dấu, không phân biệt hoa thường: "Nguyễn Đức" → "nguyen duc".
 * Nhờ vậy người dùng gõ nhanh bằng bàn phím không dấu vẫn tìm ra đúng người.
 */
fun searchKey(text: String): String =
    Normalizer.normalize(text.lowercase(), Normalizer.Form.NFD)
        .replace(diacriticMarks, "")
        .replace('đ', 'd')

/** Lọc danh sách theo từ khóa (không dấu). Mỗi tiếng trong từ khóa phải khớp ở đâu đó. */
fun filterOptions(options: List<PickOption>, query: String): List<PickOption> {
    val words = searchKey(query.trim()).split(' ').filter { it.isNotBlank() }
    if (words.isEmpty()) return options
    return options.filter { option ->
        val hay = searchKey("${option.label} ${option.sub} ${option.extra} ${option.keywords}")
        words.all { hay.contains(it) }
    }
}

/**
 * Ô chọn dạng ô nhập Material (nhãn nổi) nhưng bấm vào thì mở bảng chọn CÓ Ô TÌM KIẾM.
 * Thay cho ExposedDropdownMenu: danh sách vài trăm khách hàng/nhân viên không còn phải cuộn tay.
 */
@Composable
fun SelectField(
    label: String,
    selectedId: String?,
    options: List<PickOption>,
    onPick: (PickOption) -> Unit,
    modifier: Modifier = Modifier,
    placeholder: String = "Chọn…",
    enabled: Boolean = true,
    isError: Boolean = false,
    supportingText: String = "",
    searchHint: String = "Gõ để tìm nhanh…",
    emptyText: String = "Không có mục nào để chọn.",
    showAvatar: Boolean = false,
) {
    var open by remember { mutableStateOf(false) }
    val selected = remember(options, selectedId) { options.firstOrNull { it.id == selectedId } }
    val display = selected?.let { if (it.sub.isBlank()) it.label else "${it.label} · ${it.sub}" }.orEmpty()

    Box(modifier) {
        OutlinedTextField(
            value = display,
            onValueChange = {},
            readOnly = true,
            enabled = enabled,
            isError = isError,
            singleLine = true,
            label = { Text(label) },
            placeholder = { Text(placeholder) },
            supportingText = if (supportingText.isBlank()) null else ({ Text(supportingText) }),
            trailingIcon = { Icon(Icons.Filled.KeyboardArrowDown, contentDescription = null) },
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier.fillMaxWidth(),
        )
        // Ô readOnly của Material không tự nhận click nên phủ một lớp trong suốt lên trên để bắt.
        Box(
            Modifier
                .matchParentSize()
                .clip(RoundedCornerShape(14.dp))
                .clickable(enabled = enabled) { open = true },
        )
    }

    if (open) {
        SearchablePickerDialog(
            title = label,
            options = options,
            selectedId = selectedId,
            searchHint = searchHint,
            emptyText = emptyText,
            showAvatar = showAvatar,
            onPick = { onPick(it); open = false },
            onDismiss = { open = false },
        )
    }
}

/**
 * Nền chung của các hộp thoại ở đây: cửa sổ trải hết màn (để tự quyết bề ngang), chừa chỗ cho bàn
 * phím, và bấm ra vùng tối bên ngoài thì đóng — thứ mà cửa sổ full-screen không tự làm được.
 */
@Composable
private fun PickerDialogShell(
    onDismiss: () -> Unit,
    widthFraction: Float,
    maxHeight: Dp,
    content: @Composable ColumnScope.() -> Unit,
) {
    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false),
    ) {
        Box(
            Modifier
                .fillMaxSize()
                .clickable(
                    interactionSource = remember { MutableInteractionSource() },
                    indication = null,
                    onClick = onDismiss,
                )
                .imePadding()
                .padding(vertical = 24.dp),
            contentAlignment = Alignment.Center,
        ) {
            Surface(
                shape = RoundedCornerShape(26.dp),
                color = MaterialTheme.colorScheme.surface,
                tonalElevation = 6.dp,
                modifier = Modifier
                    .fillMaxWidth(widthFraction)
                    .heightIn(max = maxHeight)
                    // Chặn cú chạm rơi xuống nền, nếu không bấm vào chỗ trống trong hộp sẽ đóng mất.
                    .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) {},
            ) {
                Column(content = content)
            }
        }
    }
}

/** Bảng chọn toàn màn: ô tìm kiếm ở trên, danh sách đã lọc ở dưới, mục đang chọn có dấu tích. */
@Composable
fun SearchablePickerDialog(
    title: String,
    options: List<PickOption>,
    selectedId: String?,
    onPick: (PickOption) -> Unit,
    onDismiss: () -> Unit,
    searchHint: String = "Gõ để tìm nhanh…",
    emptyText: String = "Không có mục nào để chọn.",
    showAvatar: Boolean = false,
    /** Danh sách ngắn thì ẩn ô tìm kiếm cho gọn; đặt true để luôn hiện. */
    alwaysSearch: Boolean = false,
) {
    var query by remember { mutableStateOf("") }
    val filtered = remember(options, query) { filterOptions(options, query) }
    val searchable = alwaysSearch || options.size >= 8
    val focus = remember { FocusRequester() }

    LaunchedEffect(searchable) { if (searchable) runCatching { focus.requestFocus() } }

    PickerDialogShell(onDismiss = onDismiss, widthFraction = 0.94f, maxHeight = 620.dp) {
        Column(Modifier.padding(top = 12.dp, bottom = 10.dp)) {
                Row(
                    Modifier.fillMaxWidth().padding(start = 18.dp, end = 6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        title,
                        style = MaterialTheme.typography.titleLarge,
                        fontWeight = FontWeight.Black,
                        color = MaterialTheme.colorScheme.onSurface,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f),
                    )
                    IconButton(onClick = onDismiss) { Icon(Icons.Filled.Close, contentDescription = "Đóng") }
                }

                if (searchable) {
                    OutlinedTextField(
                        value = query,
                        onValueChange = { query = it },
                        singleLine = true,
                        placeholder = { Text(searchHint) },
                        leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
                        trailingIcon = {
                            if (query.isNotBlank()) {
                                IconButton(onClick = { query = "" }) { Icon(Icons.Filled.Close, contentDescription = "Xóa tìm kiếm") }
                            }
                        },
                        shape = RoundedCornerShape(14.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = 16.dp)
                            .focusRequester(focus),
                    )
                    Text(
                        if (query.isBlank()) "${options.size} mục" else "${filtered.size}/${options.size} mục khớp",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(start = 20.dp, top = 8.dp, bottom = 2.dp),
                    )
                }

                when {
                    options.isEmpty() -> Text(
                        emptyText,
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(20.dp),
                    )

                    filtered.isEmpty() -> Text(
                        "Không tìm thấy mục nào khớp “${query.trim()}”.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(20.dp),
                    )

                    else -> LazyColumn(Modifier.weight(1f, fill = false)) {
                        itemsIndexed(filtered, key = { _, row -> row.id }) { index, row ->
                            if (index > 0) HorizontalDivider(color = MaterialTheme.colorScheme.outline)
                            PickerRow(row, row.id == selectedId, showAvatar) { onPick(row) }
                        }
                    }
                }
        }
    }
}

@Composable
private fun PickerRow(option: PickOption, selected: Boolean, showAvatar: Boolean, onClick: () -> Unit) {
    // Dòng bị khoá: mờ đi và không nhận chạm, nhưng chữ vẫn đọc rõ để thấy được lý do bên dưới tên.
    val locked = option.disabled
    val dim = if (locked) 0.45f else 1f
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(enabled = !locked, onClick = onClick)
            .background(if (selected) MaterialTheme.colorScheme.primary.copy(alpha = 0.08f) else androidx.compose.ui.graphics.Color.Transparent)
            .padding(horizontal = 18.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        if (showAvatar) {
            Box(
                Modifier
                    .size(38.dp)
                    .clip(CircleShape)
                    .background(MaterialTheme.colorScheme.primaryContainer.copy(alpha = dim)),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    initials(option.label),
                    style = MaterialTheme.typography.labelMedium,
                    fontWeight = FontWeight.ExtraBold,
                    color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = dim),
                )
            }
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                option.label,
                style = MaterialTheme.typography.bodyLarge,
                fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
                color = MaterialTheme.colorScheme.onSurface.copy(alpha = dim),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (option.sub.isNotBlank()) {
                Text(
                    option.sub,
                    style = MaterialTheme.typography.bodySmall,
                    // Lý do bị khoá phải ĐỌC ĐƯỢC, nên dòng phụ của hàng khoá không mờ thêm nữa.
                    color = if (locked) MaterialTheme.colorScheme.error
                            else MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        if (option.extra.isNotBlank()) {
            Text(
                option.extra,
                style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = dim),
                maxLines = 1,
            )
        }
        if (selected && !locked) {
            Icon(Icons.Filled.Check, contentDescription = "Đang chọn", tint = MaterialTheme.colorScheme.primary)
        }
    }
}

// ───────────────────── 2. Bánh xe chọn ngày / giờ (vuốt lên xuống kiểu iOS) ─────────────────────

/**
 * Một cột bánh xe: danh sách cuộn dọc, mục nằm giữa khung là mục đang chọn. Hai đầu chèn ô trống
 * để mục đầu/cuối vẫn lên được chính giữa; nhờ vậy `firstVisibleItemIndex` chính là mục đang chọn.
 */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun WheelColumn(
    items: List<String>,
    selectedIndex: Int,
    onSelect: (Int) -> Unit,
    modifier: Modifier = Modifier,
    itemHeight: Dp = 42.dp,
    visibleCount: Int = 5,
) {
    if (items.isEmpty()) return
    val edge = (visibleCount - 1) / 2
    val safeIndex = selectedIndex.coerceIn(0, items.lastIndex)
    val state = rememberLazyListState(initialFirstVisibleItemIndex = safeIndex)
    val fling = rememberSnapFlingBehavior(state)
    val halfItemPx = with(LocalDensity.current) { itemHeight.roundToPx() } / 2
    val haptic = LocalHapticFeedback.current

    val centered by remember(items.size) {
        derivedStateOf {
            val index = state.firstVisibleItemIndex + if (state.firstVisibleItemScrollOffset > halfItemPx) 1 else 0
            index.coerceIn(0, items.lastIndex)
        }
    }

    // Người dùng vuốt → báo giá trị mới ra ngoài ngay khi mục khác trôi vào giữa khung.
    LaunchedEffect(state, items.size) {
        snapshotFlow { centered }.distinctUntilChanged().drop(1).collect { index ->
            haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
            onSelect(index)
        }
    }
    // Thả tay giữa chừng (không đủ lực để fling) thì tự kéo cho mục vào đúng giữa.
    LaunchedEffect(state) {
        snapshotFlow { state.isScrollInProgress }.distinctUntilChanged().collect { scrolling ->
            if (!scrolling && state.firstVisibleItemScrollOffset != 0) state.animateScrollToItem(centered)
        }
    }
    // Giá trị bị đổi từ bên ngoài (vd. đổi tháng làm ngày 31 co lại còn 30) → kéo bánh xe theo.
    LaunchedEffect(safeIndex, items.size) {
        if (!state.isScrollInProgress && centered != safeIndex) state.scrollToItem(safeIndex)
    }

    LazyColumn(
        state = state,
        flingBehavior = fling,
        modifier = modifier.height(itemHeight * visibleCount),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        items(edge) { Spacer(Modifier.height(itemHeight)) }
        itemsIndexed(items) { index, text ->
            val distance = abs(index - centered)
            val alpha = when (distance) {
                0 -> 1f
                1 -> 0.55f
                2 -> 0.3f
                else -> 0.18f
            }
            Box(
                Modifier
                    .fillMaxWidth()
                    .height(itemHeight),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text,
                    fontSize = if (distance == 0) 21.sp else 17.sp,
                    fontWeight = if (distance == 0) FontWeight.Bold else FontWeight.Normal,
                    color = if (distance == 0) MaterialTheme.colorScheme.onSurface.copy(alpha = alpha)
                    else MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = alpha),
                    textAlign = TextAlign.Center,
                    maxLines = 1,
                )
            }
        }
        items(edge) { Spacer(Modifier.height(itemHeight)) }
    }
}

/** Khung bánh xe: vẽ dải sáng ở giữa (chỗ "đang chọn") rồi đặt các cột lên trên. */
@Composable
private fun WheelFrame(
    itemHeight: Dp = 42.dp,
    visibleCount: Int = 5,
    columns: @Composable RowScope.() -> Unit,
) {
    Box(
        Modifier
            .fillMaxWidth()
            .height(itemHeight * visibleCount),
        contentAlignment = Alignment.Center,
    ) {
        Box(
            Modifier
                .fillMaxWidth()
                .height(itemHeight)
                .clip(RoundedCornerShape(12.dp))
                .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.10f)),
        )
        Row(Modifier.fillMaxSize(), verticalAlignment = Alignment.CenterVertically) { columns() }
    }
}

/**
 * Hộp thoại chọn ngày kiểu iOS: ba bánh xe Ngày · Tháng · Năm vuốt lên xuống.
 * Trả về [LocalDate] đã chọn — nơi gọi tự đổi sang chuỗi cần thiết.
 */
@Composable
fun WheelDatePickerDialog(
    initial: LocalDate,
    onPicked: (LocalDate) -> Unit,
    onDismiss: () -> Unit,
    title: String = "Chọn ngày",
    minYear: Int = LocalDate.now().year - 80,
    maxYear: Int = LocalDate.now().year + 10,
) {
    val years = remember(minYear, maxYear) { (minYear..maxYear).toList() }
    var year by remember { mutableIntStateOf(initial.year.coerceIn(minYear, maxYear)) }
    var month by remember { mutableIntStateOf(initial.monthValue) }
    var day by remember { mutableIntStateOf(initial.dayOfMonth) }

    val daysInMonth = remember(year, month) { YearMonth.of(year, month).lengthOfMonth() }
    // Đổi từ 31/1 sang tháng 2 thì ngày phải tụt về 28/29 chứ không được treo giá trị không có thật.
    LaunchedEffect(daysInMonth) { if (day > daysInMonth) day = daysInMonth }
    val picked = remember(year, month, day) { LocalDate.of(year, month, day.coerceAtMost(daysInMonth)) }

    WheelDialogShell(
        title = title,
        preview = "${weekdayVi(picked)}, ${"%02d/%02d/%04d".format(picked.dayOfMonth, picked.monthValue, picked.year)}",
        extraAction = "Hôm nay" to {
            val today = LocalDate.now()
            year = today.year.coerceIn(minYear, maxYear); month = today.monthValue; day = today.dayOfMonth
        },
        onConfirm = { onPicked(picked) },
        onDismiss = onDismiss,
    ) {
        WheelFrame {
            WheelColumn(
                items = (1..daysInMonth).map { it.toString() },
                selectedIndex = day - 1,
                onSelect = { day = it + 1 },
                modifier = Modifier.weight(1f),
            )
            WheelColumn(
                items = (1..12).map { "Tháng $it" },
                selectedIndex = month - 1,
                onSelect = { month = it + 1 },
                modifier = Modifier.weight(1.4f),
            )
            WheelColumn(
                items = years.map { it.toString() },
                selectedIndex = years.indexOf(year).coerceAtLeast(0),
                onSelect = { year = years[it] },
                modifier = Modifier.weight(1.2f),
            )
        }
    }
}

/**
 * Hộp thoại chọn giờ kiểu iOS: Giờ (1–12) · Phút · Sáng/Chiều. Trả về giờ 24h để dữ liệu
 * gửi lên máy chủ giữ nguyên định dạng "HH:mm" như trước.
 */
@Composable
fun WheelTimePickerDialog(
    initialHour: Int,
    initialMinute: Int,
    onPicked: (hour: Int, minute: Int) -> Unit,
    onDismiss: () -> Unit,
    title: String = "Chọn giờ",
) {
    var hour12 by remember { mutableIntStateOf(to12Hour(initialHour)) }
    var minute by remember { mutableIntStateOf(initialMinute.coerceIn(0, 59)) }
    var afternoon by remember { mutableStateOf(initialHour >= 12) }
    val hour24 = to24Hour(hour12, afternoon)

    WheelDialogShell(
        title = title,
        preview = "$hour12:${"%02d".format(minute)} ${if (afternoon) "CH" else "SA"}",
        extraAction = "Bây giờ" to {
            val now = LocalTime.now()
            hour12 = to12Hour(now.hour); minute = now.minute; afternoon = now.hour >= 12
        },
        onConfirm = { onPicked(hour24, minute) },
        onDismiss = onDismiss,
    ) {
        WheelFrame {
            WheelColumn(
                items = (1..12).map { it.toString() },
                selectedIndex = hour12 - 1,
                onSelect = { hour12 = it + 1 },
                modifier = Modifier.weight(1f),
            )
            WheelColumn(
                items = (0..59).map { "%02d".format(it) },
                selectedIndex = minute,
                onSelect = { minute = it },
                modifier = Modifier.weight(1f),
            )
            WheelColumn(
                items = listOf("Sáng", "Chiều"),
                selectedIndex = if (afternoon) 1 else 0,
                onSelect = { afternoon = it == 1 },
                modifier = Modifier.weight(1.1f),
            )
        }
    }
}

/** Khung chung của hai hộp thoại bánh xe: tiêu đề · dòng xem trước · bánh xe · nút. */
@Composable
private fun WheelDialogShell(
    title: String,
    preview: String,
    extraAction: Pair<String, () -> Unit>?,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
    content: @Composable () -> Unit,
) {
    PickerDialogShell(onDismiss = onDismiss, widthFraction = 0.92f, maxHeight = 640.dp) {
            Column(
                Modifier.padding(horizontal = 16.dp, vertical = 16.dp),
                verticalArrangement = Arrangement.spacedBy(6.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Text(title, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Text(
                    preview,
                    style = MaterialTheme.typography.headlineSmall,
                    fontWeight = FontWeight.Black,
                    color = MaterialTheme.colorScheme.primary,
                )
                Spacer(Modifier.height(2.dp))
                content()
                Text(
                    "Vuốt lên / xuống để chỉnh",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    TextButton(onClick = onDismiss) { Text("Hủy") }
                    if (extraAction != null) {
                        TextButton(onClick = extraAction.second) { Text(extraAction.first) }
                    }
                    Spacer(Modifier.weight(1f))
                    TextButton(onClick = { onConfirm(); onDismiss() }) {
                        Text("Chọn", fontWeight = FontWeight.Bold)
                    }
                }
            }
    }
}

// ───────────────────────────── 3. Ô ngày / giờ dùng trong form ─────────────────────────────

/**
 * Ô chọn ngày dạng ô nhập Material. [value] là chuỗi ISO "yyyy-MM-dd" (rỗng = chưa chọn),
 * hiển thị cho người dùng theo "dd/MM/yyyy".
 */
@Composable
fun DateField(
    label: String,
    value: String,
    onChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    isError: Boolean = false,
    supportingText: String = "",
    placeholder: String = "Chọn ngày",
) {
    var open by remember { mutableStateOf(false) }
    val parsed = remember(value) { runCatching { LocalDate.parse(value.take(10)) }.getOrNull() }

    ClickableFieldBox(
        label = label,
        display = parsed?.let { "%02d/%02d/%04d".format(it.dayOfMonth, it.monthValue, it.year) }.orEmpty(),
        placeholder = placeholder,
        icon = Icons.Filled.CalendarMonth,
        enabled = enabled,
        isError = isError,
        supportingText = supportingText,
        modifier = modifier,
        onClick = { open = true },
    )

    if (open) {
        WheelDatePickerDialog(
            initial = parsed ?: LocalDate.now(),
            title = label,
            onPicked = { onChange(it.toString()) },
            onDismiss = { open = false },
        )
    }
}

/** Ô chọn giờ dạng ô nhập Material. [value] là "HH:mm" 24h; hiển thị "8:30 SA" cho dễ đọc. */
@Composable
fun TimeField(
    label: String,
    value: String,
    onChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    isError: Boolean = false,
    supportingText: String = "",
    placeholder: String = "Chọn giờ",
) {
    var open by remember { mutableStateOf(false) }
    val parts = remember(value) { value.split(":") }
    val hour = parts.getOrNull(0)?.toIntOrNull()?.coerceIn(0, 23)
    val minute = parts.getOrNull(1)?.toIntOrNull()?.coerceIn(0, 59) ?: 0

    ClickableFieldBox(
        label = label,
        display = if (hour == null) "" else formatTime12(hour, minute),
        placeholder = placeholder,
        icon = Icons.Filled.Schedule,
        enabled = enabled,
        isError = isError,
        supportingText = supportingText,
        modifier = modifier,
        onClick = { open = true },
    )

    if (open) {
        WheelTimePickerDialog(
            initialHour = hour ?: 8,
            initialMinute = minute,
            title = label,
            onPicked = { h, m -> onChange("%02d:%02d".format(h, m)) },
            onDismiss = { open = false },
        )
    }
}

/** Ô nhập Material chỉ để BẤM (không gõ được) — nền của [DateField], [TimeField], [SelectField]. */
@Composable
private fun ClickableFieldBox(
    label: String,
    display: String,
    placeholder: String,
    icon: ImageVector,
    enabled: Boolean,
    isError: Boolean,
    supportingText: String,
    modifier: Modifier,
    onClick: () -> Unit,
) {
    Box(modifier) {
        OutlinedTextField(
            value = display,
            onValueChange = {},
            readOnly = true,
            enabled = enabled,
            isError = isError,
            singleLine = true,
            label = { Text(label) },
            placeholder = { Text(placeholder) },
            supportingText = if (supportingText.isBlank()) null else ({ Text(supportingText) }),
            trailingIcon = { Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary) },
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier.fillMaxWidth(),
        )
        Box(
            Modifier
                .matchParentSize()
                .clip(RoundedCornerShape(14.dp))
                .clickable(enabled = enabled, onClick = onClick),
        )
    }
}

// ─────────────────────────────── 4. Ô nhập tiền có dấu chấm ───────────────────────────────

/** "1500000" → "1.500.000" (kiểu Việt Nam: dấu chấm sau mỗi 3 chữ số), giống hệt bản web. */
fun groupThousands(digits: String): String {
    val clean = digits.filter { it.isDigit() }.trimStart('0')
    if (clean.isEmpty()) return ""
    return clean.reversed().chunked(3).joinToString(".").reversed()
}

/**
 * Chèn dấu chấm hàng nghìn NGAY TRÊN MÀN HÌNH trong khi state vẫn chỉ là chữ số. Dùng
 * VisualTransformation (chứ không sửa chuỗi trong state) nên con trỏ không bị nhảy lung tung
 * khi người dùng sửa giữa dãy số.
 */
class ThousandsSeparatorTransformation : VisualTransformation {
    override fun filter(text: AnnotatedString): TransformedText {
        val digits = text.text.filter { it.isDigit() }
        val out = StringBuilder()
        val originalToTransformed = IntArray(digits.length + 1)
        for (i in digits.indices) {
            if (i > 0 && (digits.length - i) % 3 == 0) out.append('.')
            originalToTransformed[i] = out.length
            out.append(digits[i])
        }
        originalToTransformed[digits.length] = out.length

        val transformedToOriginal = IntArray(out.length + 1)
        var counted = 0
        for (j in out.indices) {
            if (out[j] != '.') counted++
            transformedToOriginal[j + 1] = counted
        }

        val mapping = object : OffsetMapping {
            override fun originalToTransformed(offset: Int): Int =
                originalToTransformed[offset.coerceIn(0, digits.length)]

            override fun transformedToOriginal(offset: Int): Int =
                transformedToOriginal[offset.coerceIn(0, out.length)]
        }
        return TransformedText(AnnotatedString(out.toString()), mapping)
    }
}

/**
 * Ô nhập số tiền: bàn phím số, tự chèn dấu chấm hàng nghìn, đuôi "₫". [value] và giá trị trả về
 * chỉ gồm CHỮ SỐ nên nơi gọi vẫn `toDoubleOrNull()` như bình thường.
 */
@Composable
fun MoneyField(
    label: String,
    value: String,
    onChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    readOnly: Boolean = false,
    isError: Boolean = false,
    supportingText: String = "",
    maxDigits: Int = 15,
) {
    OutlinedTextField(
        value = value.filter { it.isDigit() },
        onValueChange = { raw -> onChange(raw.filter { it.isDigit() }.take(maxDigits)) },
        modifier = modifier,
        enabled = enabled,
        readOnly = readOnly,
        isError = isError,
        singleLine = true,
        label = { Text(label) },
        placeholder = { Text("0") },
        supportingText = if (supportingText.isBlank()) null else ({ Text(supportingText) }),
        trailingIcon = { Text("₫", fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurfaceVariant) },
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
        visualTransformation = ThousandsSeparatorTransformation(),
        shape = RoundedCornerShape(14.dp),
    )
}

// ─────────────────────────────────── Tiện ích định dạng ───────────────────────────────────

/** Giờ 24h → số giờ trên mặt đồng hồ 12 tiếng (0h và 12h đều hiện "12"). */
fun to12Hour(hour24: Int): Int = ((hour24 % 12).takeIf { it != 0 } ?: 12)

/** Số giờ 12 tiếng + buổi → giờ 24h để gửi lên máy chủ. */
fun to24Hour(hour12: Int, afternoon: Boolean): Int = when {
    afternoon && hour12 == 12 -> 12
    afternoon -> hour12 + 12
    hour12 == 12 -> 0
    else -> hour12
}

/** "08:30" → "8:30 SA", "13:05" → "1:05 CH". Chuỗi rỗng/sai định dạng trả về "--". */
fun formatTime12(value: String?): String {
    if (value.isNullOrBlank()) return "--"
    val parts = value.split(":")
    val hour = parts.getOrNull(0)?.toIntOrNull() ?: return "--"
    val minute = parts.getOrNull(1)?.toIntOrNull() ?: 0
    return formatTime12(hour, minute)
}

fun formatTime12(hour24: Int, minute: Int): String =
    "${to12Hour(hour24)}:${"%02d".format(minute)} ${if (hour24 >= 12) "CH" else "SA"}"

private fun weekdayVi(date: LocalDate): String = when (date.dayOfWeek.value) {
    1 -> "Thứ Hai"
    2 -> "Thứ Ba"
    3 -> "Thứ Tư"
    4 -> "Thứ Năm"
    5 -> "Thứ Sáu"
    6 -> "Thứ Bảy"
    else -> "Chủ Nhật"
}
