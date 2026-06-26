# Camera Snapshot Bridge

This bridge lets FFmpeg read the camera RTSP stream and continuously write a single JPEG frame.
The backend reads this image instead of opening RTSP directly.

```text
Camera RTSP H.265 -> local RTSP header-fix proxy -> FFmpeg -> latest.jpg -> KetoanMini.Api
```

Start:

```powershell
powershell -ExecutionPolicy Bypass -File tools\camera-snapshot\Start-CameraSnapshot.ps1
```

When `KioskCamera:FrameSource` is `ImageFile` and
`KioskCamera:AutoStartSnapshotBridge` is `true`, the ASP.NET backend starts this
bridge automatically on `dotnet run --launch-profile lan`.

Stop:

```powershell
powershell -ExecutionPolicy Bypass -File tools\camera-snapshot\Stop-CameraSnapshot.ps1
```

Logs are written to `.codex-logs\camera-snapshot`.
