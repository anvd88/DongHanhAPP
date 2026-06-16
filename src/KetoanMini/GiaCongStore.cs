using System.Globalization;
using Microsoft.Data.SqlClient;

namespace KetoanMini;

public sealed class GiaCongStore
{
    private readonly string _connectionString;

    public GiaCongStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    // -------------------------------------------------------------------------
    // Schema
    // -------------------------------------------------------------------------

    public void EnsureGiaCongTables()
    {
        using var connection = OpenConnection();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='gia_cong_phieu' AND xtype='U')
                CREATE TABLE gia_cong_phieu (
                    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
                    ma_phieu        NVARCHAR(20)   NOT NULL,
                    loai_phieu      NVARCHAR(50)   NOT NULL,
                    doi_tac         NVARCHAR(200)  NOT NULL DEFAULT '',
                    nhan_vien       NVARCHAR(200)  NOT NULL DEFAULT '',
                    ngay_lap        DATE           NOT NULL,
                    han_hoan_thanh  DATE               NULL,
                    trang_thai      NVARCHAR(50)   NOT NULL DEFAULT N'Đang xử lý',
                    tien_do         INT            NOT NULL DEFAULT 0,
                    buoc_hien_tai   INT            NOT NULL DEFAULT 1,
                    ghi_chu         NVARCHAR(1000) NOT NULL DEFAULT '',
                    created_at      DATETIME2      NOT NULL DEFAULT GETDATE(),
                    updated_at      DATETIME2      NOT NULL DEFAULT GETDATE()
                );
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='gia_cong_hang_hoa' AND xtype='U')
                CREATE TABLE gia_cong_hang_hoa (
                    id                 BIGINT IDENTITY(1,1) PRIMARY KEY,
                    phieu_id           BIGINT        NOT NULL REFERENCES gia_cong_phieu(id) ON DELETE CASCADE,
                    loai_dong          NVARCHAR(50)  NOT NULL DEFAULT N'Nguyên liệu',
                    ma_hang            NVARCHAR(50)  NOT NULL DEFAULT '',
                    ten_hang           NVARCHAR(200) NOT NULL DEFAULT '',
                    don_vi_tinh        NVARCHAR(30)  NOT NULL DEFAULT '',
                    so_luong           DECIMAL(18,2) NOT NULL DEFAULT 0,
                    don_gia_gia_cong   DECIMAL(18,2) NOT NULL DEFAULT 0,
                    ghi_chu            NVARCHAR(500) NOT NULL DEFAULT '',
                    trang_thai_dong    NVARCHAR(50)  NOT NULL DEFAULT N'Chờ'
                );
                """;
            cmd.ExecuteNonQuery();
        }
    }

    // -------------------------------------------------------------------------
    // Code generation
    // -------------------------------------------------------------------------

    public string GenMaPhieu()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ISNULL(MAX(id), 0) FROM gia_cong_phieu;";
        var maxId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        return $"GC{(maxId + 1):D6}";
    }

    // -------------------------------------------------------------------------
    // Queries
    // -------------------------------------------------------------------------

    public List<GiaCongPhieu> GetAllPhieu()
    {
        using var connection = OpenConnection();

        // Load aggregate counts and sums from hang_hoa in one go
        var stats = new Dictionary<long, (int count, decimal total)>();
        using (var aggCmd = connection.CreateCommand())
        {
            aggCmd.CommandText = """
                SELECT phieu_id,
                       COUNT(*)                              AS so_mat_hang,
                       ISNULL(SUM(so_luong * don_gia_gia_cong), 0) AS tong_gia_tri
                FROM gia_cong_hang_hoa
                GROUP BY phieu_id;
                """;
            using var aggReader = aggCmd.ExecuteReader();
            while (aggReader.Read())
            {
                var pid   = GetInt64(aggReader, "phieu_id");
                var count = (int)GetInt64(aggReader, "so_mat_hang");
                var total = GetDecimal(aggReader, "tong_gia_tri");
                stats[pid] = (count, total);
            }
        }

        var list = new List<GiaCongPhieu>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, ma_phieu, loai_phieu, doi_tac, nhan_vien,
                   ngay_lap, han_hoan_thanh, trang_thai, tien_do,
                   buoc_hien_tai, ghi_chu, created_at, updated_at
            FROM gia_cong_phieu
            ORDER BY id DESC;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var phieu = ReadPhieu(reader);
            if (stats.TryGetValue(phieu.Id, out var s))
            {
                phieu.SoMatHang  = s.count;
                phieu.TongGiaTri = s.total;
            }

            list.Add(phieu);
        }

        return list;
    }

    public GiaCongPhieu? GetPhieuById(long id)
    {
        using var connection = OpenConnection();

        GiaCongPhieu? phieu = null;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, ma_phieu, loai_phieu, doi_tac, nhan_vien,
                       ngay_lap, han_hoan_thanh, trang_thai, tien_do,
                       buoc_hien_tai, ghi_chu, created_at, updated_at
                FROM gia_cong_phieu
                WHERE id = @id;
                """;
            command.Parameters.AddWithValue("@id", id);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                phieu = ReadPhieu(reader);
            }
        }

        if (phieu is null)
        {
            return null;
        }

        using (var lineCmd = connection.CreateCommand())
        {
            lineCmd.CommandText = """
                SELECT id, phieu_id, loai_dong, ma_hang, ten_hang,
                       don_vi_tinh, so_luong, don_gia_gia_cong,
                       ghi_chu, trang_thai_dong
                FROM gia_cong_hang_hoa
                WHERE phieu_id = @phieuId
                ORDER BY id;
                """;
            lineCmd.Parameters.AddWithValue("@phieuId", phieu.Id);
            using var lineReader = lineCmd.ExecuteReader();
            while (lineReader.Read())
            {
                phieu.HangHoaList.Add(ReadHangHoa(lineReader));
            }
        }

        phieu.SoMatHang  = phieu.HangHoaList.Count;
        phieu.TongGiaTri = phieu.HangHoaList.Sum(h => h.ThanhTien);
        return phieu;
    }

    // -------------------------------------------------------------------------
    // Mutations
    // -------------------------------------------------------------------------

    public GiaCongPhieu CreatePhieu(
        string loaiPhieu,
        string doiTac,
        string nhanVien,
        DateOnly ngayLap,
        DateOnly? hanHoanThanh,
        string ghiChu,
        List<GiaCongHangHoa> lines)
    {
        var maPhieu = GenMaPhieu();
        var now     = DateTime.Now;

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        long newId;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO gia_cong_phieu
                    (ma_phieu, loai_phieu, doi_tac, nhan_vien, ngay_lap,
                     han_hoan_thanh, trang_thai, tien_do, buoc_hien_tai,
                     ghi_chu, created_at, updated_at)
                OUTPUT INSERTED.id
                VALUES
                    (@maPhieu, @loaiPhieu, @doiTac, @nhanVien, @ngayLap,
                     @hanHoanThanh, @trangThai, 0, 1,
                     @ghiChu, @createdAt, @updatedAt);
                """;
            command.Parameters.AddWithValue("@maPhieu",       maPhieu);
            command.Parameters.AddWithValue("@loaiPhieu",     loaiPhieu.Trim());
            command.Parameters.AddWithValue("@doiTac",        doiTac.Trim());
            command.Parameters.AddWithValue("@nhanVien",      nhanVien.Trim());
            command.Parameters.AddWithValue("@ngayLap",       ToDatabaseDate(ngayLap));
            command.Parameters.AddWithValue("@hanHoanThanh",  hanHoanThanh is null ? DBNull.Value : ToDatabaseDate(hanHoanThanh.Value));
            command.Parameters.AddWithValue("@trangThai",     GiaCongTrangThai.DangXuLy);
            command.Parameters.AddWithValue("@ghiChu",        ghiChu.Trim());
            command.Parameters.AddWithValue("@createdAt",     ToDatabaseDateTime(now));
            command.Parameters.AddWithValue("@updatedAt",     ToDatabaseDateTime(now));
            newId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        InsertLines(connection, transaction, newId, lines);
        transaction.Commit();

        return GetPhieuById(newId) ?? new GiaCongPhieu
        {
            Id         = newId,
            MaPhieu    = maPhieu,
            LoaiPhieu  = loaiPhieu.Trim(),
            DoiTac     = doiTac.Trim(),
            NhanVienPhuTrach = nhanVien.Trim(),
            NgayLap    = ngayLap,
            HanHoanThanh = hanHoanThanh,
            GhiChu     = ghiChu.Trim(),
            CreatedAt  = now,
            UpdatedAt  = now,
        };
    }

    public void UpdatePhieuTrangThai(long phieuId, string trangThai, int tienDo, int buocHienTai)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE gia_cong_phieu
            SET trang_thai    = @trangThai,
                tien_do       = @tienDo,
                buoc_hien_tai = @buocHienTai,
                updated_at    = @updatedAt
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id",           phieuId);
        command.Parameters.AddWithValue("@trangThai",    trangThai.Trim());
        command.Parameters.AddWithValue("@tienDo",       Math.Clamp(tienDo, 0, 100));
        command.Parameters.AddWithValue("@buocHienTai",  Math.Clamp(buocHienTai, 1, 5));
        command.Parameters.AddWithValue("@updatedAt",    ToDatabaseDateTime(DateTime.Now));
        command.ExecuteNonQuery();
    }

    public void UpdatePhieu(
        long phieuId,
        string loaiPhieu,
        string doiTac,
        string nhanVien,
        DateOnly ngayLap,
        DateOnly? hanHoanThanh,
        string ghiChu,
        List<GiaCongHangHoa> lines)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE gia_cong_phieu
                SET loai_phieu     = @loaiPhieu,
                    doi_tac        = @doiTac,
                    nhan_vien      = @nhanVien,
                    ngay_lap       = @ngayLap,
                    han_hoan_thanh = @hanHoanThanh,
                    ghi_chu        = @ghiChu,
                    updated_at     = @updatedAt
                WHERE id = @id;
                """;
            command.Parameters.AddWithValue("@id",            phieuId);
            command.Parameters.AddWithValue("@loaiPhieu",     loaiPhieu.Trim());
            command.Parameters.AddWithValue("@doiTac",        doiTac.Trim());
            command.Parameters.AddWithValue("@nhanVien",      nhanVien.Trim());
            command.Parameters.AddWithValue("@ngayLap",       ToDatabaseDate(ngayLap));
            command.Parameters.AddWithValue("@hanHoanThanh",  hanHoanThanh is null ? DBNull.Value : ToDatabaseDate(hanHoanThanh.Value));
            command.Parameters.AddWithValue("@ghiChu",        ghiChu.Trim());
            command.Parameters.AddWithValue("@updatedAt",     ToDatabaseDateTime(DateTime.Now));
            command.ExecuteNonQuery();
        }

        // Replace all lines: delete existing then re-insert
        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM gia_cong_hang_hoa WHERE phieu_id = @phieuId;";
            deleteCmd.Parameters.AddWithValue("@phieuId", phieuId);
            deleteCmd.ExecuteNonQuery();
        }

        InsertLines(connection, transaction, phieuId, lines);
        transaction.Commit();
    }

    public void DeletePhieu(long phieuId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        // ON DELETE CASCADE handles gia_cong_hang_hoa rows automatically
        command.CommandText = "DELETE FROM gia_cong_phieu WHERE id = @id;";
        command.Parameters.AddWithValue("@id", phieuId);
        command.ExecuteNonQuery();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void InsertLines(
        SqlConnection connection,
        SqlTransaction transaction,
        long phieuId,
        List<GiaCongHangHoa> lines)
    {
        foreach (var line in lines)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO gia_cong_hang_hoa
                    (phieu_id, loai_dong, ma_hang, ten_hang, don_vi_tinh,
                     so_luong, don_gia_gia_cong, ghi_chu, trang_thai_dong)
                VALUES
                    (@phieuId, @loaiDong, @maHang, @tenHang, @donViTinh,
                     @soLuong, @donGiaGiaCong, @ghiChu, @trangThaiDong);
                """;
            cmd.Parameters.AddWithValue("@phieuId",        phieuId);
            cmd.Parameters.AddWithValue("@loaiDong",       line.LoaiDong.Trim());
            cmd.Parameters.AddWithValue("@maHang",         line.MaHang.Trim());
            cmd.Parameters.AddWithValue("@tenHang",        line.TenHang.Trim());
            cmd.Parameters.AddWithValue("@donViTinh",      line.DonViTinh.Trim());
            cmd.Parameters.AddWithValue("@soLuong",        line.SoLuong);
            cmd.Parameters.AddWithValue("@donGiaGiaCong",  line.DonGiaGiaCong);
            cmd.Parameters.AddWithValue("@ghiChu",         line.GhiChu.Trim());
            cmd.Parameters.AddWithValue("@trangThaiDong",  line.TrangThaiDong.Trim());
            cmd.ExecuteNonQuery();
        }
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    // -------------------------------------------------------------------------
    // Reader helpers — mirrors the private helpers in AccountingStore
    // -------------------------------------------------------------------------

    private static GiaCongPhieu ReadPhieu(SqlDataReader reader)
    {
        var hanRaw = GetString(reader, "han_hoan_thanh");
        return new GiaCongPhieu
        {
            Id               = GetInt64(reader, "id"),
            MaPhieu          = GetString(reader, "ma_phieu"),
            LoaiPhieu        = GetString(reader, "loai_phieu"),
            DoiTac           = GetString(reader, "doi_tac"),
            NhanVienPhuTrach = GetString(reader, "nhan_vien"),
            NgayLap          = ParseDateOnly(GetString(reader, "ngay_lap")),
            HanHoanThanh     = string.IsNullOrWhiteSpace(hanRaw) ? null : ParseDateOnly(hanRaw),
            TrangThai        = GetString(reader, "trang_thai"),
            TienDo           = (int)GetInt64(reader, "tien_do"),
            BuocHienTai      = (int)GetInt64(reader, "buoc_hien_tai"),
            GhiChu           = GetString(reader, "ghi_chu"),
            CreatedAt        = ParseDateTime(GetString(reader, "created_at")),
            UpdatedAt        = ParseDateTime(GetString(reader, "updated_at")),
        };
    }

    private static GiaCongHangHoa ReadHangHoa(SqlDataReader reader)
    {
        return new GiaCongHangHoa
        {
            Id              = GetInt64(reader, "id"),
            PhieuId         = GetInt64(reader, "phieu_id"),
            LoaiDong        = GetString(reader, "loai_dong"),
            MaHang          = GetString(reader, "ma_hang"),
            TenHang         = GetString(reader, "ten_hang"),
            DonViTinh       = GetString(reader, "don_vi_tinh"),
            SoLuong         = GetDecimal(reader, "so_luong"),
            DonGiaGiaCong   = GetDecimal(reader, "don_gia_gia_cong"),
            GhiChu          = GetString(reader, "ghi_chu"),
            TrangThaiDong   = GetString(reader, "trang_thai_dong"),
        };
    }

    private static string GetString(SqlDataReader reader, string columnName)
    {
        int ordinal;
        try
        {
            ordinal = reader.GetOrdinal(columnName);
        }
        catch (IndexOutOfRangeException)
        {
            return "";
        }

        if (reader.IsDBNull(ordinal))
        {
            return "";
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTime dt     => dt.ToString("O", CultureInfo.InvariantCulture),
            DateOnly d      => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal dec     => dec.ToString(CultureInfo.InvariantCulture),
            bool b          => b ? "1" : "0",
            _               => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        };
    }

    private static long GetInt64(SqlDataReader reader, string columnName)
    {
        int ordinal;
        try
        {
            ordinal = reader.GetOrdinal(columnName);
        }
        catch (IndexOutOfRangeException)
        {
            return 0;
        }

        return reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static decimal GetDecimal(SqlDataReader reader, string columnName)
    {
        int ordinal;
        try
        {
            ordinal = reader.GetOrdinal(columnName);
        }
        catch (IndexOutOfRangeException)
        {
            return 0m;
        }

        if (reader.IsDBNull(ordinal))
        {
            return 0m;
        }

        return decimal.TryParse(
            Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result) ? result : 0m;
    }

    private static string ToDatabaseDate(DateOnly date)
        => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string ToDatabaseDateTime(DateTime dt)
        => dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static DateOnly ParseDateOnly(string value)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? DateOnly.FromDateTime(dt)
            : DateOnly.FromDateTime(DateTime.Today);
    }

    private static DateTime ParseDateTime(string value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : DateTime.Now;
}
