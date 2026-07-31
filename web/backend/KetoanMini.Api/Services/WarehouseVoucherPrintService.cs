using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using KetoanMini.Api.Models;

namespace KetoanMini.Api.Services;

public sealed class WarehouseVoucherPrintService(
    IWebHostEnvironment environment,
    ILogger<WarehouseVoucherPrintService> logger)
{
    private const string TemplateFileName = "PhieuXuatKho.xlsx";
    private const string DataSheetName = "PHIEU XUAT KHO";
    private const string PrintSheetName = "IN LEN PHOI";
    private const int MaxLineCount = 14;
    private const int FirstLineRow = 16;
    private const int LastLineRow = FirstLineRow + MaxLineCount - 1;
    private const string UnusedRowsSlashName = "KetoanMini_UnusedRowsSlash";
    private const double UnusedRowsSlashEndInsetPoints = 46.5;
    private const int PreviewSlashColor = 0x4536FF; // #FF3645 theo định dạng BGR của Excel.
    private const int PrintSlashColor = 0x000000;
    private const uint PrinterAttributeWorkOffline = 0x00000400;
    private const uint PrinterBlockingStatuses =
        0x00000002 // ERROR
        | 0x00000008 // PAPER_JAM
        | 0x00000010 // PAPER_OUT
        | 0x00000080 // OFFLINE
        | 0x00001000 // NOT_AVAILABLE
        | 0x00040000 // NO_TONER
        | 0x00100000 // USER_INTERVENTION
        | 0x00400000 // DOOR_OPEN
        | 0x00800000; // SERVER_UNKNOWN
    private const uint JobStatusPaused = 0x00000001;
    private const uint JobStatusError = 0x00000002;
    private const uint JobStatusOffline = 0x00000020;
    private const uint JobStatusPaperOut = 0x00000040;
    private const uint JobStatusBlocked = 0x00000200;
    private const uint JobStatusUserIntervention = 0x00000400;
    private const uint JobBlockingStatuses =
        JobStatusPaused
        | JobStatusError
        | JobStatusOffline
        | JobStatusPaperOut
        | JobStatusBlocked
        | JobStatusUserIntervention;
    private readonly SemaphoreSlim _excelGate = new(1, 1);

    public WarehousePrintSystemStatus GetSystemStatus()
    {
        var checkedAt = DateTime.UtcNow;
        var templatePath = Path.Combine(environment.ContentRootPath, "Templates", TemplateFileName);
        var serverMessage = "Máy chủ đang hoạt động và dịch vụ in đã sẵn sàng.";
        var printServiceReady = true;

        if (!OperatingSystem.IsWindows())
        {
            printServiceReady = false;
            serverMessage = "Máy chủ đang hoạt động nhưng dịch vụ in cần chạy trên Windows.";
        }
        else if (!File.Exists(templatePath))
        {
            printServiceReady = false;
            serverMessage = "Máy chủ đang hoạt động nhưng thiếu file mẫu phiếu xuất kho.";
        }
        else if (Type.GetTypeFromProgID("Excel.Application", throwOnError: false) is null)
        {
            printServiceReady = false;
            serverMessage = "Máy chủ đang hoạt động nhưng chưa cài Microsoft Excel.";
        }

        var server = new WarehouseServerStatus(
            Online: true,
            Name: Environment.MachineName,
            PrintServiceReady: printServiceReady,
            Message: serverMessage,
            UptimeSeconds: Math.Max(0, Environment.TickCount64 / 1000));

        if (!OperatingSystem.IsWindows())
        {
            return new WarehousePrintSystemStatus(
                server,
                new WarehousePrinterStatus(
                    Available: false,
                    Ready: false,
                    Name: null,
                    Message: "Không thể kiểm tra máy in vì máy chủ không chạy Windows.",
                    QueuedJobs: 0,
                    StatusCode: 0),
                checkedAt);
        }

        string? printerName = null;
        try
        {
            printerName = GetDefaultPrinterName();
            if (IsVirtualPrinter(printerName))
            {
                return new WarehousePrintSystemStatus(
                    server,
                    new WarehousePrinterStatus(
                        Available: true,
                        Ready: false,
                        Name: printerName,
                        Message: "Máy in mặc định đang là máy in ảo. Hãy chọn máy in giấy làm mặc định.",
                        QueuedJobs: 0,
                        StatusCode: 0),
                    checkedAt);
            }

            var snapshot = ReadPrinterSnapshot(printerName);
            return new WarehousePrintSystemStatus(
                server,
                new WarehousePrinterStatus(
                    Available: true,
                    Ready: snapshot.Ready,
                    Name: printerName,
                    Message: snapshot.Message,
                    QueuedJobs: snapshot.QueuedJobs,
                    StatusCode: snapshot.StatusCode),
                checkedAt);
        }
        catch (WarehousePrintUnavailableException ex)
        {
            return new WarehousePrintSystemStatus(
                server,
                new WarehousePrinterStatus(
                    Available: printerName is not null,
                    Ready: false,
                    Name: printerName,
                    Message: ex.Message,
                    QueuedJobs: 0,
                    StatusCode: 0),
                checkedAt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Khong the doc trang thai may in mac dinh tren may chu.");
            return new WarehousePrintSystemStatus(
                server,
                new WarehousePrinterStatus(
                    Available: printerName is not null,
                    Ready: false,
                    Name: printerName,
                    Message: "Không đọc được trạng thái máy in mặc định trên máy chủ.",
                    QueuedJobs: 0,
                    StatusCode: 0),
                checkedAt);
        }
    }

    public async Task<WarehouseServerPrintResult> PrintAsync(
        DocumentDetailDto document,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new WarehousePrintUnavailableException("Máy chủ phải chạy Windows và cài Microsoft Excel để in mẫu này.");
        if (string.IsNullOrWhiteSpace(document.VoucherNo))
            throw new WarehousePrintValidationException("Vui lòng nhập số phiếu trước khi in.");
        if (document.Lines.Count > MaxLineCount)
            throw new WarehousePrintValidationException($"Mẫu Excel chỉ hỗ trợ tối đa {MaxLineCount} dòng hàng.");

        var templatePath = Path.Combine(environment.ContentRootPath, "Templates", TemplateFileName);
        if (!File.Exists(templatePath))
            throw new WarehousePrintUnavailableException("Không tìm thấy file mẫu phiếu xuất kho.");

        await _excelGate.WaitAsync(cancellationToken);
        try
        {
            return await PrintInStaThreadAsync(templatePath, document);
        }
        finally
        {
            _excelGate.Release();
        }
    }

    public async Task<byte[]> CreatePreviewPdfAsync(
        DocumentDetailDto document,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new WarehousePrintUnavailableException("Máy chủ phải chạy Windows và cài Microsoft Excel để tạo bản xem trước.");
        if (string.IsNullOrWhiteSpace(document.VoucherNo))
            throw new WarehousePrintValidationException("Vui lòng nhập số phiếu trước khi xem trước.");
        if (document.Lines.Count > MaxLineCount)
            throw new WarehousePrintValidationException($"Mẫu Excel chỉ hỗ trợ tối đa {MaxLineCount} dòng hàng.");

        var templatePath = Path.Combine(environment.ContentRootPath, "Templates", TemplateFileName);
        if (!File.Exists(templatePath))
            throw new WarehousePrintUnavailableException("Không tìm thấy file mẫu phiếu xuất kho.");

        await _excelGate.WaitAsync(cancellationToken);
        try
        {
            return await CreatePreviewInStaThreadAsync(templatePath, document);
        }
        finally
        {
            _excelGate.Release();
        }
    }

    [SupportedOSPlatform("windows")]
    private Task<WarehouseServerPrintResult> PrintInStaThreadAsync(
        string templatePath,
        DocumentDetailDto document)
    {
        var completion = new TaskCompletionSource<WarehouseServerPrintResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(PrintWithExcel(templatePath, document));
            }
            catch (WarehousePrintException ex)
            {
                completion.SetException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Khong the in phieu xuat kho {DocumentId} tren may chu bang Microsoft Excel.", document.Id);
                completion.SetException(new WarehousePrintUnavailableException(
                    "Microsoft Excel không thể gửi lệnh in. Vui lòng kiểm tra Excel và máy in trên máy chủ.", ex));
            }
        })
        {
            IsBackground = true,
            Name = "WarehouseVoucherServerPrinter",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [SupportedOSPlatform("windows")]
    private Task<byte[]> CreatePreviewInStaThreadAsync(
        string templatePath,
        DocumentDetailDto document)
    {
        var completion = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(ExportPreviewPdfWithExcel(templatePath, document));
            }
            catch (WarehousePrintException ex)
            {
                completion.SetException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Khong the tao ban xem truoc phieu xuat kho {DocumentId} bang Microsoft Excel.", document.Id);
                completion.SetException(new WarehousePrintUnavailableException(
                    "Microsoft Excel không thể tạo bản xem trước từ mẫu phiếu xuất kho.", ex));
            }
        })
        {
            IsBackground = true,
            Name = "WarehouseVoucherPreviewRenderer",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [SupportedOSPlatform("windows")]
    private static WarehouseServerPrintResult PrintWithExcel(
        string templatePath,
        DocumentDetailDto document)
    {
        var defaultPrinter = GetDefaultPrinterName();
        EnsurePrinterReady(defaultPrinter);

        var excelType = Type.GetTypeFromProgID("Excel.Application", throwOnError: false);
        if (excelType is null)
            throw new WarehousePrintUnavailableException("Máy chủ chưa cài Microsoft Excel.");

        object? excel = null;
        object? workbooks = null;
        object? workbook = null;
        object? worksheets = null;
        object? dataWorksheet = null;
        object? printWorksheet = null;
        var workbookClosed = false;
        var excelQuit = false;

        try
        {
            excel = Activator.CreateInstance(excelType)
                ?? throw new WarehousePrintUnavailableException("Không khởi động được Microsoft Excel.");
            dynamic app = excel;
            app.Visible = false;
            app.DisplayAlerts = false;
            app.ScreenUpdating = false;
            app.EnableEvents = false;
            app.AskToUpdateLinks = false;
            app.AutomationSecurity = 3; // msoAutomationSecurityForceDisable

            workbooks = app.Workbooks;
            workbook = ((dynamic)workbooks).Open(templatePath, 0, true);
            worksheets = ((dynamic)workbook).Worksheets;
            dataWorksheet = ((dynamic)worksheets)[DataSheetName];
            printWorksheet = ((dynamic)worksheets)[PrintSheetName];

            // Ghi dữ liệu vào sheet nhập của đúng mẫu người dùng đã căn ở phiên trước. Sheet
            // "IN LEN PHOI" liên kết sang sheet này và giữ nguyên toàn bộ tọa độ/khổ giấy của phôi.
            PopulateWorksheet(dataWorksheet, document);
            ApplyUnusedRowsSlash(printWorksheet, document.Lines.Count, PrintSlashColor);
            app.CalculateFullRebuild();

            var activePrinter = (Convert.ToString(app.ActivePrinter) ?? "").Trim();
            if (string.IsNullOrWhiteSpace(activePrinter))
                throw new WarehousePrintUnavailableException("Máy chủ chưa cấu hình máy in mặc định.");
            if (IsVirtualPrinter(activePrinter))
                throw new WarehousePrintUnavailableException(
                    $"Máy in mặc định của máy chủ đang là máy in ảo ({activePrinter}). Vui lòng chọn máy in giấy làm mặc định.");

            ((dynamic)printWorksheet).PrintOut(
                Copies: 1,
                Preview: false,
                ActivePrinter: activePrinter,
                PrintToFile: false,
                Collate: true,
                IgnorePrintAreas: false);
            var submittedAt = DateTime.UtcNow;

            ((dynamic)workbook).Close(false);
            workbookClosed = true;
            app.Quit();
            excelQuit = true;
            return new WarehouseServerPrintResult(activePrinter, submittedAt);
        }
        catch (COMException ex)
        {
            throw new WarehousePrintUnavailableException(
                "Microsoft Excel không thể mở mẫu hoặc gửi phiếu vào hàng đợi in của máy chủ.", ex);
        }
        finally
        {
            if (!workbookClosed && workbook is not null)
            {
                try { ((dynamic)workbook).Close(false); } catch { /* best effort */ }
            }
            if (!excelQuit && excel is not null)
            {
                try { ((dynamic)excel).Quit(); } catch { /* best effort */ }
            }

            ReleaseComObject(printWorksheet);
            ReleaseComObject(dataWorksheet);
            ReleaseComObject(worksheets);
            ReleaseComObject(workbook);
            ReleaseComObject(workbooks);
            ReleaseComObject(excel);
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ExportPreviewPdfWithExcel(
        string templatePath,
        DocumentDetailDto document)
    {
        var previewPath = Path.Combine(Path.GetTempPath(), $"KetoanMini_PhieuXuatKho_{Guid.NewGuid():N}.pdf");
        var excelType = Type.GetTypeFromProgID("Excel.Application", throwOnError: false);
        if (excelType is null)
            throw new WarehousePrintUnavailableException("Máy chủ chưa cài Microsoft Excel.");

        object? excel = null;
        object? workbooks = null;
        object? workbook = null;
        object? worksheets = null;
        object? dataWorksheet = null;
        object? printWorksheet = null;
        var workbookClosed = false;
        var excelQuit = false;

        try
        {
            excel = Activator.CreateInstance(excelType)
                ?? throw new WarehousePrintUnavailableException("Không khởi động được Microsoft Excel.");
            dynamic app = excel;
            app.Visible = false;
            app.DisplayAlerts = false;
            app.ScreenUpdating = false;
            app.EnableEvents = false;
            app.AskToUpdateLinks = false;
            app.AutomationSecurity = 3;

            workbooks = app.Workbooks;
            workbook = ((dynamic)workbooks).Open(templatePath, 0, true);
            worksheets = ((dynamic)workbook).Worksheets;
            dataWorksheet = ((dynamic)worksheets)[DataSheetName];
            printWorksheet = ((dynamic)worksheets)[PrintSheetName];

            PopulateWorksheet(dataWorksheet, document);
            ApplyUnusedRowsSlash(dataWorksheet, document.Lines.Count, PreviewSlashColor);
            app.CalculateFullRebuild();

            // Xem trước bằng sheet đầy đủ để người dùng thấy phiếu sau khi hoàn thiện. Khi in thật,
            // sheet "IN LEN PHOI" chỉ in phần dữ liệu lên đúng phôi giấy đã có sẵn.
            ((dynamic)dataWorksheet).ExportAsFixedFormat(
                Type: 0, // xlTypePDF
                Filename: previewPath,
                Quality: 0, // xlQualityStandard
                IncludeDocProperties: true,
                IgnorePrintAreas: false,
                OpenAfterPublish: false);

            var pdf = File.ReadAllBytes(previewPath);
            ((dynamic)workbook).Close(false);
            workbookClosed = true;
            app.Quit();
            excelQuit = true;
            return pdf;
        }
        catch (COMException ex)
        {
            throw new WarehousePrintUnavailableException(
                "Microsoft Excel không thể mở mẫu hoặc tạo file xem trước phiếu xuất kho.", ex);
        }
        finally
        {
            if (!workbookClosed && workbook is not null)
            {
                try { ((dynamic)workbook).Close(false); } catch { /* best effort */ }
            }
            if (!excelQuit && excel is not null)
            {
                try { ((dynamic)excel).Quit(); } catch { /* best effort */ }
            }

            ReleaseComObject(printWorksheet);
            ReleaseComObject(dataWorksheet);
            ReleaseComObject(worksheets);
            ReleaseComObject(workbook);
            ReleaseComObject(workbooks);
            ReleaseComObject(excel);

            try
            {
                if (File.Exists(previewPath)) File.Delete(previewPath);
            }
            catch
            {
                // File tạm sẽ được hệ điều hành dọn; không làm hỏng kết quả xem trước vì lỗi dọn file.
            }
        }
    }

    private static bool IsVirtualPrinter(string printerName) =>
        printerName.Contains("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase)
        || printerName.Contains("Microsoft XPS", StringComparison.OrdinalIgnoreCase)
        || printerName.Contains("OneNote", StringComparison.OrdinalIgnoreCase)
        || printerName.Contains("Fax", StringComparison.OrdinalIgnoreCase);

    [SupportedOSPlatform("windows")]
    private static string GetDefaultPrinterName()
    {
        uint length = 0;
        _ = GetDefaultPrinter(null, ref length);
        if (length == 0)
            throw new WarehousePrintUnavailableException("Máy chủ chưa cấu hình máy in mặc định.");

        var printerName = new StringBuilder((int)length);
        if (!GetDefaultPrinter(printerName, ref length))
            throw new WarehousePrintUnavailableException("Không đọc được máy in mặc định của máy chủ.");
        return printerName.ToString();
    }

    [SupportedOSPlatform("windows")]
    private static void EnsurePrinterReady(string printerName)
    {
        var snapshot = ReadPrinterSnapshot(printerName);
        if (!snapshot.Ready)
            throw new WarehousePrintUnavailableException(snapshot.Message);
    }

    [SupportedOSPlatform("windows")]
    private static PrinterSnapshot ReadPrinterSnapshot(string printerName)
    {
        if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
            throw new WarehousePrintUnavailableException($"Không kết nối được máy in máy chủ: {printerName}.");

        try
        {
            _ = GetPrinter(printerHandle, 2, IntPtr.Zero, 0, out var requiredBytes);
            if (requiredBytes == 0)
                throw new WarehousePrintUnavailableException($"Không đọc được trạng thái máy in: {printerName}.");

            var buffer = Marshal.AllocHGlobal((int)requiredBytes);
            try
            {
                if (!GetPrinter(printerHandle, 2, buffer, requiredBytes, out _))
                    throw new WarehousePrintUnavailableException($"Không đọc được trạng thái máy in: {printerName}.");

                var printer = Marshal.PtrToStructure<PrinterInfo2>(buffer);
                var jobs = ReadPrinterJobs(printerHandle, printer.JobCount);
                var queuedJobs = Math.Max(printer.JobCount, jobs.TotalCount);
                var ready = (printer.Attributes & PrinterAttributeWorkOffline) == 0
                    && (printer.Status & PrinterBlockingStatuses) == 0
                    && jobs.BlockingCount == 0;
                var message = jobs.BlockingCount > 0
                    ? DescribeJobIssue(jobs)
                    : ready
                        ? queuedJobs > 0
                            ? $"Máy in đang hoạt động, có {queuedJobs} lệnh bình thường trong hàng đợi."
                            : "Máy in đang kết nối và sẵn sàng nhận lệnh."
                        : DescribePrinterIssue(printer.Status, printer.Attributes);
                return new PrinterSnapshot(ready, message, queuedJobs, printer.Status);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = ClosePrinter(printerHandle);
        }
    }

    [SupportedOSPlatform("windows")]
    private static PrinterJobsSnapshot ReadPrinterJobs(IntPtr printerHandle, uint reportedJobCount)
    {
        if (reportedJobCount == 0)
            return new PrinterJobsSnapshot(0, 0, 0);

        _ = EnumJobs(
            printerHandle,
            0,
            reportedJobCount,
            1,
            IntPtr.Zero,
            0,
            out var requiredBytes,
            out _);
        if (requiredBytes == 0)
            return new PrinterJobsSnapshot(reportedJobCount, 0, 0);

        var buffer = Marshal.AllocHGlobal((int)requiredBytes);
        try
        {
            if (!EnumJobs(
                    printerHandle,
                    0,
                    reportedJobCount,
                    1,
                    buffer,
                    requiredBytes,
                    out _,
                    out var returnedJobs))
            {
                return new PrinterJobsSnapshot(reportedJobCount, 0, 0);
            }

            var jobInfoSize = Marshal.SizeOf<JobInfo1>();
            uint blockingCount = 0;
            uint combinedBlockingStatus = 0;
            for (var index = 0; index < returnedJobs; index++)
            {
                var jobAddress = IntPtr.Add(buffer, checked((int)(index * (uint)jobInfoSize)));
                var job = Marshal.PtrToStructure<JobInfo1>(jobAddress);
                var blockingStatus = job.Status & JobBlockingStatuses;
                if (blockingStatus == 0) continue;
                blockingCount++;
                combinedBlockingStatus |= blockingStatus;
            }

            return new PrinterJobsSnapshot(returnedJobs, blockingCount, combinedBlockingStatus);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string DescribeJobIssue(PrinterJobsSnapshot jobs)
    {
        var countText = $"{jobs.BlockingCount} lệnh in";
        if ((jobs.BlockingStatus & JobStatusPaperOut) != 0)
            return $"Có {countText} đang chờ vì máy in hết giấy.";
        if ((jobs.BlockingStatus & JobStatusOffline) != 0)
            return $"Có {countText} đang lỗi vì máy in ngoại tuyến.";
        if ((jobs.BlockingStatus & JobStatusUserIntervention) != 0)
            return $"Có {countText} đang cần người dùng xử lý.";
        if ((jobs.BlockingStatus & JobStatusBlocked) != 0)
            return $"Có {countText} bị chặn trong hàng đợi.";
        if ((jobs.BlockingStatus & JobStatusPaused) != 0)
            return $"Có {countText} đang tạm dừng trong hàng đợi.";
        return $"Có {countText} đang báo lỗi trong hàng đợi. Hãy mở hàng đợi máy in để kiểm tra hoặc hủy lệnh lỗi.";
    }

    private static string DescribePrinterIssue(uint status, uint attributes)
    {
        if ((attributes & PrinterAttributeWorkOffline) != 0 || (status & 0x00000080) != 0)
            return "Máy in đang ngoại tuyến. Hãy kiểm tra nguồn điện hoặc kết nối.";
        if ((status & 0x00000008) != 0)
            return "Máy in đang kẹt giấy.";
        if ((status & 0x00000010) != 0)
            return "Máy in đã hết giấy.";
        if ((status & 0x00040000) != 0)
            return "Máy in đã hết mực.";
        if ((status & 0x00400000) != 0)
            return "Nắp máy in đang mở.";
        if ((status & 0x00100000) != 0)
            return "Máy in đang cần người dùng xử lý.";
        if ((status & 0x00001000) != 0 || (status & 0x00800000) != 0)
            return "Máy in hiện không khả dụng.";
        return "Máy in đang báo lỗi và cần được kiểm tra.";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo2
    {
        public IntPtr ServerName;
        public IntPtr PrinterName;
        public IntPtr ShareName;
        public IntPtr PortName;
        public IntPtr DriverName;
        public IntPtr Comment;
        public IntPtr Location;
        public IntPtr DevMode;
        public IntPtr SeparatorFile;
        public IntPtr PrintProcessor;
        public IntPtr DataType;
        public IntPtr Parameters;
        public IntPtr SecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint JobCount;
        public uint AveragePagesPerMinute;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JobInfo1
    {
        public uint JobId;
        public IntPtr PrinterName;
        public IntPtr MachineName;
        public IntPtr UserName;
        public IntPtr Document;
        public IntPtr DataType;
        public IntPtr StatusText;
        public uint Status;
        public uint Priority;
        public uint Position;
        public uint TotalPages;
        public uint PagesPrinted;
        public SystemTime Submitted;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDefaultPrinter(StringBuilder? printerName, ref uint bufferLength);

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printerHandle, IntPtr printerDefaults);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPrinter(
        IntPtr printerHandle,
        uint level,
        IntPtr printerInfo,
        uint bufferSize,
        out uint requiredBytes);

    [DllImport("winspool.drv", EntryPoint = "EnumJobsW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumJobs(
        IntPtr printerHandle,
        uint firstJob,
        uint numberOfJobs,
        uint level,
        IntPtr jobInfo,
        uint bufferSize,
        out uint requiredBytes,
        out uint returnedJobs);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr printerHandle);

    [SupportedOSPlatform("windows")]
    private static void PopulateWorksheet(object worksheet, DocumentDetailDto document)
    {
        SetText(worksheet, "D12", document.CustomerName);
        SetText(worksheet, "C13", document.Note);
        SetNumber(worksheet, "D14", document.Date.Day);
        SetNumber(worksheet, "H14", document.Date.Month);
        SetText(worksheet, "R14", document.VoucherNo);

        if (document.Date.Year is >= 2020 and <= 2029)
        {
            SetText(worksheet, "J14", "năm 202");
            SetNumber(worksheet, "M14", document.Date.Year % 10);
        }
        else
        {
            SetText(worksheet, "J14", "năm");
            SetNumber(worksheet, "M14", document.Date.Year);
        }

        for (var index = 0; index < MaxLineCount; index++)
        {
            var row = FirstLineRow + index;
            ClearCell(worksheet, $"B{row}");
            ClearCell(worksheet, $"K{row}");
            ClearCell(worksheet, $"M{row}");
            ClearCell(worksheet, $"O{row}");

            if (index < document.Lines.Count)
            {
                var line = document.Lines[index];
                SetText(
                    worksheet,
                    $"B{row}",
                    FormatWarehouseItemDescription(line.LineContent, line.Spec));
                SetNumber(worksheet, $"M{row}", line.Quantity);
                SetNumber(worksheet, $"O{row}", line.UnitPrice);
            }

            SetFormula(worksheet, $"R{row}", $"=IF(OR(M{row}=\"\",O{row}=\"\"),\"\",M{row}*O{row})");
        }

        SetFormula(worksheet, "R30", $"=IF(COUNT(R{FirstLineRow}:R{LastLineRow})=0,\"\",SUM(R{FirstLineRow}:R{LastLineRow}))");
        SetFormula(worksheet, "E31", "=IF(R30=\"\",\"\",R30)");
        ClearCell(worksheet, "F32");
        SetFormula(worksheet, "G33", "=IF(R30=\"\",\"\",R30-IF(F32=\"\",0,F32))");
    }

    internal static string FormatWarehouseItemDescription(string? lineContent, string? specification)
    {
        var content = (lineContent ?? "").Trim();
        var spec = (specification ?? "").Trim();

        if (string.IsNullOrEmpty(content)) return spec;
        if (string.IsNullOrEmpty(spec)) return content;
        return $"{content} {spec}";
    }

    internal static int? GetFirstUnusedWorksheetRow(int usedLineCount)
    {
        if (usedLineCount is < 0 or > MaxLineCount)
            throw new ArgumentOutOfRangeException(nameof(usedLineCount));
        return usedLineCount == MaxLineCount ? null : FirstLineRow + usedLineCount;
    }

    internal static WarehouseSlashEndpoints CalculateUnusedRowsSlashEndpoints(
        double left,
        double top,
        double width,
        double height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var startX = left + width;
        var startY = top;
        var endX = left;
        var endY = top + height;
        var length = Math.Sqrt((width * width) + (height * height));
        var inset = Math.Min(UnusedRowsSlashEndInsetPoints, length * 0.2);
        var unitX = (endX - startX) / length;
        var unitY = (endY - startY) / length;

        return new WarehouseSlashEndpoints(
            StartX: startX + (unitX * inset),
            StartY: startY + (unitY * inset),
            EndX: endX - (unitX * inset),
            EndY: endY - (unitY * inset));
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyUnusedRowsSlash(object worksheet, int usedLineCount, int color)
    {
        var firstUnusedRow = GetFirstUnusedWorksheetRow(usedLineCount);
        if (firstUnusedRow is null) return;

        object? range = null;
        object? shapes = null;
        object? oldShape = null;
        object? shape = null;
        object? line = null;
        object? foreColor = null;
        try
        {
            range = ((dynamic)worksheet).Range[$"A{firstUnusedRow}:U{LastLineRow}"];
            var endpoints = CalculateUnusedRowsSlashEndpoints(
                Convert.ToDouble(((dynamic)range).Left),
                Convert.ToDouble(((dynamic)range).Top),
                Convert.ToDouble(((dynamic)range).Width),
                Convert.ToDouble(((dynamic)range).Height));

            shapes = ((dynamic)worksheet).Shapes;
            try
            {
                oldShape = ((dynamic)shapes).Item(UnusedRowsSlashName);
                ((dynamic)oldShape).Delete();
            }
            catch (Exception ex) when (ex is COMException or ArgumentException)
            {
                // Excel có thể trả COMException hoặc ArgumentException khi mẫu sạch chưa có nét chéo cũ.
            }
            finally
            {
                ReleaseComObject(oldShape);
                oldShape = null;
            }

            shape = ((dynamic)shapes).AddLine(
                endpoints.StartX,
                endpoints.StartY,
                endpoints.EndX,
                endpoints.EndY);
            ((dynamic)shape).Name = UnusedRowsSlashName;

            line = ((dynamic)shape).Line;
            ((dynamic)line).Visible = -1; // msoTrue
            ((dynamic)line).Weight = 1.1;
            ((dynamic)line).Transparency = 0;
            foreColor = ((dynamic)line).ForeColor;
            ((dynamic)foreColor).RGB = color;
        }
        finally
        {
            ReleaseComObject(foreColor);
            ReleaseComObject(line);
            ReleaseComObject(shape);
            ReleaseComObject(oldShape);
            ReleaseComObject(shapes);
            ReleaseComObject(range);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetText(object worksheet, string address, string? value)
    {
        object? range = null;
        try
        {
            range = ((dynamic)worksheet).Range[address];
            ((dynamic)range).NumberFormat = "@";
            ((dynamic)range).Value2 = value ?? "";
        }
        finally
        {
            ReleaseComObject(range);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetNumber(object worksheet, string address, decimal value)
    {
        object? range = null;
        try
        {
            range = ((dynamic)worksheet).Range[address];
            ((dynamic)range).Value2 = Convert.ToDouble(value);
        }
        finally
        {
            ReleaseComObject(range);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetFormula(object worksheet, string address, string formula)
    {
        object? range = null;
        try
        {
            range = ((dynamic)worksheet).Range[address];
            ((dynamic)range).Formula = formula;
        }
        finally
        {
            ReleaseComObject(range);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ClearCell(object worksheet, string address)
    {
        object? range = null;
        object? mergeArea = null;
        try
        {
            range = ((dynamic)worksheet).Range[address];
            mergeArea = ((dynamic)range).MergeArea;
            ((dynamic)mergeArea).ClearContents();
        }
        finally
        {
            ReleaseComObject(mergeArea);
            ReleaseComObject(range);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}

internal readonly record struct WarehouseSlashEndpoints(
    double StartX,
    double StartY,
    double EndX,
    double EndY);

public sealed record WarehouseServerPrintResult(string PrinterName, DateTime SubmittedAt);

public sealed record WarehousePrintSystemStatus(
    WarehouseServerStatus Server,
    WarehousePrinterStatus Printer,
    DateTime CheckedAt);

public sealed record WarehouseServerStatus(
    bool Online,
    string Name,
    bool PrintServiceReady,
    string Message,
    long UptimeSeconds);

public sealed record WarehousePrinterStatus(
    bool Available,
    bool Ready,
    string? Name,
    string Message,
    uint QueuedJobs,
    uint StatusCode);

internal sealed record PrinterSnapshot(bool Ready, string Message, uint QueuedJobs, uint StatusCode);
internal sealed record PrinterJobsSnapshot(uint TotalCount, uint BlockingCount, uint BlockingStatus);

public abstract class WarehousePrintException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class WarehousePrintValidationException(string message)
    : WarehousePrintException(message);

public sealed class WarehousePrintUnavailableException(string message, Exception? innerException = null)
    : WarehousePrintException(message, innerException);
