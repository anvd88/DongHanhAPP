package com.ketoanapk.hr.ui

import android.app.Activity
import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.os.PersistableBundle
import android.view.MotionEvent
import android.view.View
import android.widget.Button
import android.widget.TextView
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.FocusMeteringAction
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.core.UseCaseGroup
import androidx.camera.core.ViewPort
import androidx.camera.core.resolutionselector.AspectRatioStrategy
import androidx.camera.core.resolutionselector.ResolutionSelector
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.core.content.ContextCompat
import com.google.mlkit.vision.barcode.BarcodeScanner
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.ZoomSuggestionOptions
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import com.ketoanapk.hr.R
import com.ketoanapk.hr.data.HrRepository
import com.ketoanapk.hr.data.QrActionEnvelope
import com.ketoanapk.hr.data.QrResolveOutcome
import com.ketoanapk.hr.network.ApiClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.serialization.encodeToString
import java.util.ArrayDeque
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import android.util.Rational

/**
 * Màn quét QR chạy trên **CameraX + ML Kit** (trước đây là Camera1 qua zxing-android-embedded).
 *
 * Vì sao đổi cả cụm camera — để bắt mã ngang tầm các app quét tốt (Zalo/Google Lens):
 *  • **Tự phóng to (auto-zoom)**: ML Kit phát hiện có mã nhưng quá nhỏ để giải mã thì đề xuất mức
 *    zoom, ta áp thẳng vào `cameraControl`. Đây là thứ khiến mã ở xa "bắt cái là dính" thay vì bắt
 *    người dùng bước tới gần.
 *  • **`STRATEGY_KEEP_ONLY_LATEST`**: luôn phân tích khung MỚI NHẤT và vứt khung tồn đọng, nên không
 *    bao giờ giải mã một khung đã cũ/nhoè do tay rung.
 *  • **Lấy nét/phơi sáng của CameraX** tốt hơn hẳn Camera1 (API Android đã khai tử), lại **chạm để
 *    lấy nét** được vào đúng chỗ có mã.
 *
 * ML Kit đọc tốt mã NGHIÊNG/méo phối cảnh nên không cần ZXing nữa; nó cũng trả về 4 GÓC THẬT của mã
 * để khung vàng ôm sát kể cả khi mã méo. Mã QR ĐẢO MÀU (nền tối chữ sáng) là thứ duy nhất mất đi so
 * với bản ZXing cũ — đổi lại tốc độ và độ nhạy cao hơn nhiều ở mọi trường hợp thực tế.
 *
 * Màn này phục vụ CẢ HAI chỗ quét trong app (xem [QrCaptureMode]) nên chỉ còn một trải nghiệm quét
 * duy nhất; trước đây chấm công dùng màn quét Camera1 riêng của zxing với chất lượng khác hẳn.
 */
class QrLoginCaptureActivity : ComponentActivity() {
    private lateinit var previewView: PreviewView
    private lateinit var overlay: QrOverlayView
    private lateinit var statusView: TextView
    private lateinit var resultSheet: View
    private lateinit var resultTitle: TextView
    private lateinit var resultBody: TextView
    private lateinit var copyButton: Button
    private lateinit var closeButton: Button
    private lateinit var torchButton: Button

    private val workScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val analysisExecutor: ExecutorService = Executors.newSingleThreadExecutor()
    private val repository by lazy(LazyThreadSafetyMode.NONE) { HrRepository.foreground(applicationContext) }
    private val continuousScanGate = QrContinuousScanGate()
    private val overlaySelection = QrOverlaySelection()
    private val pendingScans = ArrayDeque<PendingScan>(MAX_PENDING_SCANS)

    private var barcodeScanner: BarcodeScanner? = null
    private var cameraControl: androidx.camera.core.CameraControl? = null
    private var resolvingQr = false
    private var localRead: QrLocalRead? = null
    private var torchOn = false
    private var hasFlash = false
    private val captureMode by lazy(LazyThreadSafetyMode.NONE) {
        QrCaptureMode.fromName(intent?.getStringExtra(EXTRA_CAPTURE_MODE))
    }

    private val cameraPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) startCamera() else {
            Toast.makeText(this, "Cần quyền camera để quét mã QR.", Toast.LENGTH_LONG).show()
            finish()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.qr_login_capture)

        previewView = findViewById(R.id.qr_preview)
        overlay = findViewById(R.id.qr_overlay)
        statusView = findViewById(R.id.qr_status_view)
        resultSheet = findViewById(R.id.qr_result_sheet)
        resultTitle = findViewById(R.id.qr_result_title)
        resultBody = findViewById(R.id.qr_result_body)
        copyButton = findViewById(R.id.qr_copy_button)
        closeButton = findViewById(R.id.qr_close_button)
        torchButton = findViewById(R.id.qr_torch_button)

        copyButton.setOnClickListener { copyDisplayedContent() }
        closeButton.setOnClickListener { finish() }

        intent?.getStringExtra(EXTRA_PROMPT)?.trim()?.takeIf { it.isNotEmpty() }?.let { statusView.text = it }

        hasFlash = packageManager.hasSystemFeature(PackageManager.FEATURE_CAMERA_FLASH)
        torchButton.visibility = if (hasFlash) View.VISIBLE else View.GONE
        torchButton.setOnClickListener { setTorch(!torchOn) }

        // Chạm vào preview = lấy nét + đo sáng ĐÚNG chỗ người dùng chỉ (giống app quét tốt). Rất hữu
        // ích khi có nhiều vật ở các khoảng cách khác nhau hoặc nền quá sáng làm mã bị tối.
        previewView.setOnTouchListener { view, event ->
            if (event.action == MotionEvent.ACTION_UP) {
                focusAt(event.x, event.y)
                view.performClick()
            }
            true
        }

        if (ContextCompat.checkSelfPermission(this, android.Manifest.permission.CAMERA)
            == PackageManager.PERMISSION_GRANTED
        ) {
            startCamera()
        } else {
            cameraPermission.launch(android.Manifest.permission.CAMERA)
        }
    }

    // ── Camera ───────────────────────────────────────────────────────────────────

    private fun startCamera() {
        // ViewPort cần kích thước thật của PreviewView; onCreate thì view chưa đo xong nên chờ một nhịp.
        if (previewView.width <= 0 || previewView.height <= 0) {
            previewView.post { if (!isFinishing && !isDestroyed) startCamera() }
            return
        }
        val future = ProcessCameraProvider.getInstance(this)
        future.addListener({
            val provider = runCatching { future.get() }.getOrNull() ?: return@addListener
            val resolution = ResolutionSelector.Builder()
                .setAspectRatioStrategy(AspectRatioStrategy.RATIO_16_9_FALLBACK_AUTO_STRATEGY)
                .build()

            val preview = Preview.Builder()
                .setResolutionSelector(resolution)
                .build()
                .also { it.setSurfaceProvider(previewView.surfaceProvider) }

            val analysis = ImageAnalysis.Builder()
                .setResolutionSelector(resolution)
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()

            // ViewPort = hợp đồng "hai luồng nhìn cùng một vùng". CameraX tự gắn cropRect tương ứng vào
            // từng khung phân tích, nên không phải đoán xem Preview và ImageAnalysis có cùng tỉ lệ hay
            // không (chúng được cấp độ phân giải ĐỘC LẬP và hoàn toàn có thể lệch tỉ lệ).
            val viewPort = ViewPort.Builder(
                Rational(previewView.width, previewView.height),
                preview.targetRotation,
            ).setScaleType(ViewPort.FILL_CENTER).build()

            val useCases = UseCaseGroup.Builder()
                .setViewPort(viewPort)
                .addUseCase(preview)
                .addUseCase(analysis)
                .build()

            val camera = runCatching {
                provider.unbindAll()
                provider.bindToLifecycle(this, CameraSelector.DEFAULT_BACK_CAMERA, useCases)
            }.getOrNull() ?: run {
                Toast.makeText(this, "Không mở được camera trên thiết bị này.", Toast.LENGTH_LONG).show()
                finish()
                return@addListener
            }
            cameraControl = camera.cameraControl

            // Bộ quét phải dựng SAU khi bind vì mức zoom tối đa chỉ biết được từ camera đã mở.
            val maxZoom = camera.cameraInfo.zoomState.value?.maxZoomRatio ?: 1f
            barcodeScanner = BarcodeScanning.getClient(buildScannerOptions(maxZoom))
            analysis.setAnalyzer(analysisExecutor, ::analyze)
        }, ContextCompat.getMainExecutor(this))
    }

    /**
     * Bật auto-zoom của ML Kit: khi nó thấy có mã nhưng quá nhỏ để giải mã, nó gọi lại kèm mức zoom
     * nên áp dụng. Ta áp thẳng vào camera rồi báo đã áp dụng để ML Kit tính tiếp cho khung sau.
     * Máy không zoom được (maxZoom = 1) thì bỏ hẳn tuỳ chọn này.
     */
    private fun buildScannerOptions(maxZoomRatio: Float): BarcodeScannerOptions {
        val builder = BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE)
        if (maxZoomRatio > 1f) {
            builder.setZoomSuggestionOptions(
                ZoomSuggestionOptions.Builder { ratio ->
                    val control = cameraControl ?: return@Builder false
                    runCatching { control.setZoomRatio(ratio) }.isSuccess
                }.setMaxSupportedZoomRatio(maxZoomRatio).build(),
            )
        }
        return builder.build()
    }

    /** Lấy nét + đo sáng tại điểm người dùng chạm trên preview. */
    private fun focusAt(x: Float, y: Float) {
        val control = cameraControl ?: return
        val point = previewView.meteringPointFactory.createPoint(x, y)
        runCatching {
            control.startFocusAndMetering(
                FocusMeteringAction.Builder(point, FocusMeteringAction.FLAG_AF or FocusMeteringAction.FLAG_AE)
                    .build(),
            )
        }
    }

    private fun setTorch(on: Boolean) {
        val control = cameraControl ?: return
        runCatching { control.enableTorch(on) }.onSuccess {
            torchOn = on
            torchButton.text = if (on) "Tắt đèn" else "Bật đèn"
        }
    }

    // ── Phân tích khung hình ─────────────────────────────────────────────────────

    @androidx.camera.core.ExperimentalGetImage
    private fun analyze(image: androidx.camera.core.ImageProxy) {
        val scanner = barcodeScanner
        val media = image.image
        if (scanner == null || media == null) {
            image.close()
            return
        }
        // Bọc toàn bộ: một lỗi bất kỳ không được làm chết app, và ImageProxy phải luôn được đóng
        // đúng một lần — quên đóng là CameraX ngừng đẩy khung mới và màn quét "đứng hình".
        try {
            val rotation = image.imageInfo.rotationDegrees
            // cropRect (vùng ViewPort đang hiển thị) nằm trong ảnh CHƯA xoay, còn toạ độ ML Kit nằm
            // trong ảnh ĐÃ xoay — quy về cùng hệ ngay tại đây rồi mới đưa xuống lớp vẽ.
            val source = image.cropRect
            val crop = QrFrameMapper.rotateCrop(
                QrCropRect(source.left, source.top, source.right, source.bottom),
                imageWidth = image.width,
                imageHeight = image.height,
                rotationDegrees = rotation,
            )
            scanner.process(InputImage.fromMediaImage(media, rotation))
                .addOnSuccessListener(ContextCompat.getMainExecutor(this)) { barcodes ->
                    runCatching { onBarcodes(barcodes, crop) }
                }
                .addOnCompleteListener(ContextCompat.getMainExecutor(this)) { runCatching { image.close() } }
        } catch (_: Throwable) {
            runCatching { image.close() }
        }
    }

    private fun onBarcodes(barcodes: List<Barcode>, crop: QrCropRect) {
        if (isFinishing || isDestroyed) return
        val candidates = barcodes.mapNotNull { candidate ->
            val value = candidate.rawValue?.trim()?.takeIf(String::isNotEmpty) ?: return@mapNotNull null
            val corners = candidate.cornerPoints?.map { TrackPoint(it.x.toFloat(), it.y.toFloat()) }
            // Bỏ mã nằm ngoài vùng đang hiển thị: chỉ quét đúng thứ người dùng nhìn thấy và chĩa vào.
            if (corners != null && !QrFrameMapper.isVisible(corners, crop)) return@mapNotNull null
            val area = candidate.boundingBox?.let { it.width().toLong() * it.height().toLong() } ?: 0L
            Candidate(value, corners, area)
        }
        // Nhiều mã cùng khung: ưu tiên mã LỚN NHẤT để một nhãn nhỏ phía sau không cướp lượt quét.
        // Không thấy mã nào: GIỮ NGUYÊN khung vàng của mã đang chọn (nó chỉ rõ nội dung đang hiển thị
        // thuộc mã nào) — không nhấp nháy theo từng khung trượt.
        val best = candidates.maxByOrNull { it.area } ?: return

        val quad = best.corners?.let {
            QrFrameMapper.map(it, crop, previewView.width.toFloat(), previewView.height.toFloat())
        }
        val now = android.os.SystemClock.uptimeMillis()

        if (continuousScanGate.shouldAccept(best.value, now)) {
            enqueueScan(PendingScan(best.value, quad, now))
        } else if (overlaySelection.owns(best.value) && quad != null) {
            // Chỉ mã ĐANG được xử lý/hiển thị mới được kéo khung. Mã khác lọt vào khung hình tuyệt đối
            // không được làm khung vàng nhảy sang chỗ khác.
            overlay.submitQuad(quad, snapToQr = false)
        }
    }

    private data class Candidate(val value: String, val corners: List<TrackPoint>?, val area: Long)

    // ── Luồng xử lý kết quả (giữ nguyên như bản cũ) ──────────────────────────────

    private fun enqueueScan(scan: PendingScan) {
        // Chế độ thô: người gọi tự xử lý nội dung (chấm công), không hỏi máy chủ, quét xong đóng luôn.
        if (captureMode == QrCaptureMode.Raw) {
            setResult(Activity.RESULT_OK, Intent().putExtra(EXTRA_SCANNED_VALUE, scan.value))
            finish()
            return
        }
        if (resolvingQr) {
            // Giữ vài mã kế tiếp để không bắt người dùng đóng/mở camera giữa chừng, nhưng CHỈ vài mã:
            // lướt camera qua một tờ giấy đầy mã mà xếp hàng dài thì app sẽ bắn hàng loạt request rồi
            // nhấp nháy qua từng kết quả, tệ nhất là tự đóng màn quét vì một mã người dùng không định quét.
            if (pendingScans.size < MAX_PENDING_SCANS) pendingScans.addLast(scan)
            return
        }
        resolveScan(scan)
    }

    /** Bỏ các mã đã nằm chờ quá lâu — chúng không còn là thứ người dùng đang chĩa camera vào. */
    private fun nextFreshScan(): PendingScan? {
        val now = android.os.SystemClock.uptimeMillis()
        while (true) {
            val next = pendingScans.pollFirst() ?: return null
            if (now - next.atMs <= MAX_PENDING_AGE_MS) return next
        }
    }

    private fun resolveScan(scan: PendingScan) {
        resolvingQr = true
        val switchedQr = overlaySelection.activate(scan.value)
        scan.quad?.let { overlay.submitQuad(it, snapToQr = switchedQr) }
        // Không để nội dung QR trước nằm dưới khung QR mới trong lúc chờ server.
        localRead = null
        setResultSheetVisible(false)
        statusView.text = "Đang kiểm tra mã QR…"
        statusView.visibility = View.VISIBLE

        workScope.launch {
            val outcome = runCatching { repository.resolveQr(scan.value) }
                .getOrElse { QrResolveOutcome.Rejected("Không thể xử lý mã QR lúc này.") }
            if (isFinishing || isDestroyed) return@launch
            when (outcome) {
                is QrResolveOutcome.Handled -> {
                    deliverServerResult(scan.value, outcome.envelope)
                    return@launch
                }
                QrResolveOutcome.Unhandled -> showLocalResult(scan.value, offline = false)
                QrResolveOutcome.Offline -> showLocalResult(scan.value, offline = true)
                is QrResolveOutcome.Rejected -> showError(outcome.message)
            }

            resolvingQr = false
            nextFreshScan()?.let(::resolveScan)
        }
    }

    private fun deliverServerResult(value: String, envelope: QrActionEnvelope) {
        setResult(
            Activity.RESULT_OK,
            Intent()
                .putExtra(EXTRA_SCANNED_VALUE, value)
                .putExtra(EXTRA_RESOLVED_ENVELOPE, ApiClient.json.encodeToString(envelope)),
        )
        finish()
    }

    private fun showLocalResult(value: String, offline: Boolean) {
        val read = QrContentReader.read(value)
        localRead = read
        resultTitle.text = read.title
        resultBody.text = if (offline) {
            read.body + "\n\nChưa kết nối được máy chủ; ứng dụng mới chỉ đọc nội dung mã này."
        } else {
            read.body
        }
        copyButton.text = read.copyLabel
        copyButton.visibility = if (read.copyText.isNotEmpty()) View.VISIBLE else View.GONE
        statusView.text = "Đưa mã QR tiếp theo vào khung"
        statusView.visibility = View.VISIBLE
        setResultSheetVisible(true)
    }

    private fun showError(message: String) {
        localRead = null
        resultTitle.text = "Không thể xử lý mã QR"
        resultBody.text = message
        copyButton.visibility = View.GONE
        statusView.text = "Đưa mã QR khác vào khung"
        statusView.visibility = View.VISIBLE
        setResultSheetVisible(true)
    }

    /** Bảng kết quả nằm đè lên góc dưới nên nút đèn phải nhường chỗ khi bảng hiện. */
    private fun setResultSheetVisible(visible: Boolean) {
        resultSheet.visibility = if (visible) View.VISIBLE else View.GONE
        torchButton.visibility = if (!visible && hasFlash) View.VISIBLE else View.GONE
    }

    private fun copyDisplayedContent() {
        val read = localRead ?: return
        if (read.copyText.isEmpty()) return
        val clipboard = getSystemService(ClipboardManager::class.java) ?: return
        val clip = ClipData.newPlainText("Nội dung mã QR", read.copyText)
        if (read.sensitive && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            clip.description.extras = PersistableBundle().apply {
                putBoolean(ClipDescription.EXTRA_IS_SENSITIVE, true)
            }
        }
        clipboard.setPrimaryClip(clip)
        // Android 13+ tự hiện clipboard overlay; chỉ báo Toast trên máy cũ để không hiện trùng hai lần.
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
            Toast.makeText(this, "Đã sao chép", Toast.LENGTH_SHORT).show()
        }
    }

    override fun onDestroy() {
        workScope.cancel()
        runCatching { barcodeScanner?.close() }
        runCatching { analysisExecutor.shutdown() }
        super.onDestroy()
    }

    private data class PendingScan(val value: String, val quad: TrackQuad?, val atMs: Long)

    companion object {
        const val EXTRA_RESOLVED_ENVELOPE = "com.ketoanapk.hr.extra.QR_RESOLVED_ENVELOPE"
        const val EXTRA_SCANNED_VALUE = "com.ketoanapk.hr.extra.QR_SCANNED_VALUE"
        const val EXTRA_CAPTURE_MODE = "com.ketoanapk.hr.extra.QR_CAPTURE_MODE"
        const val EXTRA_PROMPT = "com.ketoanapk.hr.extra.QR_PROMPT"
        private const val MAX_PENDING_SCANS = 3
        private const val MAX_PENDING_AGE_MS = 2_000L
    }
}

/**
 * Kết quả màn quét. [resolvedEnvelopeJson] có giá trị khi máy chủ đã nhận diện được nghiệp vụ ngay
 * trên màn camera (QR đăng nhập web, ký nhận phiếu chi…) — khi đó không cần hỏi lại máy chủ lần nữa.
 */
data class QrCaptureResult(val value: String?, val resolvedEnvelopeJson: String?)

enum class QrCaptureMode {
    /** Mặc định: hỏi `/api/qr/resolve` ngay trên camera, nghiệp vụ do máy chủ quyết định. */
    Resolve,

    /** Trả thẳng nội dung mã cho người gọi tự xử lý (chấm công đã có endpoint riêng của nó). */
    Raw;

    companion object {
        fun fromName(value: String?): QrCaptureMode =
            entries.firstOrNull { it.name == value } ?: Resolve
    }
}

data class QrCaptureRequest(val mode: QrCaptureMode = QrCaptureMode.Resolve, val prompt: String? = null)

/**
 * Contract riêng cho màn quét. Trước đây chấm công dùng `ScanContract` của zxing (Camera1) còn màn
 * đăng nhập dùng màn này, thành ra hai trải nghiệm quét khác hẳn nhau trong cùng một app. Giờ cả hai
 * đi qua đây, khác nhau đúng một tham số [QrCaptureMode].
 */
class QrCaptureContract :
    androidx.activity.result.contract.ActivityResultContract<QrCaptureRequest, QrCaptureResult>() {
    override fun createIntent(context: android.content.Context, input: QrCaptureRequest): Intent =
        Intent(context, QrLoginCaptureActivity::class.java)
            .putExtra(QrLoginCaptureActivity.EXTRA_CAPTURE_MODE, input.mode.name)
            .putExtra(QrLoginCaptureActivity.EXTRA_PROMPT, input.prompt)

    override fun parseResult(resultCode: Int, intent: Intent?): QrCaptureResult {
        if (resultCode != Activity.RESULT_OK || intent == null) return QrCaptureResult(null, null)
        return QrCaptureResult(
            value = intent.getStringExtra(QrLoginCaptureActivity.EXTRA_SCANNED_VALUE),
            resolvedEnvelopeJson = intent.getStringExtra(QrLoginCaptureActivity.EXTRA_RESOLVED_ENVELOPE),
        )
    }
}
