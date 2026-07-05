package com.ketoanapk.hr.ui

import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonPrimitive

/**
 * Cấu hình các trường nhập cho từng loại đơn — app dựng form động từ đây, tương ứng với
 * `requestFields` bên web (frontend/src/lib/hr.ts). Backend nhận payload jsonb linh hoạt nên
 * việc thêm/bớt trường ở đây là an toàn; các loại chưa khai báo sẽ dùng một ô "Lý do" mặc định.
 */

enum class RfType { Text, Date, Time, Number, Money, Textarea, Segmented }

/**
 * Một trường nhập của đơn.
 * @param hint gợi ý bằng ngôn ngữ đời thường để nhân viên ít rành công nghệ dễ hiểu.
 * @param required bắt buộc điền (kiểm tra khi bấm Gửi).
 * @param options dùng cho trường lựa chọn (Segmented): (value gửi lên, nhãn hiển thị).
 */
data class RequestField(
    val key: String,
    val label: String,
    val type: RfType,
    val hint: String = "",
    val required: Boolean = true,
    val options: List<Pair<String, String>> = emptyList(),
)

val requestFields: Map<String, List<RequestField>> = mapOf(
    "leave" to listOf(
        RequestField("fromDate", "Từ ngày", RfType.Date, "Ngày đầu tiên bạn nghỉ"),
        RequestField("toDate", "Đến ngày", RfType.Date, "Ngày cuối cùng bạn nghỉ"),
        RequestField("days", "Số ngày nghỉ", RfType.Number, "Tự động tính theo khoảng ngày"),
        RequestField("reason", "Lý do nghỉ", RfType.Textarea, "Ví dụ: về quê, việc gia đình…"),
    ),
    "sick" to listOf(
        RequestField("fromDate", "Từ ngày", RfType.Date, "Ngày đầu tiên bạn nghỉ"),
        RequestField("toDate", "Đến ngày", RfType.Date, "Ngày cuối cùng bạn nghỉ"),
        RequestField("days", "Số ngày nghỉ", RfType.Number, "Tự động tính theo khoảng ngày"),
        RequestField("reason", "Lý do nghỉ ốm", RfType.Textarea, "Ví dụ: sốt, đi khám bệnh…"),
    ),
    "business_trip" to listOf(
        RequestField("fromDate", "Từ ngày", RfType.Date, "Ngày bắt đầu đi công tác"),
        RequestField("toDate", "Đến ngày", RfType.Date, "Ngày kết thúc công tác"),
        RequestField("destination", "Nơi công tác", RfType.Text, "Ví dụ: Hà Nội, kho Bình Dương…"),
        RequestField("reason", "Nội dung công tác", RfType.Textarea, "Bạn đi làm việc gì?"),
    ),
    "overtime" to listOf(
        RequestField("date", "Ngày tăng ca", RfType.Date, "Ngày bạn làm thêm giờ"),
        RequestField("fromTime", "Từ giờ", RfType.Time, "Giờ bắt đầu làm thêm"),
        RequestField("toTime", "Đến giờ", RfType.Time, "Giờ kết thúc làm thêm"),
        RequestField("reason", "Nội dung công việc", RfType.Textarea, "Bạn làm thêm việc gì?"),
    ),
    "attendance_fix" to listOf(
        RequestField("date", "Ngày cần điều chỉnh", RfType.Date, "Ngày chấm công bị sai"),
        RequestField("checkIn", "Giờ vào đúng", RfType.Time, "Bỏ trống nếu không cần sửa", required = false),
        RequestField("checkOut", "Giờ ra đúng", RfType.Time, "Bỏ trống nếu không cần sửa", required = false),
        RequestField("reason", "Lý do", RfType.Textarea, "Vì sao chấm công bị sai?"),
    ),
    "forgot_checkin" to listOf(
        RequestField("date", "Ngày quên chấm", RfType.Date, "Ngày bạn quên chấm công"),
        RequestField(
            "direction", "Bạn quên chấm giờ nào?", RfType.Segmented, "Chọn giờ vào hoặc giờ ra",
            options = listOf("in" to "Giờ vào", "out" to "Giờ ra"),
        ),
        RequestField("time", "Giờ thực tế", RfType.Time, "Giờ bạn thực sự vào/ra"),
        RequestField("reason", "Lý do", RfType.Textarea, "Vì sao bạn quên chấm?"),
    ),
    "shift_swap" to listOf(
        RequestField("date", "Ngày đổi ca", RfType.Date, "Ngày cần đổi ca"),
        RequestField("withPerson", "Người nhận ca", RfType.Text, "Tên đồng nghiệp nhận ca giúp"),
        RequestField("reason", "Lý do", RfType.Textarea, "Vì sao bạn cần đổi ca?"),
    ),
    "payment" to listOf(
        RequestField("amount", "Số tiền", RfType.Money, "Số tiền cần thanh toán"),
        RequestField("content", "Nội dung thanh toán", RfType.Textarea, "Thanh toán cho khoản gì?"),
    ),
    "advance" to listOf(
        RequestField("amount", "Số tiền tạm ứng", RfType.Money, "Số tiền bạn muốn tạm ứng"),
        RequestField("reason", "Lý do", RfType.Textarea, "Bạn tạm ứng để làm gì?"),
    ),
    "purchase" to listOf(
        RequestField("item", "Vật tư cần mua", RfType.Text, "Tên món đồ cần mua"),
        RequestField("quantity", "Số lượng", RfType.Number, "Cần mua bao nhiêu?"),
        RequestField("amount", "Dự trù chi phí", RfType.Money, "Ước tính hết bao nhiêu tiền", required = false),
        RequestField("reason", "Mục đích", RfType.Textarea, "Mua để dùng vào việc gì?"),
    ),
    "booking" to listOf(
        RequestField("resource", "Xe / phòng họp", RfType.Text, "Ví dụ: xe tải, phòng họp tầng 2…"),
        RequestField("date", "Ngày sử dụng", RfType.Date, "Ngày bạn cần dùng"),
        RequestField("fromTime", "Từ giờ", RfType.Time, "Giờ bắt đầu dùng"),
        RequestField("toTime", "Đến giờ", RfType.Time, "Giờ trả lại"),
        RequestField("reason", "Mục đích", RfType.Textarea, "Dùng để làm gì?"),
    ),
    "penalty_appeal" to listOf(
        RequestField("penaltyNo", "Mã quyết định phạt", RfType.Text, "Xem ở mục Phạt / kỷ luật"),
        RequestField("penaltyType", "Hình thức phạt", RfType.Text, "Ví dụ: phạt tiền, cảnh cáo…", required = false),
        RequestField("penaltyAmount", "Số tiền phạt", RfType.Money, "Bỏ trống nếu không phải phạt tiền", required = false),
        RequestField("reason", "Nội dung khiếu nại", RfType.Textarea, "Vì sao bạn cho rằng phạt chưa đúng?"),
    ),
)

/** Trường mặc định khi loại đơn chưa được khai báo chi tiết: chỉ cần một ô lý do. */
val defaultRequestFields = listOf(
    RequestField("reason", "Nội dung đơn", RfType.Textarea, "Mô tả chi tiết yêu cầu của bạn"),
)

fun fieldsForType(type: String): List<RequestField> = requestFields[type] ?: defaultRequestFields

/** Với leave/sick, số ngày nghỉ tự tính khớp khoảng Từ ngày → Đến ngày (tính cả 2 đầu). */
fun daysAutoSynced(type: String): Boolean = type == "leave" || type == "sick"

fun fieldLabel(type: String, key: String): String =
    fieldsForType(type).find { it.key == key }?.label ?: key

/** Đổi giá trị lưu trong payload sang chuỗi hiển thị (nhãn lựa chọn, tiền có dấu chấm, số gọn). */
fun fieldDisplayValue(type: String, key: String, value: String): String {
    val f = fieldsForType(type).find { it.key == key }
    return when (f?.type) {
        RfType.Segmented -> f.options.find { it.first == value }?.second ?: value
        RfType.Money -> value.toDoubleOrNull()?.let { formatMoney(it) } ?: value
        RfType.Number -> value.toDoubleOrNull()?.let { d ->
            if (d % 1.0 == 0.0) d.toLong().toString() else value
        } ?: value
        RfType.Date -> formatIsoDate(value)
        else -> value
    }
}

/** Đọc một trường trong payload JSON ra chuỗi thô (chưa định dạng). */
fun JsonObject.readString(key: String): String {
    val el = this[key] ?: return ""
    return runCatching { el.jsonPrimitive.content }.getOrElse { el.toString() }
}

/** Chuỗi hiển thị đã định dạng của một trường trong payload. */
fun JsonObject.displayValue(type: String, key: String): String =
    fieldDisplayValue(type, key, readString(key))

/** Số nguyên/ số thực gửi lên dưới dạng JSON number; các loại khác gửi chuỗi. */
fun jsonValueFor(field: RequestField, raw: String): JsonPrimitive {
    val v = raw.trim()
    return when (field.type) {
        RfType.Number, RfType.Money -> {
            val num = v.toDoubleOrNull()
            when {
                num == null -> JsonPrimitive(v)
                num % 1.0 == 0.0 -> JsonPrimitive(num.toLong())
                else -> JsonPrimitive(num)
            }
        }
        else -> JsonPrimitive(v)
    }
}
