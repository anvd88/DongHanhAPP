using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using Microsoft.Data.SqlClient;

namespace KetoanMini.Api.Endpoints;

public static class GiaCongEndpoints
{
    public static async Task EnsureTables(Database db)
    {
        await using var conn = await db.OpenAsync();
        await conn.Cmd(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='gia_cong_phieu' AND xtype='U')
            CREATE TABLE gia_cong_phieu (
                id BIGINT IDENTITY(1,1) PRIMARY KEY, ma_phieu NVARCHAR(20) NOT NULL,
                loai_phieu NVARCHAR(50) NOT NULL, doi_tac NVARCHAR(200) NOT NULL DEFAULT '',
                nhan_vien NVARCHAR(200) NOT NULL DEFAULT '', ngay_lap DATE NOT NULL,
                han_hoan_thanh DATE NULL, trang_thai NVARCHAR(50) NOT NULL DEFAULT N'Đang xử lý',
                tien_do INT NOT NULL DEFAULT 0, buoc_hien_tai INT NOT NULL DEFAULT 1,
                ghi_chu NVARCHAR(1000) NOT NULL DEFAULT '',
                created_at DATETIME2 NOT NULL DEFAULT GETDATE(), updated_at DATETIME2 NOT NULL DEFAULT GETDATE());").ExecuteNonQueryAsync();
        await conn.Cmd(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='gia_cong_hang_hoa' AND xtype='U')
            CREATE TABLE gia_cong_hang_hoa (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                phieu_id BIGINT NOT NULL REFERENCES gia_cong_phieu(id) ON DELETE CASCADE,
                loai_dong NVARCHAR(50) NOT NULL DEFAULT N'Nguyên liệu', ma_hang NVARCHAR(50) NOT NULL DEFAULT '',
                ten_hang NVARCHAR(200) NOT NULL DEFAULT '', quy_cach NVARCHAR(200) NOT NULL DEFAULT '',
                don_vi_tinh NVARCHAR(30) NOT NULL DEFAULT '',
                so_luong DECIMAL(18,2) NOT NULL DEFAULT 0, don_gia_gia_cong DECIMAL(18,2) NOT NULL DEFAULT 0,
                ghi_chu NVARCHAR(500) NOT NULL DEFAULT '', trang_thai_dong NVARCHAR(50) NOT NULL DEFAULT N'Chờ');").ExecuteNonQueryAsync();
        // Bổ sung cột quy_cach cho CSDL đã tạo từ trước (idempotent).
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
            var stats = new Dictionary<long, (int c, decimal t)>();
            await using (var r = await conn.Cmd(
                @"SELECT phieu_id, COUNT(*) c, ISNULL(SUM(so_luong*don_gia_gia_cong),0) t
                  FROM gia_cong_hang_hoa GROUP BY phieu_id").ExecuteReaderAsync())
                while (await r.ReadAsync()) stats[r.GetInt64(0)] = (r.GetInt32(1), r.GetDecimal(2));

            var where = filter switch
            {
                "nhap" => "WHERE loai_phieu LIKE N'%Nhập%'",
                "xuat" => "WHERE loai_phieu LIKE N'%Xuất%'",
                "dangxuly" => "WHERE trang_thai = N'Đang xử lý'",
                _ => "WHERE 1=1"
            };
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND (ma_phieu LIKE @s OR doi_tac LIKE @s OR nhan_vien LIKE @s)";

            var list = new List<GiaCongListItemDto>();
            var cmd = conn.Cmd(
                $@"SELECT id, ma_phieu, loai_phieu, doi_tac, nhan_vien, ngay_lap, han_hoan_thanh,
                          trang_thai, tien_do, buoc_hien_tai FROM gia_cong_phieu {where} ORDER BY id DESC");
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@s", $"%{search}%");
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                {
                    var id = r.Long("id");
                    var s = stats.GetValueOrDefault(id);
                    list.Add(new GiaCongListItemDto(id, r.Str("ma_phieu"), r.Str("loai_phieu"), r.Str("doi_tac"),
                        r.Str("nhan_vien"), r.DateOnly("ngay_lap"),
                        r.IsDBNull(r.GetOrdinal("han_hoan_thanh")) ? null : r.DateOnly("han_hoan_thanh"),
                        r.Str("trang_thai"), r.Int("tien_do"), r.Int("buoc_hien_tai"), s.c, s.t));
                }
            return Results.Ok(list);
        });

        g.MapGet("/{id:long}", async (long id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            GiaCongDetailDto? p = null;
            await using (var r = await conn.Cmd(
                @"SELECT id, ma_phieu, loai_phieu, doi_tac, nhan_vien, ngay_lap, han_hoan_thanh,
                         trang_thai, tien_do, buoc_hien_tai, ghi_chu FROM gia_cong_phieu WHERE id=@id")
                .With("@id", id).ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                    p = new GiaCongDetailDto(r.Long("id"), r.Str("ma_phieu"), r.Str("loai_phieu"), r.Str("doi_tac"),
                        r.Str("nhan_vien"), r.DateOnly("ngay_lap"),
                        r.IsDBNull(r.GetOrdinal("han_hoan_thanh")) ? null : r.DateOnly("han_hoan_thanh"),
                        r.Str("trang_thai"), r.Int("tien_do"), r.Int("buoc_hien_tai"), r.Str("ghi_chu"), new());
            }
            if (p is null) return Results.NotFound();
            await using (var r = await conn.Cmd(
                @"SELECT id, loai_dong, ma_hang, ten_hang, quy_cach, don_vi_tinh, so_luong, don_gia_gia_cong, trang_thai_dong, ghi_chu
                  FROM gia_cong_hang_hoa WHERE phieu_id=@id ORDER BY id").With("@id", id).ExecuteReaderAsync())
                while (await r.ReadAsync())
                    p.Lines.Add(new GiaCongLineDto(r.Long("id"), r.Str("loai_dong"), r.Str("ma_hang"), r.Str("ten_hang"),
                        r.Str("quy_cach"), r.Str("don_vi_tinh"), r.Dec("so_luong"), r.Dec("don_gia_gia_cong"), r.Str("trang_thai_dong"), r.Str("ghi_chu")));
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

    private static async Task<IResult> Save(Database db, ClaimsPrincipal u, long? id, SaveGiaCongRequest req)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            long phieuId;
            var ngay = req.NgayLap.ToDateTime(TimeOnly.MinValue);
            object han = req.HanHoanThanh is { } h ? h.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

            if (id is null)
            {
                var maxId = Convert.ToInt64(await new SqlCommand("SELECT ISNULL(MAX(id),0) FROM gia_cong_phieu", conn, tx).ExecuteScalarAsync());
                var maPhieu = $"GC{(maxId + 1):D6}";
                var cmd = new SqlCommand(
                    @"INSERT INTO gia_cong_phieu (ma_phieu, loai_phieu, doi_tac, nhan_vien, ngay_lap, han_hoan_thanh, trang_thai, tien_do, buoc_hien_tai, ghi_chu, updated_at)
                      OUTPUT INSERTED.id
                      VALUES (@mp, @lp, @dt, @nv, @ng, @han, @tt, @td, @bh, @gc, GETDATE())", conn, tx);
                cmd.Parameters.AddWithValue("@mp", maPhieu);
                FillPhieu(cmd, req, ngay, han);
                phieuId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }
            else
            {
                phieuId = id.Value;
                var cmd = new SqlCommand(
                    @"UPDATE gia_cong_phieu SET loai_phieu=@lp, doi_tac=@dt, nhan_vien=@nv, ngay_lap=@ng,
                        han_hoan_thanh=@han, trang_thai=@tt, tien_do=@td, buoc_hien_tai=@bh, ghi_chu=@gc, updated_at=GETDATE()
                      WHERE id=@id", conn, tx);
                cmd.Parameters.AddWithValue("@id", phieuId);
                FillPhieu(cmd, req, ngay, han);
                if (await cmd.ExecuteNonQueryAsync() == 0) { await tx.RollbackAsync(); return Results.NotFound(); }
                await new SqlCommand("DELETE FROM gia_cong_hang_hoa WHERE phieu_id=@id", conn, tx)
                    { Parameters = { new("@id", phieuId) } }.ExecuteNonQueryAsync();
            }

            foreach (var line in req.Lines ?? new())
            {
                var lc = new SqlCommand(
                    @"INSERT INTO gia_cong_hang_hoa (phieu_id, loai_dong, ma_hang, ten_hang, quy_cach, don_vi_tinh, so_luong, don_gia_gia_cong, ghi_chu, trang_thai_dong)
                      VALUES (@p, @ld, @mh, @th, @qc, @dv, @sl, @dg, @gc, @ttd)", conn, tx);
                lc.Parameters.AddWithValue("@p", phieuId);
                lc.Parameters.AddWithValue("@ld", line.LoaiDong ?? "Nguyên liệu");
                lc.Parameters.AddWithValue("@mh", line.MaHang ?? "");
                lc.Parameters.AddWithValue("@th", line.TenHang ?? "");
                lc.Parameters.AddWithValue("@qc", line.QuyCach ?? "");
                lc.Parameters.AddWithValue("@dv", line.DonViTinh ?? "");
                lc.Parameters.AddWithValue("@sl", line.SoLuong);
                lc.Parameters.AddWithValue("@dg", line.DonGiaGiaCong);
                lc.Parameters.AddWithValue("@gc", line.GhiChu ?? "");
                lc.Parameters.AddWithValue("@ttd", line.TrangThaiDong ?? "Chờ");
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

    private static void FillPhieu(SqlCommand cmd, SaveGiaCongRequest req, DateTime ngay, object han)
    {
        cmd.Parameters.AddWithValue("@lp", req.LoaiPhieu ?? "Xuất gia công");
        cmd.Parameters.AddWithValue("@dt", req.DoiTac ?? "");
        cmd.Parameters.AddWithValue("@nv", req.NhanVienPhuTrach ?? "");
        cmd.Parameters.AddWithValue("@ng", ngay);
        cmd.Parameters.AddWithValue("@han", han);
        cmd.Parameters.AddWithValue("@tt", req.TrangThai ?? "Đang xử lý");
        cmd.Parameters.AddWithValue("@td", req.TienDo);
        cmd.Parameters.AddWithValue("@bh", req.BuocHienTai <= 0 ? 1 : req.BuocHienTai);
        cmd.Parameters.AddWithValue("@gc", req.GhiChu ?? "");
    }
}
