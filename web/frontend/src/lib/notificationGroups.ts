import { api } from "./api";

/**
 * Nhóm thông báo mỗi người tự tắt/bật. Danh sách khóa phải khớp `Services/NotificationGroups.cs` —
 * máy chủ mới là nơi CHỐT: tắt một nhóm thì thông báo không được ghi vào hộp thư và cũng không bắn
 * push xuống điện thoại, chứ không phải chỉ ẩn đi trên giao diện.
 *
 * Cố ý KHÔNG có công tắc cho cảnh báo bảo mật ("đăng nhập trên thiết bị mới") và thông báo hệ thống:
 * tắt được cảnh báo bảo mật thì kẻ chiếm tài khoản chỉ cần tắt nó là chủ tài khoản không bao giờ biết.
 */
export const NOTIFICATION_GROUPS = [
  {
    key: "delivery",
    label: "Giao hàng",
    hint: "Gán chuyến, tài xế nhận chuyến, đã giao khách, phiếu ký nhận về kho.",
  },
  {
    key: "collection",
    label: "Thu tiền",
    hint: "Tài xế nhận lệnh, đã thu tiền khách, bàn giao lệch, thủ quỹ nhận đủ.",
  },
  {
    key: "accounting",
    label: "Chứng từ & phiếu chi",
    hint: "Phiếu xuất kho phát hành hoặc bị hủy, phiếu chi chờ duyệt / đã duyệt / đã chi.",
  },
  {
    key: "work",
    label: "Việc được giao & đơn từ",
    hint: "Giao việc, nộp nghiệm thu, đơn chờ duyệt, đơn được duyệt hoặc bị từ chối.",
  },
  {
    key: "people",
    label: "Nhân sự & chấm công",
    hint: "Quyết định phạt, duyệt khuôn mặt, nhắc chấm công.",
  },
] as const;

export type NotificationGroupKey = (typeof NOTIFICATION_GROUPS)[number]["key"];
export type NotificationGroupState = Record<string, boolean>;

/** Chưa từng đặt thì máy chủ trả về BẬT hết — người mới không bị im lặng mất thông báo. */
export async function loadNotificationGroups(): Promise<NotificationGroupState> {
  const result = await api.get<{ groups: NotificationGroupState }>("/api/preferences/notifications");
  return result.groups ?? {};
}

export async function saveNotificationGroup(
  group: string,
  enabled: boolean,
): Promise<NotificationGroupState> {
  const result = await api.put<{ groups: NotificationGroupState }>("/api/preferences/notifications", {
    groups: { [group]: enabled },
  });
  return result.groups ?? {};
}
