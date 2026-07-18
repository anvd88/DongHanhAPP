# Xóa bí mật khỏi lịch sử Git (P4 – mục 1)

Các bí mật **đã lộ trong lịch sử** repo (đã được rotate ở P0 nên hiện không còn dùng được — đây là bước dọn dẹp, không khẩn cấp):

| Bí mật | Nơi xuất hiện trong history |
|---|---|
| Mật khẩu DB `matkhau123`, `postgres`, `KetoanMini@2026` | các bản cũ của `web/backend/KetoanMini.Api/appsettings.json` |
| Khóa/chứng chỉ `auto.key`, `auto.crt` | `web/tools/video-bridge/` |

> ⚠️ **CẢNH BÁO:** thao tác này **viết lại toàn bộ lịch sử Git** → thay đổi mọi commit SHA. Sau khi làm phải **force-push** và **mọi người phải clone lại** (không được `git pull` bản cũ vào). Chỉ làm khi đã hẹn với cả nhóm. Vì secret đã rotate, có thể bỏ qua nếu repo có nhiều người đang làm việc.

## Cách làm (khuyến nghị: git-filter-repo)

```bash
# 1) Cài công cụ (một lần)
pip install git-filter-repo

# 2) Clone MỚI, sạch (bản mirror) để thao tác
git clone --mirror https://github.com/anvd88/KetoanMini.git ketoan-scrub
cd ketoan-scrub

# 3) Tạo file thay thế chuỗi bí mật (bằng ***REMOVED***)
cat > replacements.txt <<'EOF'
matkhau123==>***REMOVED***
KetoanMini@2026==>***REMOVED***
Password=postgres==>Password=***REMOVED***
EOF

# 4) Thay chuỗi + xóa hẳn file khóa/chứng chỉ khỏi mọi commit
git filter-repo --replace-text replacements.txt \
  --path web/tools/video-bridge/auto.key --path web/tools/video-bridge/auto.crt --invert-paths

# 5) Đẩy đè (viết lại history trên GitHub)
git push --force --mirror
```

## Sau khi scrub
- Mọi thành viên: **xóa clone cũ, clone lại** repo (bản local cũ vẫn còn history bẩn).
- Kiểm tra lại bằng gitleaks (đã cấu hình ở `.gitleaks.toml` + workflow `security.yml`):
  ```bash
  docker run -v "$PWD:/repo" zricethezav/gitleaks:latest detect --source=/repo -v
  ```
- Xác nhận các secret cũ đã **không còn đăng nhập được** (đúng ra đã rotate ở P0).

## Lựa chọn thay thế: BFG Repo-Cleaner
```bash
bfg --replace-text replacements.txt   # thay chuỗi
bfg --delete-files "{auto.key,auto.crt}"
git reflog expire --expire=now --all && git gc --prune=now --aggressive
git push --force
```
