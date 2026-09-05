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
    val dataChanged = MutableSharedFlow<String>(extraBufferCapacity = 16)

    fun signalDataChanged(scope: String = "all") {
        dataChanged.tryEmit(scope.ifBlank { "all" })
    }

    // Buộc đăng xuất NGAY (tài khoản vừa đăng nhập ở thiết bị khác → 1 máy/tài khoản). Kèm lý do hiện ở
    // màn đăng nhập. RealtimeClient phát khi nhận "kicked"; HrViewModel lắng nghe rồi đăng xuất tức thì.
    val forceLogout = MutableSharedFlow<String>(extraBufferCapacity = 4)

    fun signalForceLogout(reason: String) {
        forceLogout.tryEmit(reason)
    }
}
