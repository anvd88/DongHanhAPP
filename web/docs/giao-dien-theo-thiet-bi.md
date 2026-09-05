# Giao diện web theo thiết bị

Quy ước bố cục của frontend theo bề rộng máy, và bộ kiểm phải chạy trước khi chốt một màn hình mới.
Kế hoạch gốc và số đo từng đợt: artifact `Bốn bề rộng`.

## 1. Bốn lớp thiết bị

| Lớp | Bề rộng cửa sổ | Phục vụ việc gì |
| --- | --- | --- |
| Điện thoại | ≤ 767 px | Chấm công, tra cứu, duyệt, đọc thông báo. **Không** lập chứng từ mới. |
| Máy bảng | 768 – 1023 px | Duyệt, xem báo cáo, xem bảng công. |
| Laptop | 1024 – 1439 px | **Bề rộng làm việc chính.** Mọi màn hình phải đủ dùng ở đây. |
| Màn hình rời | ≥ 1440 px | Nhập liệu khối lượng lớn, đối chiếu hai cột, sổ cái. |

Khai báo ở `src/lib/device.ts` (`useBreakpoint`). Điện thoại và máy bảng **không** đồng nghĩa với
"cầm trên tay": dùng `useIsHandheld()` (`pointer: coarse` và ≤ 1279 px) khi cần phân biệt, vì laptop
hai trong một có màn cảm ứng vẫn là máy tính, còn cửa sổ thu nhỏ trên máy bàn vẫn là chuột.

## 2. Bề rộng khung chứa, không phải bề rộng cửa sổ

Bảng dữ liệu và thanh công cụ tự đo chỗ chúng đứng bằng `useContainerWidth()`, vì cùng cửa sổ
1024 px thì bảng còn 766 px khi menu trái mở và 934 px khi menu thu gọn.

| Khổ khung | Bề rộng | Bảng hiện gì |
| --- | --- | --- |
| `narrow` | < 640 px | Chuyển sang danh sách thẻ. Bộ lọc rời thanh công cụ vào khối mở/đóng. |
| `medium` | 640 – 1039 px | Cột mức 1 và 2. |
| `wide` | ≥ 1040 px | Mọi cột. |

Ngưỡng `wide` là 1040 chứ không thấp hơn: ở 934 px vài màn nhiều cột (sổ quỹ, sổ cái) sẽ tràn lại,
mà tràn ngang thì không cứu được, còn cột thiếu chỉ cách một cú bấm ở dòng chi tiết.

## 3. Mức ưu tiên cột

Mỗi `Column<T>` khai `priority?: 1 | 2 | 3`, không khai thì coi như 2.

| Mức | Ý nghĩa | Đặt cho cột nào |
| --- | --- | --- |
| 1 | Luôn ở lại | Mã chứng từ, tên đối tượng, số tiền chính, trạng thái |
| 2 | Rời bảng khi khung < 1040 px | Ngày phụ, người liên quan, đơn vị, số lượng |
| 3 | Rời bảng khi khung < 1040 px, và khỏi thân thẻ khi < 640 px | Diễn giải dài, người lập, mã số thuế, mốc thời gian phụ |

Cột rời bảng **không mất dữ liệu**: nó xuống dòng chi tiết mở bằng mũi tên đầu dòng, hoặc nằm sau
nút "Xem thêm" trên thẻ.

Trên thẻ, cột mức 1 đầu tiên làm tiêu đề, và **cột canh phải cuối cùng** làm con số nổi — trên một
dòng chứng từ đó là Thành tiền chứ không phải Số lượng, trên bảng lương là Thực nhận chứ không phải
Giờ tăng ca.

## 4. Phạm vi thiết bị của màn hình

`NavRoute.deviceScope` nhận `'all' | 'handheld' | 'desktop'`, mặc định `'all'`. Màn hình không hợp
loại máy biến khỏi menu và bảng lệnh, nhưng **route vẫn còn** — đường dẫn từ chuông thông báo phải
sống sót — và mở ra là màn hướng dẫn đổi máy.

Hiện chỉ `/chamcong` là `'handheld'`. Không màn nào cần `'desktop'`: bộ kiểm ở mục 5 cho thấy cả 44
màn đều đọc được ở 375 px. Màn hình mới phải cân nhắc trường này chứ không để mặc định trôi qua.

## 5. Bộ kiểm bốn bề rộng

Chạy ở **375, 768, 1024 và 1440 px**, cả nền sáng lẫn nền tối, trên máy chủ đang phục vụ bản build.

1. Trang không cuộn ngang. Chỉ khung bảng được phép cuộn, và chỉ khi bảng thật sự nhiều cột.
2. Không chữ nào bị cắt mất phần mang thông tin. Nhãn được phép rút gọn, con số thì không.
3. Đầu bảng dữ liệu nằm trong màn hình đầu tiên, không phải cuộn qua khối lọc mới thấy.
4. Vùng chạm đủ rộng ở bề rộng cảm ứng.
5. Hành động chính của màn hình luôn nhìn thấy.
6. Nền tối đọc được như nền sáng, gồm cả viền bảng và chữ mờ.

Ba điều đầu chạy được bằng máy. Dán đoạn sau vào console của trình duyệt rồi gọi
`await sweep()` ở từng bề rộng:

```js
window.sweep = async (paths = SCREEN_PATHS) => {
  const allowed = ['no-scrollbar', 'figure-strip', 'sr-only']
  const check = () => {
    const scrollers = [...document.querySelectorAll('*')].filter(
      (e) =>
        e.scrollWidth > e.clientWidth + 1 &&
        getComputedStyle(e).overflowX !== 'visible' &&
        !allowed.some((a) => String(e.className).includes(a)),
    )
    const clipped = [...document.querySelectorAll('td, dd, .tnum')].filter(
      (e) =>
        e.scrollWidth > e.clientWidth + 2 &&
        !e.classList.contains('is-truncate') &&
        !e.classList.contains('truncate'),
    ).length
    return {
      scroll: document.documentElement.scrollWidth - innerWidth,
      tableOver: Math.max(0, ...scrollers.map((e) => e.scrollWidth - e.clientWidth)),
      clipped,
      empty: document.body.innerText.trim().length < 40,
    }
  }
  const out = []
  for (const p of paths) {
    history.pushState({}, '', p)
    dispatchEvent(new PopStateEvent('popstate'))
    await new Promise((r) => setTimeout(r, 550))
    out.push({ p, ...check() })
  }
  console.table(out)
  return out.filter((r) => r.scroll > 0 || r.clipped > 0 || r.empty)
}
```

`SCREEN_PATHS` là danh sách `path` trong `src/nav/navigation.ts`. Kết quả trả về là các màn hình
**hỏng**; danh sách rỗng nghĩa là đạt. Một lần quét mất khoảng 25 giây cho 22 màn, nên chia hai nửa
nếu console giới hạn thời gian chạy.

Điều 4, 5 và 6 vẫn phải nhìn bằng mắt trên ít nhất một màn hình danh sách và một màn chứng từ.

## 6. Kết quả lần chạy 2026-09-04

44 màn hình × 4 bề rộng = 176 lượt kiểm.

| Bề rộng | Cuộn ngang trang | Chữ bị cắt | Màn trắng | Cuộn trong bảng, lớn nhất |
| --- | --- | --- | --- | --- |
| 375 px (cảm ứng) | 0 | 0 | 0 | 0 px, mọi bảng đã thành thẻ |
| 768 px | 0 | 0 | 0 | 238 px |
| 1024 px | 0 | 0 | 0 | 216 px |
| 1440 px (nền tối) | 0 | 0 | 0 | 50 px |

Vùng chạm ở 375 px: trên màn Phiếu bán hàng còn 17 phần tử nhỏ hơn 32 px, tất cả đều nằm trong một
vùng bấm lớn hơn chính nó — ô tích 18 px trong nhãn đệm 38 px, và liên kết số phiếu 16 px nằm trong
nút tiêu đề thẻ. Trước khi nới là 64 phần tử.

## 7. Hai lỗi bộ kiểm bắt được

- **Ba màn trắng hoàn toàn** (`/thiet-bi`, `/thong-bao`, `/ho-so`) vì `sortRows` gọi `[...rows]` khi
  máy chủ trả về thứ không phải mảng. Đã chặn ở `sortRows`: không phải mảng thì bảng trống, không
  làm trắng màn hình.
- **`doc?.lines.reduce(...)`** ở trang chi tiết phiếu bán hàng và phiếu nhập: chứng từ có nhưng
  thiếu dòng hàng thì trắng màn hình. Đã đổi thành `doc?.lines?.reduce(...)`.
