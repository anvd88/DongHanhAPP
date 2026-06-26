using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using Microsoft.Data.SqlClient;

namespace KetoanMini.Api.Endpoints;

public static class GiaCongEndpoints
{
    private const string LoaiXuat = "Xuất gia công";
    private const string LoaiNhap = "Nhập gia công";

    private const string TypedRowsSelect = @"
        SELECT
            p.id AS phieu_id, p.doi_tac, h.ten_hang, h.quy_cach, h.don_vi_tinh,
            h.so_luong, h.don_gia_gia_cong,
            CASE
                WHEN h.loai_dong LIKE N'%Nhập%' OR p.loai_phieu LIKE N'%Nhập%' THEN N'Nhap'
                WHEN h.loai_dong LIKE N'%Xuất%' OR p.loai_phieu LIKE N'%Xuất%' THEN N'Xuat'
                ELSE N''
            END AS loai
        FROM gia_cong_phieu p
        JOIN gia_cong_hang_hoa h ON h.phieu_id = p.id
        WHERE p.loai_phieu LIKE N'%Xuất%' OR p.loai_phieu LIKE N'%Nhập%'
           OR h.loai_dong LIKE N'%Xuất%' OR h.loai_dong LIKE N'%Nhập%'";

    private const string AggregateColumns = @"
        ISNULL(SUM(CASE WHEN loai = N'Xuat' THEN so_luong ELSE 0 END), 0) AS so_luong_xuat,
        ISNULL(SUM(CASE WHEN loai = N'Nhap' THEN so_luong ELSE 0 END), 0) AS so_luong_nhap,
        ISNULL(CASE
            WHEN SUM(CASE WHEN loai = N'Xuat' THEN so_luong ELSE 0 END)
                - SUM(CASE WHEN loai = N'Nhap' THEN so_luong ELSE 0 END) < 0 THEN 0
            ELSE SUM(CASE WHEN loai = N'Xuat' THEN so_luong ELSE 0 END)
                - SUM(CASE WHEN loai = N'Nhap' THEN so_luong ELSE 0 END)
        END, 0) AS so_luong_con_tai_cong_ty,
        ISNULL(SUM(CASE WHEN loai = N'Nhap' THEN so_luong * don_gia_gia_cong ELSE 0 END), 0) AS tien_gia_cong_phai_tra";

    public static async Task EnsureTables(Database db)
    {
        await using var conn = await db.OpenAsync();
        await conn.Cmd(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='gia_cong_phieu' AND xtype='U')
            CREATE TABLE gia_cong_phieu (
                id BIGINT IDENTITY(1,1) PRIMARY KEY, ma_phieu NVARCHAR(20) NOT NULL,
                loai_phieu NVARCHAR(50) NOT NULL, doi_tac NVARCHAR(200) NOT NULL DEFAULT '',
                nhan_vien NVARCHAR(200) NOT NULL DEFAULT '', ngay_lap DATE NOT NULL,
                han_hoan_thanh DATE NULL,
                ghi_chu NVARCHAR(1000) NOT NULL DEFAULT '',
                created_at DATETIME2 NOT NULL DEFAULT GETDATE(), updated_at DATETIME2 NOT NULL DEFAULT GETDATE());").ExecuteNonQueryAsync();
        await conn.Cmd(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='gia_cong_hang_hoa' AND xtype='U')
            CREATE TABLE gia_cong_hang_hoa (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                phieu_id BIGINT NOT NULL REFERENCES gia_cong_phieu(id) ON DELETE CASCADE,
                loai_dong NVARCHAR(50) NOT NULL DEFAULT N'Xuất gia công', ma_hang NVARCHAR(50) NOT NULL DEFAULT '',
                ten_hang NVARCHAR(200) NOT NULL DEFAULT '', quy_cach NVARCHAR(200) NOT NULL DEFAULT '',
                don_vi_tinh NVARCHAR(30) NOT NULL DEFAULT '',
                so_luong DECIMAL(18,2) NOT NULL DEFAULT 0, don_gia_gia_cong DECIMAL(18,2) NOT NULL DEFAULT 0,
                ghi_chu NVARCHAR(500) NOT NULL DEFAULT '');").ExecuteNonQueryAsync();
        await conn.Cmd(@"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name='quy_cach' AND Object_ID=Object_ID('gia_cong_hang_hoa'))
            ALTER TABLE gia_cong_hang_hoa ADD quy_cach NVARCHAR(200) NOT NULL DEFAULT '';").ExecuteNonQueryAsync();
    }

    public static void MapGiaCong(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/giacong").RequireAuthorization();

        g.MapGet("/", async (Database db, string? filter, string? search) =>
        {
            await using var conn = await db.OpenAsync();
            var stats = await ReadStatsByPhieu(conn);

            var where = filter switch
            {
                "nhap" => "WHERE p.loai_phieu LIKE N'%Nhập%'",
                "xuat" => "WHERE p.loai_phieu LIKE N'%Xuất%'",
                _ => "WHERE (p.loai_phieu LIKE N'%Xuất%' OR p.loai_phieu LIKE N'%Nhập%')"
            };
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND (p.ma_phieu LIKE @s OR p.doi_tac LIKE @s OR p.nhan_vien LIKE @s)";

            var list = new List<GiaCongListItemDto>();
            var cmd = conn.Cmd(
                $@"SELECT p.id, p.ma_phieu, p.loai_phieu, p.doi_tac, p.nhan_vien, p.ngay_lap, p.han_hoan_thanh
                   FROM gia_cong_phieu p {where} ORDER BY p.id DESC");
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@s", $"%{search}%");
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                {
                    var id = r.Long("id");
                    var s = stats.GetValueOrDefault(id, GiaCongStats.Empty);
                    list.Add(new GiaCongListItemDto(id, r.Str("ma_phieu"), r.Str("loai_phieu"), r.Str("doi_tac"),
                        r.Str("nhan_vien"), r.DateOnly("ngay_lap"),
                        r.IsDBNull(r.GetOrdinal("han_hoan_thanh")) ? null : r.DateOnly("han_hoan_thanh"),
                        s.Count, s.TongGiaTri, s.SoLuongXuat, s.SoLuongNhap, s.SoLuongConTaiCongTy,
                        s.TienGiaCongPhaiTra));
                }
            return Results.Ok(list);
        });

        g.MapGet("/report", async (Database db, string? doiTac, DateOnly? from, DateOnly? to) =>
        {
            await using var conn = await db.OpenAsync();
            return Results.Ok(await ReadReport(conn, doiTac, from, to));
        });

        g.MapGet("/{id:long}", async (long id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            GiaCongDetailDto? p = null;
            await using (var r = await conn.Cmd(
                @"SELECT id, ma_phieu, loai_phieu, doi_tac, nhan_vien, ngay_lap, han_hoan_thanh, ghi_chu
                  FROM gia_cong_phieu WHERE id=@id AND (loai_phieu LIKE N'%Xuất%' OR loai_phieu LIKE N'%Nhập%')")
                .With("@id", id).ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                    p = new GiaCongDetailDto(r.Long("id"), r.Str("ma_phieu"), r.Str("loai_phieu"), r.Str("doi_tac"),
                        r.Str("nhan_vien"), r.DateOnly("ngay_lap"),
                        r.IsDBNull(r.GetOrdinal("han_hoan_thanh")) ? null : r.DateOnly("han_hoan_thanh"),
                        r.Str("ghi_chu"), new());
            }
            if (p is null) return Results.NotFound();
            await using (var r = await conn.Cmd(
                @"SELECT id, loai_dong, ma_hang, ten_hang, quy_cach, don_vi_tinh, so_luong, don_gia_gia_cong, ghi_chu
                  FROM gia_cong_hang_hoa
                  WHERE phieu_id=@id
                  ORDER BY id").With("@id", id).ExecuteReaderAsync())
                while (await r.ReadAsync())
                    p.Lines.Add(new GiaCongLineDto(r.Long("id"), p.LoaiPhieu,
                        r.Str("ma_hang"), r.Str("ten_hang"), r.Str("quy_cach"), r.Str("don_vi_tinh"),
                        r.Dec("so_luong"), r.Dec("don_gia_gia_cong"), r.Str("ghi_chu")));
            return Results.Ok(p);
        });

        g.MapPost("/", async (SaveGiaCongRequest req, ClaimsPrincipal u, Database db) => await Save(db, u, null, req));
        g.MapPut("/{id:long}", async (long id, SaveGiaCongRequest req, ClaimsPrincipal u, Database db) => await Save(db, u, id, req));

        g.MapDelete("/{id:long}", async (long id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM gia_cong_phieu WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Xóa phiếu gia công", "GiaCong", id.ToString(), "Xóa phiếu gia công (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        });
    }

    private static async Task<Dictionary<long, GiaCongStats>> ReadStatsByPhieu(SqlConnection conn)
    {
        var stats = new Dictionary<long, GiaCongStats>();
        await using var r = await conn.Cmd(
            $@"WITH typed AS ({TypedRowsSelect})
               SELECT phieu_id, COUNT(*) AS so_mat_hang, {AggregateColumns}
               FROM typed WHERE loai IN (N'Xuat', N'Nhap') GROUP BY phieu_id").ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            stats[r.Long("phieu_id")] = ReadStats(r);
        }
        return stats;
    }

    private static async Task<GiaCongReportDto> ReadReport(SqlConnection conn, string? doiTac, DateOnly? from, DateOnly? to)
    {
        var where = BuildReportWhere(doiTac, from, to);

        GiaCongStats total;
        await using (var r = await CreateReportCommand(conn,
            $@"WITH typed AS ({TypedRowsSelect} {where})
               SELECT COUNT(*) AS so_mat_hang, {AggregateColumns} FROM typed WHERE loai IN (N'Xuat', N'Nhap')",
            doiTac, from, to).ExecuteReaderAsync())
        {
            total = await r.ReadAsync() ? ReadStats(r) : GiaCongStats.Empty;
        }

        var partners = new List<GiaCongReportPartnerDto>();
        await using (var r = await CreateReportCommand(conn,
            $@"WITH typed AS ({TypedRowsSelect} {where})
               SELECT COALESCE(NULLIF(doi_tac, N''), N'Chưa nhập đối tác') AS doi_tac,
                      COUNT(*) AS so_mat_hang, {AggregateColumns}
               FROM typed
               WHERE loai IN (N'Xuat', N'Nhap')
               GROUP BY COALESCE(NULLIF(doi_tac, N''), N'Chưa nhập đối tác')
               ORDER BY doi_tac", doiTac, from, to).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var s = ReadStats(r);
                partners.Add(new GiaCongReportPartnerDto(r.Str("doi_tac"), s.SoLuongXuat, s.SoLuongNhap,
                    s.SoLuongConTaiCongTy, s.TienGiaCongPhaiTra));
            }
        }

        var items = new List<GiaCongReportItemDto>();
        await using (var r = await CreateReportCommand(conn,
            $@"WITH typed AS ({TypedRowsSelect} {where})
               SELECT COALESCE(NULLIF(doi_tac, N''), N'Chưa nhập đối tác') AS doi_tac,
                      COALESCE(NULLIF(ten_hang, N''), N'Chưa nhập tên hàng') AS ten_hang,
                      quy_cach, don_vi_tinh, COUNT(*) AS so_mat_hang, {AggregateColumns}
               FROM typed
               WHERE loai IN (N'Xuat', N'Nhap')
               GROUP BY COALESCE(NULLIF(doi_tac, N''), N'Chưa nhập đối tác'),
                        COALESCE(NULLIF(ten_hang, N''), N'Chưa nhập tên hàng'), quy_cach, don_vi_tinh
               ORDER BY doi_tac, ten_hang, quy_cach", doiTac, from, to).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var s = ReadStats(r);
                items.Add(new GiaCongReportItemDto(r.Str("doi_tac"), r.Str("ten_hang"), r.Str("quy_cach"),
                    r.Str("don_vi_tinh"), s.SoLuongXuat, s.SoLuongNhap, s.SoLuongConTaiCongTy,
                    s.TienGiaCongPhaiTra));
            }
        }

        return new GiaCongReportDto(total.SoLuongXuat, total.SoLuongNhap, total.SoLuongConTaiCongTy,
            total.TienGiaCongPhaiTra, partners, items);
    }

    private static string BuildReportWhere(string? doiTac, DateOnly? from, DateOnly? to)
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(doiTac)) filters.Add("p.doi_tac LIKE @doiTac");
        if (from is not null) filters.Add("p.ngay_lap >= @from");
        if (to is not null) filters.Add("p.ngay_lap <= @to");
        return filters.Count == 0 ? "" : " AND " + string.Join(" AND ", filters);
    }

    private static SqlCommand CreateReportCommand(SqlConnection conn, string sql, string? doiTac, DateOnly? from, DateOnly? to)
    {
        var cmd = conn.Cmd(sql);
        if (!string.IsNullOrWhiteSpace(doiTac)) cmd.With("@doiTac", $"%{doiTac.Trim()}%");
        if (from is not null) cmd.With("@from", from.Value.ToDateTime(TimeOnly.MinValue));
        if (to is not null) cmd.With("@to", to.Value.ToDateTime(TimeOnly.MinValue));
        return cmd;
    }

    private static GiaCongStats ReadStats(SqlDataReader r) => new(
        r.Int("so_mat_hang"),
        r.Dec("so_luong_xuat"),
        r.Dec("so_luong_nhap"),
        r.Dec("so_luong_con_tai_cong_ty"),
        r.Dec("tien_gia_cong_phai_tra"));

    private static async Task<IResult> Save(Database db, ClaimsPrincipal u, long? id, SaveGiaCongRequest req)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            long phieuId;
            var loaiPhieu = NormalizePhieuType(req.LoaiPhieu);
            var ngay = req.NgayLap.ToDateTime(TimeOnly.MinValue);
            object han = req.HanHoanThanh is { } h ? h.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

            if (id is null)
            {
                var maxId = Convert.ToInt64(await new SqlCommand("SELECT ISNULL(MAX(id),0) FROM gia_cong_phieu", conn, tx).ExecuteScalarAsync());
                var maPhieu = $"GC{(maxId + 1):D6}";
                var cmd = new SqlCommand(
                    @"INSERT INTO gia_cong_phieu (ma_phieu, loai_phieu, doi_tac, nhan_vien, ngay_lap, han_hoan_thanh, ghi_chu, updated_at)
                      OUTPUT INSERTED.id
                      VALUES (@mp, @lp, @dt, @nv, @ng, @han, @gc, GETDATE())", conn, tx);
                cmd.Parameters.AddWithValue("@mp", maPhieu);
                FillPhieu(cmd, req, loaiPhieu, ngay, han);
                phieuId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }
            else
            {
                phieuId = id.Value;
                var cmd = new SqlCommand(
                    @"UPDATE gia_cong_phieu SET loai_phieu=@lp, doi_tac=@dt, nhan_vien=@nv, ngay_lap=@ng,
                        han_hoan_thanh=@han, ghi_chu=@gc, updated_at=GETDATE()
                      WHERE id=@id", conn, tx);
                cmd.Parameters.AddWithValue("@id", phieuId);
                FillPhieu(cmd, req, loaiPhieu, ngay, han);
                if (await cmd.ExecuteNonQueryAsync() == 0) { await tx.RollbackAsync(); return Results.NotFound(); }
                await new SqlCommand("DELETE FROM gia_cong_hang_hoa WHERE phieu_id=@id", conn, tx)
                    { Parameters = { new("@id", phieuId) } }.ExecuteNonQueryAsync();
            }

            foreach (var line in req.Lines ?? new())
            {
                var donGia = IsNhap(loaiPhieu) ? line.DonGiaGiaCong : 0m;
                var lc = new SqlCommand(
                    @"INSERT INTO gia_cong_hang_hoa (phieu_id, loai_dong, ma_hang, ten_hang, quy_cach, don_vi_tinh, so_luong, don_gia_gia_cong, ghi_chu)
                      VALUES (@p, @ld, @mh, @th, @qc, @dv, @sl, @dg, @gc)", conn, tx);
                lc.Parameters.AddWithValue("@p", phieuId);
                lc.Parameters.AddWithValue("@ld", loaiPhieu);
                lc.Parameters.AddWithValue("@mh", line.MaHang ?? "");
                lc.Parameters.AddWithValue("@th", line.TenHang ?? "");
                lc.Parameters.AddWithValue("@qc", line.QuyCach ?? "");
                lc.Parameters.AddWithValue("@dv", line.DonViTinh ?? "");
                lc.Parameters.AddWithValue("@sl", line.SoLuong);
                lc.Parameters.AddWithValue("@dg", donGia);
                lc.Parameters.AddWithValue("@gc", line.GhiChu ?? "");
                await lc.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            await db.RecordAudit(u.Username(), id is null ? "Tạo phiếu gia công" : "Cập nhật phiếu gia công",
                "GiaCong", phieuId.ToString(), $"{(id is null ? "Tạo" : "Cập nhật")} phiếu gia công (web).");
            return Results.Ok(new { id = phieuId });
        }
        catch (SqlException ex)
        {
            await tx.RollbackAsync();
            return Results.Json(new { message = "Lỗi lưu phiếu gia công: " + ex.Message }, statusCode: 400);
        }
    }

    private static void FillPhieu(SqlCommand cmd, SaveGiaCongRequest req, string loaiPhieu, DateTime ngay, object han)
    {
        cmd.Parameters.AddWithValue("@lp", loaiPhieu);
        cmd.Parameters.AddWithValue("@dt", req.DoiTac ?? "");
        cmd.Parameters.AddWithValue("@nv", req.NhanVienPhuTrach ?? "");
        cmd.Parameters.AddWithValue("@ng", ngay);
        cmd.Parameters.AddWithValue("@han", han);
        cmd.Parameters.AddWithValue("@gc", req.GhiChu ?? "");
    }

    private static string NormalizePhieuType(string? type)
    {
        type = (type ?? "").Trim();
        if (type.Length == 0) return LoaiXuat;
        if (Contains(type, "Nhập")) return LoaiNhap;
        if (Contains(type, "Xuất")) return LoaiXuat;
        return LoaiXuat;
    }

    private static bool IsNhap(string type) => Contains(type, "Nhập");

    private static bool Contains(string source, string value) =>
        source.IndexOf(value, StringComparison.CurrentCultureIgnoreCase) >= 0;

    private sealed record GiaCongStats(int Count, decimal SoLuongXuat, decimal SoLuongNhap,
        decimal SoLuongConTaiCongTy, decimal TienGiaCongPhaiTra)
    {
        public static GiaCongStats Empty { get; } = new(0, 0, 0, 0, 0);
        public decimal TongGiaTri => TienGiaCongPhaiTra;
    }
}
