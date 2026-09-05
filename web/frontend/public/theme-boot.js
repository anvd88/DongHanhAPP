// Đặt chủ đề TRƯỚC khi React dựng cây để không nháy nền trắng khi người dùng chọn nền tối.
// Phải là tệp riêng chứ không phải <script> nội tuyến: CSP của backend là
// "script-src 'self' 'wasm-unsafe-eval'" (không có 'unsafe-inline'), script nội tuyến bị chặn thẳng.
try {
  var choice = localStorage.getItem('km.theme') || 'system'
  var dark =
    choice === 'dark' ||
    (choice === 'system' && matchMedia('(prefers-color-scheme: dark)').matches)
  document.documentElement.dataset.theme = dark ? 'dark' : 'light'
} catch (e) {}
