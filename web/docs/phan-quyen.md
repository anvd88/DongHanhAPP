# Phân quyền: vai trò, quyền, phạm vi, giao diện

Tài liệu này mô tả cách hệ thống quyết định **ai được làm gì**. Đọc file này trước khi thêm vai trò
mới, thêm endpoint nhạy cảm, hay sửa menu.

## Nguyên tắc gốc

> URL và localStorage chỉ quyết định **hiện ra cái gì**. Backend quyết định **làm được cái gì**.

Người dùng sửa được URL, localStorage và cả mã JavaScript trên trình duyệt. Vì vậy:

- Không có `?role=` nào có tác dụng. Không có giá trị nào trong localStorage cấp thêm quyền.
- Frontend ẩn/hiện menu chỉ để đỡ rối mắt. Gõ thẳng URL của trang bị ẩn thì trang mở ra rỗng và mọi
  lời gọi API trả 403.
- Quyền được **tính lại từ CSDL ở MỖI request** (không đọc từ JWT), nên cấp/thu quyền có hiệu lực
  ngay từ thao tác kế tiếp — không ai phải đăng xuất, không ai bị gián đoạn.

## Bốn thành phần

| Thành phần | Trả lời câu hỏi | Định nghĩa ở |
|---|---|---|
| **Vai trò** (role) | Người này thuộc nhóm nào? | `Security/AppRoles.cs` |
| **Quyền** (permission) | Được làm hành động gì? | `Security/Permissions.cs` |
| **Phạm vi** (scope) | Được xem dữ liệu của ai? | `AccessProfileService.ResolveScopeAsync` |
| **Giao diện** (ui profile) | Vào thẳng màn hình nào? | `AccessProfileService.UiProfileFor` |

Vai trò **không** được kiểm tra trực tiếp trong nghiệp vụ. Vai trò chỉ dùng để **tra ra quyền**, và
nghiệp vụ kiểm tra quyền. Nhờ vậy thêm vai trò mới = thêm một dòng trong bảng vai trò→quyền.

## Vai trò hiện có

`Admin`, `Accounting` (Kế toán), `ChiefAccountant` (Kế toán trưởng), `Hr` (Nhân sự),
`Manager` (Trưởng phòng), `Employee` (Nhân viên), `Warehouse` (Thủ kho), `Kiosk` (máy chấm công).

Một tài khoản có **một vai trò chính** (cột `app_users.role`) và **nhiều vai trò phụ**
(bảng `user_roles`). Quyền của tài khoản là **hợp** của mọi vai trò đang giữ.

Vai trò phụ cấp được kèm `expires_at` → ủy quyền tạm (trưởng phòng đi vắng); hết hạn tự mất hiệu
lực, không cần ai nhớ đi thu hồi.

Ngoại lệ cố ý: **Admin không lập/duyệt được phiếu chi tiền mặt**. Chỉ Kế toán thuộc phòng ban có cờ
`is_accounting` mới đụng được tiền mặt (`PayoutVoucherEndpoints.IsCashierAsync`).

## Thêm một vai trò mới

1. Thêm hằng số + nhánh `Normalize` + nhãn tiếng Việt trong `Security/AppRoles.cs`.
2. Thêm dòng trong `Permissions.RolePermissions` — vai trò đó có những quyền nào.
3. Muốn cấp được như vai trò phụ thì thêm vào `AppRoles.Secondary` và `SECONDARY_ROLES`
   (`frontend/src/pages/NhanSu.tsx`) để hiện nút bấm.

Không phải sửa endpoint nào. `PermissionMapTests` sẽ báo đỏ nếu quên bước 2.

## Chốt cửa một endpoint

```csharp
var g = app.MapGroup("/api/xxx").RequirePermission(Permissions.XxxManage);
// hoặc cho một endpoint đơn lẻ:
g.MapPost("/duyet", Handler).RequirePermission(Permissions.VouchersApprove);
```

Cần rẽ nhánh **bên trong** handler (không phải chặn cửa) thì dùng `u.Can(Permissions.X)`.

**KHÔNG** dùng `RequireRole("Admin")` cho endpoint mới, và không tự trả `Forbid()` theo tên vai trò.

### Đóng mặc định khi không xác định được quyền

Claim quyền **không nằm trong JWT** — middleware trong `Program.cs` dựng lại từ CSDL ở mỗi request.
Nếu CSDL không đọc được, request đó **không có claim quyền nào**, nên mọi endpoint
`.RequirePermission(...)` trả 403 thay vì tin vào quyền cũ. Token cũ mang sẵn claim `perm` cũng bị
gỡ bỏ trước khi chấm quyền (có test: `ClaimQuyenGanSanTrongToken_BiBoQua`).

## Hồ sơ truy cập (AccessProfile)

`GET /api/auth/access-profile` trả về vai trò, quyền, phạm vi, giao diện mặc định, trang đích và
`authorizationVersion`. Frontend (`lib/access.tsx`) dùng **duy nhất** nguồn này để dựng giao diện:

- `useAccess().can(PERM.x)` — ẩn/hiện menu, nút.
- `<Protected requires={PERM.x}>` — chặn route, thiếu quyền thì về trang đích của tài khoản.
- Trang đích sau đăng nhập lấy từ `landingPath`, không phải hằng số cứng.

Tên quyền trong `frontend/src/lib/access.tsx` (`PERM`) phải **khớp từng chữ** với
`Security/Permissions.cs`. Gõ sai thì `can()` lặng lẽ trả false và menu biến mất không báo lỗi.

## Đổi quyền có hiệu lực thế nào

`UserEndpoints.AfterAuthorizationChangeAsync` gom mọi việc phải làm sau một lần đổi quyền:

1. Tăng `app_users.authorization_version`.
2. Ghi `user_role_history` — trước → sau, ai đổi, lý do, địa chỉ truy cập.
3. Ghi `audit_logs` để tra cùng dòng thời gian với thao tác khác.
4. Bắn tín hiệu realtime `changed("access")` tới **đúng người bị đổi quyền** (không phát cả công ty).

Người đó đang mở một trang không còn quyền sẽ được đưa về trang mặc định ngay, không cần tải lại
trang hay đăng nhập lại. Quyền thật thì đã đổi từ request kế tiếp rồi — tín hiệu chỉ để giao diện
khỏi hiển thị sai.

## Phiên đăng nhập: cookie cho web, Bearer cho app

| | Trình duyệt | Ứng dụng Android |
|---|---|---|
| Phiên nằm ở | Cookie `km_auth` (HttpOnly) | JWT trong thân phản hồi, app tự lưu |
| JavaScript đọc được? | **Không** | không liên quan |
| Hạn | 7 ngày, gia hạn trượt khi còn hoạt động | 365 ngày |
| Chống CSRF | Cookie `km_csrf` + header `X-CSRF-Token` | không cần (không phải ambient credential) |

Mã: `Security/AuthCookies.cs` (đặt/xoá/kiểm cookie), `Program.cs` (đọc cookie, middleware CSRF,
gia hạn trượt), `frontend/src/lib/api.ts` (`session`, header CSRF).

**Vì sao đổi:** JWT trong localStorage thì bất kỳ JavaScript nào trên trang cũng đọc được — một lỗ
XSS (kể cả trong thư viện phụ thuộc) là đủ để lấy trọn phiên còn hạn và dùng lại ở máy khác. Cookie
HttpOnly thì XSS vẫn có thể gọi API trong lúc trang còn mở, nhưng **không mang phiên đi được**.

Business realtime dùng SSE cùng origin, tự xác thực lại phiên định kỳ và tạo stream mới khi đổi tài
khoản. Mã WebSocket của mô-đun giao tiếp đã được tách khỏi hệ thống hiện tại.

**Thu hồi thiết bị từ xa** (`/api/auth/devices`) chạy xuyên qua cả hai kiểu phiên: phiên Bearer thu
hồi được phiên cookie và ngược lại, vì chốt nằm ở cột `revoked` của `user_sessions` chứ không ở kiểu
xác thực. Màn quản lý thiết bị **chỉ có trong ứng dụng Android** (`SettingsScreens.kt`), web chưa có.
Hai điểm phải giữ: lệnh thu hồi lọc theo `username` (sid do client tự đặt, không lọc thì đoán trúng
sid là đá được người khác ra), và lệnh thu hồi phải qua chốt CSRF (nếu không, trang lạ đá được nạn
nhân ra khỏi mọi thiết bị chỉ bằng cách dụ họ mở một trang). Cả hai đều có test trong
`CookieSessionTests`.

**Lưu ý triển khai:** cookie chỉ được gửi khi frontend và API **cùng origin** (đúng cách hệ thống
đang chạy: API phục vụ luôn `wwwroot`). Tách origin thì phải đổi `credentials` sang `include` ĐỒNG
THỜI bật `AllowCredentials` và siết danh sách origin trong CORS — không làm nửa vời.

Tắt khẩn cấp: `Security:CookieAuth=false` → quay lại Bearer + localStorage (chỉ dùng để chữa cháy).

## Không gian làm việc (Làm việc / Quản trị)

`lib/workArea.tsx` chia menu làm hai khu: việc của chính mình và khu quản trị/nghiệp vụ. Nút chuyển
chỉ hiện với người có **cả hai**, và **chỉ đổi menu — không cấp thêm quyền nào**.

## Kiểm thử

- `PermissionMapTests` — khoá bảng vai trò→quyền (không cần CSDL).
- `AccessProfileEndpointTests` — chạy cả hệ thống thật: hồ sơ đúng, claim quyền bịa bị bỏ qua, cấp/thu
  quyền có hiệu lực ngay với token cũ, vai trò tạm hết hạn tự mất.
- `TokenRoleFreshnessTests`, `SecurityTests` — các chốt cũ vẫn giữ nguyên.
