package com.ketoanapk.hr.data

import kotlinx.coroutines.flow.MutableSharedFlow

/**
 * Kênh sự kiện toàn app (không phụ thuộc vòng đời) để nối FCM push ↔ ViewModel đang chạy.
 *
 * Khi có push báo dữ liệu đổi từ máy chủ (đơn được duyệt/từ chối, đơn mới chờ duyệt, phạt…),
 * [HrMessagingService] phát [dataChanged]; [HrViewModel] lắng nghe rồi làm mới NGAY màn đang xem —
 * nhờ vậy đơn từ cập nhật gần như tức thì (theo độ trễ FCM ~1–2s) thay vì chờ nhịp poll nền.
 */
object AppEvents {
    // replay=0, có buffer để tryEmit không bị rớt khi chưa có collector tức thời.
    val dataChanged = MutableSharedFlow<Unit>(extraBufferCapacity = 16)

    fun signalDataChanged() {
        dataChanged.tryEmit(Unit)
    }
}
