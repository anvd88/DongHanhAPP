package com.ketoanapk.hr.ui

import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.PersistableBundle
import android.provider.MediaStore
import android.util.Rational
import android.util.Size
import android.view.MotionEvent
import android.view.View
import android.widget.Button
import android.widget.ImageButton
import android.widget.TextView
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.FocusMeteringAction
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.core.UseCaseGroup
import androidx.camera.core.ViewPort
import androidx.camera.core.resolutionselector.AspectRatioStrategy
import androidx.camera.core.resolutionselector.ResolutionSelector
import androidx.camera.core.resolutionselector.ResolutionStrategy
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.core.content.ContextCompat
import androidx.core.net.toUri
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updatePadding
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
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

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
    private lateinit var openButton: Button
    private lateinit var copyButton: Button
    private lateinit var closeButton: Button
    private lateinit var torchButton: ImageButton
    private lateinit var exitButton: ImageButton
    private lateinit var galleryButton: ImageButton
    private lateinit var bottomActions: View

    private val workScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val analysisExecutor: ExecutorService = Executors.newSingleThreadExecutor()
    private val repository by lazy(LazyThreadSafetyMode.NONE) { HrRepository.foreground(applicationContext) }
    private val continuousScanGate = QrContinuousScanGate()
    private val overlaySelection = QrOverlaySelection()

    private var barcodeScanner: BarcodeScanner? = null
    private var cameraProvider: ProcessCameraProvider? = null
    private var previewUseCase: Preview? = null
    private var analysisUseCase: ImageAnalysis? = null
    private var cameraControl: androidx.camera.core.CameraControl? = null
    private var cameraStartRequested = false
    private var resolvingQr = false
    private var readingImage = false
    private var localRead: QrLocalRead? = null
    private var torchOn = false
    private var hasFlash = false
    private var scanPrompt = ""
    private val captureMode by lazy(LazyThreadSafetyMode.NONE) {
        QrCaptureMode.fromName(intent?.getStringExtra(EXTRA_CAPTURE_MODE))
    }

    private val cameraPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) {
            startCamera()
        } else {
            showCameraUnavailable("Camera chưa được cấp quyền.")
            Toast.makeText(this, "Bạn vẫn có thể chọn một ảnh có mã QR.", Toast.LENGTH_LONG).show()
        }
    }

    /** Photo Picker shows an image/album grid and grants access to only the selected photo. */
    private val visualImagePicker = registerForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        uri?.let(::scanQrFromImage)
    }

    /**
     * Android 10 devices may not have the modular Photo Picker. ACTION_PICK opens the installed
     * image library instead of falling back to the generic Android file browser.
     */
    private val legacyImageLibrary = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        if (result.resultCode == Activity.RESULT_OK) result.data?.data?.let(::scanQrFromImage)
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
        openButton = findViewById(R.id.qr_open_button)
        copyButton = findViewById(R.id.qr_copy_button)
        closeButton = findViewById(R.id.qr_close_button)
        torchButton = findViewById(R.id.qr_torch_button)
        exitButton = findViewById(R.id.qr_exit_button)
        galleryButton = findViewById(R.id.qr_gallery_button)
        bottomActions = findViewById(R.id.qr_bottom_actions)

        openButton.setOnClickListener { openDisplayedLink() }
        copyButton.setOnClickListener { copyDisplayedContent() }
        closeButton.setOnClickListener { prepareForNextScan() }
        exitButton.setOnClickListener { finish() }
        galleryButton.setOnClickListener {
            if (resolvingQr) {
                Toast.makeText(this, "Đang xử lý mã QR hiện tại.", Toast.LENGTH_SHORT).show()
            } else {
                openImageLibrary()
            }
        }

        scanPrompt = intent?.getStringExtra(EXTRA_PROMPT)?.trim()?.takeIf { it.isNotEmpty() }
            ?: getString(R.string.qr_default_prompt)
        statusView.text = scanPrompt

        // Chỉ hiện đèn sau khi camera sau đã bind và chính camera đó xác nhận có flash unit.
        torchButton.visibility = View.GONE
        torchButton.setOnClickListener { setTorch(!torchOn) }
        applySystemBarInsets()

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

    @androidx.annotation.OptIn(androidx.camera.core.ExperimentalGetImage::class)
    private fun startCamera() {
        // ViewPort cần kích thước thật của PreviewView; onCreate thì view chưa đo xong nên chờ một nhịp.
        if (previewView.width <= 0 || previewView.height <= 0) {
            previewView.post { if (!isFinishing && !isDestroyed) startCamera() }
            return
        }
        if (cameraStartRequested) return
        cameraStartRequested = true
        val future = ProcessCameraProvider.getInstance(this)
        future.addListener({
            val provider = runCatching { future.get() }.getOrElse {
                cameraStartRequested = false
                showCameraUnavailable("Không khởi tạo được camera.")
                return@addListener
            }
            val previewResolution = ResolutionSelector.Builder()
                .setAspectRatioStrategy(AspectRatioStrategy.RATIO_16_9_FALLBACK_AUTO_STRATEGY)
                .build()

            // Phân tích ở độ phân giải CAO hơn hẳn preview để đọc được mã nhìn CHẾCH. Mã QR dán một độ
            // cao cố định, người cao/thấp khác nhau sẽ nhìn mã chếch lên/xuống → mã hiện ra hình thang,
            // cạnh xa bị dồn pixel; ML Kit chỉ giải mã được khi cạnh đó còn đủ điểm ảnh. Không đặt gì thì
            // CameraX chọn mức khiêm tốn (~cỡ preview) nên góc nghiêng "hết pixel" sớm — chốt cứng 1080p,
            // thiếu thì lấy mức gần nhất cao hơn rồi mới xuống thấp hơn. Vẫn KEEP_ONLY_LATEST nên máy yếu
            // chỉ giảm SỐ khung phân tích chứ không dồn ứ khung cũ; phiên quét ngắn nên không lo nóng máy.
            val analysisResolution = ResolutionSelector.Builder()
                .setAspectRatioStrategy(AspectRatioStrategy.RATIO_16_9_FALLBACK_AUTO_STRATEGY)
                .setResolutionStrategy(
                    ResolutionStrategy(
                        Size(1920, 1080),
                        ResolutionStrategy.FALLBACK_RULE_CLOSEST_HIGHER_THEN_LOWER,
                    ),
                )
                .build()

            val preview = Preview.Builder()
                .setResolutionSelector(previewResolution)
                .build()
                .also { it.setSurfaceProvider(previewView.surfaceProvider) }

            val analysis = ImageAnalysis.Builder()
                .setResolutionSelector(analysisResolution)
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
                provider.bindToLifecycle(this, CameraSelector.DEFAULT_BACK_CAMERA, useCases)
            }.getOrNull() ?: run {
                cameraStartRequested = false
                showCameraUnavailable("Không mở được camera sau trên thiết bị này.")
                return@addListener
            }
            cameraProvider = provider
            previewUseCase = preview
            analysisUseCase = analysis
            cameraControl = camera.cameraControl
            hasFlash = camera.cameraInfo.hasFlashUnit()
            torchButton.visibility = if (hasFlash && resultSheet.visibility != View.VISIBLE) View.VISIBLE else View.GONE

            // Bộ quét phải dựng SAU khi bind vì mức zoom tối đa chỉ biết được từ camera đã mở.
            val maxZoom = camera.cameraInfo.zoomState.value?.maxZoomRatio ?: 1f
            runCatching { barcodeScanner?.close() }
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
        if (!hasFlash || !torchButton.isEnabled) return
        val request = runCatching { control.enableTorch(on) }.getOrElse {
            Toast.makeText(this, "Không điều khiển được đèn pin.", Toast.LENGTH_SHORT).show()
            return
        }
        torchButton.isEnabled = false
        request.addListener({
            if (isFinishing || isDestroyed) return@addListener
            torchButton.isEnabled = true
            runCatching { request.get() }.onSuccess {
                torchOn = on
                torchButton.isSelected = on
                val description = getString(if (on) R.string.qr_torch_off else R.string.qr_torch_on)
                torchButton.contentDescription = description
                torchButton.tooltipText = description
            }.onFailure {
                Toast.makeText(this, "Không điều khiển được đèn pin.", Toast.LENGTH_SHORT).show()
            }
        }, ContextCompat.getMainExecutor(this))
    }

    private fun showCameraUnavailable(message: String) {
        hasFlash = false
        torchButton.visibility = View.GONE
        statusView.text = getString(R.string.qr_camera_fallback, message)
    }

    /** Keep camera edge-to-edge while moving controls away from cutouts and gesture navigation. */
    private fun applySystemBarInsets() {
        val root = findViewById<View>(R.id.qr_capture_root)
        val topContent = findViewById<View>(R.id.qr_top_content)
        val baseTopPadding = topContent.paddingTop
        val baseSheetBottomPadding = resultSheet.paddingBottom
        val baseActionsBottomPadding = bottomActions.paddingBottom

        ViewCompat.setOnApplyWindowInsetsListener(root) { _, insets ->
            val safe = insets.getInsets(
                WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout(),
            )
            topContent.updatePadding(top = baseTopPadding + safe.top)
            resultSheet.updatePadding(bottom = baseSheetBottomPadding + safe.bottom)
            bottomActions.updatePadding(bottom = baseActionsBottomPadding + safe.bottom)
            insets
        }
        ViewCompat.requestApplyInsets(root)
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
        if (isFinishing || isDestroyed || readingImage) return
        val candidates = barcodes.mapNotNull { candidate ->
            val value = candidate.rawValue?.trim()?.takeIf(String::isNotEmpty) ?: return@mapNotNull null
            val corners = candidate.cornerPoints?.map { TrackPoint(it.x.toFloat(), it.y.toFloat()) }
            val box = candidate.boundingBox
            // ML Kit phân tích toàn ảnh cảm biến, còn PreviewView chỉ hiển thị crop của ViewPort. Phải
            // chứng minh tâm mã nằm trong vùng người dùng thật sự nhìn thấy; nếu thiếu cornerPoints thì
            // dùng boundingBox, không cho một mã vô hình kích hoạt chấm công/phiếu chi.
            val visible = when {
                corners != null && corners.size >= 4 -> QrFrameMapper.isVisible(corners, crop)
                box != null -> QrFrameMapper.isPointVisible(
                    TrackPoint(box.exactCenterX(), box.exactCenterY()),
                    crop,
                )
                else -> false
            }
            if (!visible) return@mapNotNull null
            val area = box?.let { it.width().toLong() * it.height().toLong() } ?: 0L
            Candidate(value, corners, area)
        }
        // Giữ mã đang hiển thị nếu nó vẫn còn trong camera. Chỉ khi mã đó rời khung mới chuyển sang mã
        // lớn nhất còn lại; nhờ vậy một tờ giấy có nhiều QR không làm khung vàng và kết quả nhảy liên tục.
        val best = candidates.firstOrNull { overlaySelection.owns(it.value) }
            ?: candidates.maxByOrNull { it.area }
            ?: return

        val quad = best.corners?.let {
            QrFrameMapper.map(it, crop, previewView.width.toFloat(), previewView.height.toFloat())
        }
        val now = android.os.SystemClock.uptimeMillis()

        if (overlaySelection.owns(best.value) && quad != null) {
            overlay.submitQuad(quad, snapToQr = false)
        }
        // Không xếp hàng mã nhìn thấy thoáng qua trong lúc request đang chạy. Sau khi request xong,
        // analyzer sẽ chỉ nhận mã nào vẫn còn thật sự trước camera và tránh tự chạy kết quả FIFO cũ.
        if (resolvingQr) return
        if (continuousScanGate.shouldAccept(best.value, now)) {
            enqueueScan(PendingScan(best.value, quad))
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
        if (resolvingQr) return
        resolveScan(scan)
    }

    private fun resolveScan(scan: PendingScan) {
        resolvingQr = true
        val switchedQr = overlaySelection.activate(scan.value)
        scan.quad?.let { overlay.submitQuad(it, snapToQr = switchedQr) }
        // Không để nội dung QR trước nằm dưới khung QR mới trong lúc chờ server.
        localRead = null
        setResultSheetVisible(false)

        // Các chuẩn công khai được đọc tại chỗ: nhanh hơn và không đưa mật khẩu Wi-Fi/danh thiếp/URL
        // lên API. Chuỗi nội bộ hoặc không rõ định dạng vẫn dùng bộ phân giải nghiệp vụ trên server.
        if (QrContentReader.isStandardFormat(scan.value)) {
            showLocalResult(scan.value, offline = false)
            resolvingQr = false
            return
        }

        statusView.text = getString(R.string.qr_status_checking)
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
        openButton.visibility = if (read.openUrl != null) View.VISIBLE else View.GONE
        copyButton.text = read.copyLabel
        copyButton.visibility = if (read.copyText.isNotEmpty()) View.VISIBLE else View.GONE
        copyButton.setBackgroundResource(
            if (read.openUrl == null) R.drawable.qr_result_copy_button else R.drawable.qr_result_close_button,
        )
        statusView.text = getString(R.string.qr_status_next)
        statusView.visibility = View.VISIBLE
        setResultSheetVisible(true)
    }

    private fun showError(message: String) {
        localRead = null
        resultTitle.text = getString(R.string.qr_error_title)
        resultBody.text = message
        openButton.visibility = View.GONE
        copyButton.visibility = View.GONE
        statusView.text = getString(R.string.qr_status_other)
        statusView.visibility = View.VISIBLE
        setResultSheetVisible(true)
    }

    /** Bảng kết quả nằm đè lên góc dưới nên cụm thao tác camera phải nhường chỗ khi bảng hiện. */
    private fun setResultSheetVisible(visible: Boolean) {
        resultSheet.visibility = if (visible) View.VISIBLE else View.GONE
        bottomActions.visibility = if (visible) View.GONE else View.VISIBLE
        torchButton.visibility = if (!visible && hasFlash) View.VISIBLE else View.GONE
    }

    /** Ẩn kết quả nhưng giữ camera mở, tương ứng hành động “Quét tiếp” thay vì thoát cả scanner. */
    private fun prepareForNextScan() {
        localRead = null
        overlaySelection.clear()
        overlay.clear()
        setResultSheetVisible(false)
        statusView.text = scanPrompt
        statusView.visibility = View.VISIBLE
        runCatching { cameraControl?.setZoomRatio(1f) }
    }

    private fun openDisplayedLink() {
        val safeUrl = QrExternalUrlPolicy.normalize(localRead?.openUrl)
        if (safeUrl == null) {
            Toast.makeText(this, "Liên kết này không đủ điều kiện an toàn để mở.", Toast.LENGTH_LONG).show()
            return
        }
        try {
            startActivity(
                Intent(Intent.ACTION_VIEW, safeUrl.toUri()).addCategory(Intent.CATEGORY_BROWSABLE),
            )
        } catch (_: ActivityNotFoundException) {
            Toast.makeText(this, "Điện thoại chưa có ứng dụng phù hợp để mở liên kết.", Toast.LENGTH_LONG).show()
        } catch (_: SecurityException) {
            Toast.makeText(this, "Điện thoại đã chặn liên kết này.", Toast.LENGTH_LONG).show()
        }
    }

    /**
     * Prefer Android's privacy-preserving photo grid. If a device has no Photo Picker module,
     * explicitly open its image library so the user never lands in the generic DocumentsUI flow.
     */
    private fun openImageLibrary() {
        runCatching {
            if (ActivityResultContracts.PickVisualMedia.isPhotoPickerAvailable(this)) {
                visualImagePicker.launch(
                    PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly),
                )
            } else {
                legacyImageLibrary.launch(
                    Intent(Intent.ACTION_PICK, MediaStore.Images.Media.EXTERNAL_CONTENT_URI).apply {
                        type = "image/*"
                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                    },
                )
            }
        }.onFailure {
            Toast.makeText(this, "Không mở được thư viện ảnh.", Toast.LENGTH_SHORT).show()
        }
    }

    /** Đọc QR từ ảnh do người dùng chủ động chọn; không cần quyền truy cập toàn bộ thư viện ảnh. */
    private fun scanQrFromImage(uri: Uri) {
        if (readingImage || resolvingQr || isFinishing || isDestroyed) return
        val image = runCatching { InputImage.fromFilePath(this, uri) }.getOrElse {
            showError("Không đọc được ảnh đã chọn.")
            return
        }

        readingImage = true
        galleryButton.isEnabled = false
        localRead = null
        overlaySelection.clear()
        overlay.clear()
        setResultSheetVisible(false)
        statusView.text = getString(R.string.qr_status_reading_image)

        val stillScanner = runCatching {
            BarcodeScanning.getClient(
                BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build(),
            )
        }.getOrElse {
            readingImage = false
            galleryButton.isEnabled = true
            showError("Không khởi tạo được bộ đọc mã QR cho ảnh.")
            return
        }
        stillScanner.process(image)
            .addOnSuccessListener(ContextCompat.getMainExecutor(this)) { barcodes ->
                if (isFinishing || isDestroyed) return@addOnSuccessListener
                val selected = barcodes
                    .mapNotNull { barcode ->
                        val value = barcode.rawValue?.trim()?.takeIf(String::isNotEmpty)
                            ?: return@mapNotNull null
                        val area = barcode.boundingBox?.let { it.width().toLong() * it.height().toLong() } ?: 0L
                        value to area
                    }
                    .maxByOrNull { it.second }
                if (selected == null) {
                    showError("Không tìm thấy mã QR đọc được trong ảnh đã chọn.")
                } else {
                    enqueueScan(
                        PendingScan(
                            value = selected.first,
                            quad = null,
                        ),
                    )
                }
            }
            .addOnFailureListener(ContextCompat.getMainExecutor(this)) {
                if (!isFinishing && !isDestroyed) showError("Không đọc được mã QR trong ảnh đã chọn.")
            }
            .addOnCompleteListener(ContextCompat.getMainExecutor(this)) {
                readingImage = false
                if (!isDestroyed) galleryButton.isEnabled = true
                runCatching { stillScanner.close() }
            }
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
        runCatching { analysisUseCase?.clearAnalyzer() }
        val ownedUseCases = listOfNotNull(previewUseCase, analysisUseCase)
        if (ownedUseCases.isNotEmpty()) {
            runCatching { cameraProvider?.unbind(*ownedUseCases.toTypedArray()) }
        }
        runCatching { barcodeScanner?.close() }
        runCatching { analysisExecutor.shutdown() }
        super.onDestroy()
    }

    private data class PendingScan(val value: String, val quad: TrackQuad?)

    companion object {
        const val EXTRA_RESOLVED_ENVELOPE = "com.ketoanapk.hr.extra.QR_RESOLVED_ENVELOPE"
        const val EXTRA_SCANNED_VALUE = "com.ketoanapk.hr.extra.QR_SCANNED_VALUE"
        const val EXTRA_CAPTURE_MODE = "com.ketoanapk.hr.extra.QR_CAPTURE_MODE"
        const val EXTRA_PROMPT = "com.ketoanapk.hr.extra.QR_PROMPT"
    }
}

/**
 * Kết quả màn quét. [resolvedEnvelopeJson] có giá trị khi máy chủ đã nhận diện được nghiệp vụ ngay
 * trên màn camera (QR đăng nhập web, ký nhận phiếu chi…) — khi đó không cần hỏi lại máy chủ lần nữa.
 */
data class QrCaptureResult(val value: String?, val resolvedEnvelopeJson: String?)

enum class QrCaptureMode {
    /** Mặc định: chuẩn phổ thông đọc tại chỗ; chuỗi nội bộ/không rõ mới hỏi `/api/qr/resolve`. */
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
