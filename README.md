# KetoanMiniDotNet

Ban nang cap giao dien C# .NET 8 WinForms theo huong phan mem ke toan desktop kieu MISA mini.

## Da co trong ban nay

- Header: logo, ten cong ty/chi nhanh, ky ke toan, thong bao, profile.
- Header co thanh gio lam viec 8h-12h va 13h-17h, hien gio hien tai chay theo thanh tien do.
- Tu 12:00 den 13:00, tai khoan User se duoc thong bao den gio nghi trua va khoa vung lam viec; tai khoan Admin khong bi khoa.
- Khi mo app se hien man hinh dang nhap/dang ky.
  - Tai khoan admin mac dinh: `admin`
  - Mat khau admin mac dinh: `admin`
  - Mat khau duoc luu trong SQLite bang hash PBKDF2, khong luu mat khau tho.
- Form dang nhap khong hien goi y admin tren giao dien; co tuy chon hien/an mat khau.
- Link `Quen mat khau` nam ngay trong man hinh dang nhap, cho nhan vien nhap ten tai khoan de gui yeu cau cho admin.
- Khi admin dang nhap, nut `Thong bao` se hien so luong yeu cau quen mat khau dang cho; trong `Quan ly User`, dong user do hien `Can doi MK`.
- Khi admin nhap mat khau moi cho user va bam luu, yeu cau quen mat khau cua user do tu chuyen sang da xu ly.
- Khu vuc ten nguoi dung ben canh nut `Thong bao` la nut ho so:
  - `Sua ho so`: doi ten hien thi va doi mat khau cua chinh tai khoan dang dang nhap.
  - `Dang xuat`: app hoi xac nhan truoc khi dang xuat va quay lai man hinh dang nhap.
- Tai khoan thuong co the dang ky o man hinh dang nhap, quyen mac dinh la `User`.
- Chi admin moi thay muc `Quan ly User` va `Nhat ky` tren sidebar.
- Man hinh `Quan ly User` cho admin xem admin mac dinh va them/sua/xoa/khoa tai khoan User cap thap hon.
- Man hinh `Nhat ky` cho admin xem ai dang nhap, ai nhap chung tu/thanh toan, ai sua/xoa thanh toan, ai sua/xoa KH, ai xuat Excel.
- Nhat ky thao tac chi bat dau ghi tu ban co dang nhap nay; du lieu cu truoc do khong co thong tin nguoi nhap/sua.
- Logo app da cap nhat theo logo tach nen Inox Cuong Phat; header hien `Cong ty TNHH Inox Cuong Phat` va icon exe dung asset trong `assets/logo_cuong_phat.png` / `.ico`.
- Sidebar trai co the thu gon.
- Workspace mot man hinh: khong con thanh tab tren cung; dang mo muc nao thi muc do duoc lam noi bat tren sidebar trai.
- Chuyen muc da giam nhay: app tao man hinh moi truoc khi thay man hinh cu, bat double-buffer cho Form/Panel/DataGridView va chi refresh du lieu cua man hinh dang mo.
- Dashboard tile-based, co drill-down khi double click KPI.
- Bieu do dong tien Thu/Chi va co cau chi phi ve bang custom WinForms, khong can package ngoai.
- Man hinh phieu thu/chi dang master-detail.
- Goi y Ten KH khi go: hien ca ten chuan va bi danh, ho tro go khong dau, dung phim len/xuong va Enter/Tab de chon.
- Bang bi danh KH: app doc `data/ThongtinKH.xlsx`, table `Thong_tin_thanh_toan`, roi luu vao SQLite de them/xoa trong app.
  - Cot 1 la Ten KH chuan trong app.
  - Cac cot sau la ten goi nhanh/ten nho.
  - Khi nhap ten goi nhanh vao o Ten KH, app tu doi ve ten chuan o cot 1.
- Man hinh `Khach hang`:
  - Click chuot phai vao mot KH de mo menu `Xem chi tiet`, `Sua thong tin KH`, `Sua bi danh`, `Xoa khach hang`.
  - Menu chuot phai hien ngay gan o dang bam, ke ca khi bam vao cot MST/dien thoai/dia chi/ghi chu.
  - `Sua bi danh` mo mot cua so noi rieng, cho phep them/xoa bi danh cua KH dang chon.
  - Neu nhap KH bang bi danh, app van doi ve `Ten KH` chuan nhung luu them cot `Ten da nhap` de tra soat.
- Man hinh Thanh toan co them/sua/xoa:
  - O `So tien` nam ngay sau `Ten KH`, chon KH xong bam Tab la nhap so tien.
  - Nhap tien dang `100000000` app tu hien thi thanh `100.000.000`.
  - Them moi: nhap thong tin roi bam `Luu`.
  - Sua nhap nham: chon dong trong bang roi bam `Sua`, hoac double-click dong do, sua tren form roi bam `Cap nhat`.
  - Xoa: chon dong trong bang roi bam `Xoa`, app se hoi xac nhan truoc khi xoa.
  - Dang sua ma khong muon luu: bam `Huy`.
- Man hinh Ban hang:
  - Nhap Ten KH, ngay ban, so phieu, dien giai.
  - Nhap hang hoa/dich vu theo grid: ten hang, DVT, so luong, don gia, thanh tien tu tinh.
  - Cot `Don gia` tu dinh dang tien khi nhap, vi du `100000000` thanh `100.000.000`.
  - Co checkbox `VAT` mac dinh duoc tich san; neu bo tich thi khi luu/lam moi sang phieu moi app tu tich lai.
  - Bam `Luu ban hang` de tao chung tu `Ban hang`; du lieu vao dashboard, so cai, pivot va export Excel.
- DataGrid nhap lieu ke toan:
  - Cot `So tien` tu dinh dang tien khi nhap/paste, vi du `100000000` thanh `100.000.000`.
  - Copy/Paste tu Excel bang `Ctrl+V`.
  - `Enter` nhay sang o tiep theo, cuoi dong tu tao dong moi.
  - `Insert` them dong.
  - `Delete` / `Ctrl+Delete` xoa dong.
  - `F4` mo popup lookup tai khoan ngay tai o dang dung.
  - Click vao o `TK No` / `TK Co` hien lookup tai o, khong mo cua so moi.
  - Footer sum realtime.
  - Co dinh cac cot dau nhu Excel.
- So cai la man hinh rieng de doi chieu voi phieu chi/phieu thu.
- `Chi tiet KH`: o chon Ten KH va dropdown da mo rong de hien ten dai ro hon; chon KH de xem lich su mua/ban/thanh toan, tong ban, tong mua, da thanh toan va con lai.
- Cac bang tra soat nhu Thanh toan gan day, Ban hang gan day, So cai, Chi tiet KH va file Excel xuat ra co cot `Ten da nhap` de xem lai bi danh/ten goi ban dau.
- Trong man hinh `Khach hang`, double-click mot KH de mo nhanh man hinh chi tiet giao dich cua KH do.
- Pivot bao cao co khu vuc truong du lieu va vung Rows de keo/tha truong, tao bao cao nhom du lieu local.
- Luu du lieu local bang SQLite tai `data/ketoan_mini.db`.
  - Bang `app_users` luu tai khoan.
  - Bang `audit_logs` luu nhat ky thao tac.
  - Bang `password_reset_requests` luu yeu cau quen mat khau.
- Neu co file JSON cu `data/ketoan_data.json`, app se tu import sang SQLite khi database moi chua co du lieu.
- Xuat Excel truc tiep dang `.xlsx` bang OpenXML noi bo, khong can mo Excel/PowerShell khi xuat.
  - Co sheet `Tong hop`.
  - Moi KH co 1 sheet rieng, gom chi tiet giao dich mua/ban/thanh toan va cot `Con lai`.
  - Moi sheet KH co them cot `Ten da nhap` de doi chieu neu giao dich duoc nhap bang bi danh.
  - Man hinh xuat co thanh loading 0-100%, khoa nut khi dang xuat de tranh bam lap.
  - Ho tro chay dong lenh: `KetoanMini.exe --export-openxml C:\duongdan\Cong_no.xlsx`.

## Yeu cau cai dat

- Windows.
- Microsoft Excel hoac LibreOffice/WPS de mo file `.xlsx` sau khi xuat. App khong can Excel de tao file `.xlsx`.
- .NET 8 SDK.

Tai .NET 8 SDK tu Microsoft:
https://dotnet.microsoft.com/en-us/download/dotnet/8.0

## Chay trong VS Code

Mo thu muc:

`outputs/KetoanMiniDotNet/src/KetoanMini`

Sau khi cai .NET 8 SDK:

```powershell
dotnet run
```

## Build file chay

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

File publish nam trong:

`bin/Release/net8.0-windows/win-x64/publish`

## Database va backup

- File database khi chay ban Debug nam tai:

`src/KetoanMini/bin/Debug/net8.0-windows/data/ketoan_mini.db`

- Khi dong goi/publish, database se nam trong thu muc `data` canh file `KetoanMini.exe`.
- Muon backup du lieu, chi can copy file `ketoan_mini.db` khi app da dong.
- Build hien tai da chay duoc tren .NET 8 SDK voi 0 warning, 0 error.
