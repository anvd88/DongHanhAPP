# Silent-Face Anti-Spoofing (MiniFASNetV2 + MiniFASNetV1SE)

Lớp chống giả mạo dùng **Silent-Face** của minivision — gồm **2 model** chạy song song rồi cộng softmax.
Engine (`SilentFaceLiveness.cs`) **tự dò** mọi file `*.onnx` trong thư mục này có tên dạng
`*_{H}x{W}_MiniFASNet*` nên chỉ cần đặt đúng tên file vào đây là chạy.

## File cần có (đặt vào `Models/Face/`)

| File | Crop scale | Đầu vào |
|------|-----------|---------|
| `2.7_80x80_MiniFASNetV2.onnx`     | 2.7 | 80×80 |
| `4_0_0_80x80_MiniFASNetV1SE.onnx` | 4.0 | 80×80 |

> Tên file quyết định **scale crop** (token đầu) và **kích thước đầu vào** (`80x80`). Giữ nguyên tên
> như trên. Nếu thiếu cả hai → hệ thống tự lùi về model `face_antispoof_minifasnet.onnx` cũ (2 lớp).

## Cách lấy file ONNX

### Cách 1 — Tải bản ONNX đã convert sẵn (khuyến nghị)
Cộng đồng đã convert sẵn 2 model này sang ONNX, ví dụ kho:
- `hpc203/Silent-Face-Anti-Spoofing-onnxrun` (GitHub) — có sẵn `2.7_80x80_MiniFASNetV2.onnx` và
  `4_0_0_80x80_MiniFASNetV1SE.onnx`.

Tải 2 file `.onnx`, đổi tên đúng như bảng trên rồi đặt vào thư mục này.

### Cách 2 — Tự convert từ checkpoint gốc `.pth`
Lấy 2 checkpoint từ kho gốc **minivision-ai/Silent-Face-Anti-Spoofing**
(`resources/anti_spoof_models/2.7_80x80_MiniFASNetV2.pth` và `4_0_0_80x80_MiniFASNetV1SE.pth`),
rồi convert bằng kiến trúc trong repo đó:

```python
import torch
from src.model_lib.MiniFASNet import MiniFASNetV2, MiniFASNetV1SE

def export(pth, ctor, onnx, conv6_kernel=(5, 5)):
    net = ctor(conv6_kernel=conv6_kernel, num_classes=3, img_channel=3)
    sd = torch.load(pth, map_location="cpu")
    # checkpoint của minivision có tiền tố "module." (DataParallel) → bỏ đi
    sd = { (k[7:] if k.startswith("module.") else k): v for k, v in sd.items() }
    net.load_state_dict(sd)
    net.eval()
    x = torch.randn(1, 3, 80, 80)
    torch.onnx.export(net, x, onnx, input_names=["input"], output_names=["output"], opset_version=11)

export("2.7_80x80_MiniFASNetV2.pth",     MiniFASNetV2,   "2.7_80x80_MiniFASNetV2.onnx")
export("4_0_0_80x80_MiniFASNetV1SE.pth", MiniFASNetV1SE, "4_0_0_80x80_MiniFASNetV1SE.onnx")
```

## Tiền xử lý (đã cài đúng trong engine)
- Crop quanh bbox theo **scale của từng model** (2.7 và 4.0), nếu vượt biên thì **dịch vào trong**, không pad.
- Resize **80×80**, kênh **BGR**, chỉ chia **255** (không trừ mean/std).
- Mỗi model ra **3 lớp** → softmax; **cộng** softmax 2 model; `argmax == LiveClass` ⇒ **THẬT**.
  ⚠️ Với bộ ONNX convert sẵn của **hpc203** (đang dùng), lớp "thật/sống" nằm ở **index 2**, KHÔNG phải
  index 1 như quy ước minivision gốc — đã kiểm chứng bằng thực đo (mặt thật → lớp 2 ≈ 0.99 trên cả 2
  model). Hằng số `LiveClass` trong `SilentFaceLiveness.cs` đặt = 2. Nếu đổi sang bộ ONNX export theo
  quy ước gốc thì sửa `LiveClass` về 1.

## Kiểm tra đã nạp đúng
Gọi `GET /api/chamcong/trangthai` — trường `name` phải chứa
`Silent-Face MiniFASNetV2/V1SE anti-spoof`. Nếu vẫn ghi `MiniFASNet anti-spoof` (không có chữ
Silent-Face) nghĩa là chưa tìm thấy 2 file → kiểm tra lại tên file.
