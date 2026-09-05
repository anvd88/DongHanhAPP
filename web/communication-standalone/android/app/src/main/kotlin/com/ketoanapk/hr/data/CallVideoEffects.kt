package com.ketoanapk.hr.data

/**
 * Trạng thái HIỆU ỨNG VIDEO cuộc gọi (làm mịn da + bộ lọc màu), áp NGAY TRÊN MÁY bằng GPU trước khi
 * mã hoá gửi đi — nên NGƯỜI BÊN KIA cũng thấy, mà server KHÔNG phải gánh gì (vẫn P2P, vẫn mã hoá đầu-cuối).
 *
 * TỐI ƯU NHIỆT: khi KHÔNG bật hiệu ứng ([hasEffect] = false) thì [BeautyVideoProcessor] chuyển tiếp
 * khung hình NGUYÊN BẢN, KHÔNG chạm GPU → gần như 0 xử lý, không nóng máy. Chỉ khi bật mới chạy 1 lượt
 * shader nhẹ.
 */
object CallVideoEffects {
    // 0f = tắt, 1f = mịn tối đa.
    @Volatile var beauty: Float = 0f
        private set

    // 0 = gốc, 1 = nắng ấm, 2 = trong xanh, 3 = hồng đào, 4 = cổ điển, 5 = mộng mơ (khớp thứ tự UI).
    @Volatile var filter: Int = 0
        private set

    fun setBeauty(value: Float) { beauty = value.coerceIn(0f, 1f) }
    fun setFilter(index: Int) { filter = index.coerceIn(0, 5) }

    /** Có đang bật hiệu ứng nào không — dùng để processor bỏ qua hoàn toàn khi tắt (tiết kiệm điện/nhiệt). */
    fun hasEffect(): Boolean = beauty > 0.01f || filter != 0

    /** Reset khi kết thúc cuộc gọi. */
    fun reset() { beauty = 0f; filter = 0 }
}
