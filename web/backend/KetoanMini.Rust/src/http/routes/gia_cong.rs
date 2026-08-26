//! Native subcontracting (`Gia cong`) endpoints, ported from `GiaCongEndpoints.cs`.
//!
//! Mount this router behind `auth::require_auth`. Every route also enforces the
//! original `accounting.access` policy. PostgreSQL statement triggers installed
//! by the compatibility host remain the single realtime publisher for the
//! `gia_cong_*` tables, so native writes produce the same `data` refresh scope.

use crate::{
    auth::{AuthContext, permissions},
    state::AppState,
};
use axum::{
    Extension, Json, Router,
    body::Body,
    extract::{
        DefaultBodyLimit, Path, Query, State,
        rejection::{JsonRejection, QueryRejection},
    },
    http::{HeaderValue, Request, StatusCode, header},
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::get,
};
use chrono::NaiveDate;
use serde::{Deserialize, Serialize};
use serde_json::{Number, json};
use sqlx::{AssertSqlSafe, FromRow, PgPool, Postgres, Transaction};
use std::{collections::HashMap, sync::Arc};

const ROOT_PATH: &str = "/api/giacong";
const ROOT_SLASH_PATH: &str = "/api/giacong/";
const REPORT_PATH: &str = "/api/giacong/report";
const BY_ID_PATH: &str = "/api/giacong/{id}";

const LOAI_XUAT: &str = "Xuất gia công";
const LOAI_NHAP: &str = "Nhập gia công";
const MAX_JSON_BODY_BYTES: usize = 16 * 1024 * 1024;
const PAYLOAD_TOO_LARGE_MESSAGE: &str = "Payload vượt giới hạn 16777216 byte.";
const DATABASE_UNAVAILABLE_MESSAGE: &str = "Khong ket noi duoc co so du lieu PostgreSQL.";
const SAVE_INVALID_MESSAGE: &str =
    "Không lưu được phiếu gia công (dữ liệu không hợp lệ hoặc trùng lặp).";
const AUDIT_CREATE: &str = "Tạo phiếu gia công";
const AUDIT_UPDATE: &str = "Cập nhật phiếu gia công";
const AUDIT_DELETE: &str = "Xóa phiếu gia công";
const AUDIT_CREATE_DETAILS: &str = "Tạo phiếu gia công (web).";
const AUDIT_UPDATE_DETAILS: &str = "Cập nhật phiếu gia công (web).";
const AUDIT_DELETE_DETAILS: &str = "Xóa phiếu gia công (web).";

const AUDIT_SQL: &str = r#"
    INSERT INTO audit_logs (occurred_at, username, action, entity, entity_name, details)
    VALUES (CURRENT_TIMESTAMP, $1, $2, 'GiaCong', $3, $4)
"#;

// Keep the original predicate deliberately unparenthesized. Appending report
// filters therefore has the same PostgreSQL operator precedence as the .NET
// implementation; changing it here would silently change existing reports.
const TYPED_ROWS_SELECT: &str = r#"
    SELECT
        p.id AS phieu_id, p.doi_tac, h.ten_hang, h.quy_cach, h.don_vi_tinh,
        h.so_luong, h.don_gia_gia_cong,
        CASE
            WHEN h.loai_dong ILIKE '%Nhập%' OR p.loai_phieu ILIKE '%Nhập%' THEN 'Nhap'
            WHEN h.loai_dong ILIKE '%Xuất%' OR p.loai_phieu ILIKE '%Xuất%' THEN 'Xuat'
            ELSE ''
        END AS loai
    FROM gia_cong_phieu p
    JOIN gia_cong_hang_hoa h ON h.phieu_id = p.id
    WHERE p.loai_phieu ILIKE '%Xuất%' OR p.loai_phieu ILIKE '%Nhập%'
       OR h.loai_dong ILIKE '%Xuất%' OR h.loai_dong ILIKE '%Nhập%'
"#;

const STATS_BY_PHIEU_SQL: &str = r#"
    WITH typed AS (
        SELECT
            p.id AS phieu_id, h.so_luong, h.don_gia_gia_cong,
            CASE
                WHEN h.loai_dong ILIKE '%Nhập%' OR p.loai_phieu ILIKE '%Nhập%' THEN 'Nhap'
                WHEN h.loai_dong ILIKE '%Xuất%' OR p.loai_phieu ILIKE '%Xuất%' THEN 'Xuat'
                ELSE ''
            END AS loai
        FROM gia_cong_phieu p
        JOIN gia_cong_hang_hoa h ON h.phieu_id = p.id
        WHERE p.loai_phieu ILIKE '%Xuất%' OR p.loai_phieu ILIKE '%Nhập%'
           OR h.loai_dong ILIKE '%Xuất%' OR h.loai_dong ILIKE '%Nhập%'
    )
    SELECT
        phieu_id,
        COUNT(*)::integer AS so_mat_hang,
        COALESCE(SUM(CASE WHEN loai = 'Xuat' THEN so_luong ELSE 0 END), 0)::text
            AS so_luong_xuat,
        COALESCE(SUM(CASE WHEN loai = 'Nhap' THEN so_luong ELSE 0 END), 0)::text
            AS so_luong_nhap,
        COALESCE(CASE
            WHEN SUM(CASE WHEN loai = 'Xuat' THEN so_luong ELSE 0 END)
                - SUM(CASE WHEN loai = 'Nhap' THEN so_luong ELSE 0 END) < 0 THEN 0
            ELSE SUM(CASE WHEN loai = 'Xuat' THEN so_luong ELSE 0 END)
                - SUM(CASE WHEN loai = 'Nhap' THEN so_luong ELSE 0 END)
        END, 0)::text AS so_luong_con_tai_cong_ty,
        COALESCE(SUM(CASE
            WHEN loai = 'Nhap' THEN so_luong * don_gia_gia_cong ELSE 0
        END), 0)::text AS tien_gia_cong_phai_tra
    FROM typed
    WHERE loai IN ('Xuat', 'Nhap')
    GROUP BY phieu_id
"#;

const LIST_SQL: &str = r#"
    SELECT p.id, p.ma_phieu, p.loai_phieu, p.doi_tac, p.nhan_vien,
           p.ngay_lap, p.han_hoan_thanh
    FROM gia_cong_phieu p
    WHERE (
        ($1 = 'nhap' AND p.loai_phieu ILIKE '%Nhập%')
        OR ($1 = 'xuat' AND p.loai_phieu ILIKE '%Xuất%')
        OR ($1 NOT IN ('nhap', 'xuat')
            AND (p.loai_phieu ILIKE '%Xuất%' OR p.loai_phieu ILIKE '%Nhập%'))
    )
      AND ($2::text IS NULL
           OR p.ma_phieu ILIKE $2 OR p.doi_tac ILIKE $2 OR p.nhan_vien ILIKE $2)
    ORDER BY p.id DESC
"#;

const DETAIL_SQL: &str = r#"
    SELECT id, ma_phieu, loai_phieu, doi_tac, nhan_vien, ngay_lap,
           han_hoan_thanh, ghi_chu
    FROM gia_cong_phieu
    WHERE id = $1
      AND (loai_phieu ILIKE '%Xuất%' OR loai_phieu ILIKE '%Nhập%')
"#;

const DETAIL_LINES_SQL: &str = r#"
    SELECT id, ma_hang, ten_hang, quy_cach, don_vi_tinh,
           so_luong::text AS so_luong,
           don_gia_gia_cong::text AS don_gia_gia_cong,
           (so_luong * don_gia_gia_cong)::text AS thanh_tien,
           ghi_chu
    FROM gia_cong_hang_hoa
    WHERE phieu_id = $1
    ORDER BY id
"#;

#[cfg(test)]
const ROUTE_CONTRACTS: &[(&str, &str)] = &[
    ("GET", "/api/giacong/"),
    ("GET", "/api/giacong/report"),
    ("GET", "/api/giacong/{id:long}"),
    ("POST", "/api/giacong/"),
    ("PUT", "/api/giacong/{id:long}"),
    ("DELETE", "/api/giacong/{id:long}"),
];

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        // ASP.NET routing treats the terminal slash as optional. Keep both
        // forms because the current web client uses one for GET and the other
        // for POST.
        .route(ROOT_PATH, get(list_phieu).post(create_phieu))
        .route(ROOT_SLASH_PATH, get(list_phieu).post(create_phieu))
        .route(REPORT_PATH, get(report))
        .route(
            BY_ID_PATH,
            get(get_phieu).put(update_phieu).delete(delete_phieu),
        )
        .route_layer(middleware::from_fn(require_accounting_access))
        .layer(DefaultBodyLimit::max(MAX_JSON_BODY_BYTES))
}

async fn require_accounting_access(request: Request<Body>, next: Next) -> Response {
    let Some(auth) = request.extensions().get::<AuthContext>() else {
        return StatusCode::UNAUTHORIZED.into_response();
    };
    if !may_access(&auth.permissions) {
        return StatusCode::FORBIDDEN.into_response();
    }
    next.run(request).await
}

fn may_access(permission_set: &std::collections::BTreeSet<String>) -> bool {
    permission_set.contains(permissions::ACCOUNTING_ACCESS)
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum ListFilter {
    Nhap,
    Xuat,
    All,
}

impl ListFilter {
    const fn sql_key(self) -> &'static str {
        match self {
            Self::Nhap => "nhap",
            Self::Xuat => "xuat",
            Self::All => "all",
        }
    }
}

fn normalize_list_filter(value: Option<&str>) -> ListFilter {
    match value {
        Some("nhap") => ListFilter::Nhap,
        Some("xuat") => ListFilter::Xuat,
        _ => ListFilter::All,
    }
}

#[derive(Debug, Default, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct ListQuery {
    #[serde(alias = "Filter")]
    filter: Option<String>,
    #[serde(alias = "Search")]
    search: Option<String>,
}

#[derive(Debug, Default, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct ReportQuery {
    #[serde(alias = "DoiTac")]
    doi_tac: Option<String>,
    #[serde(alias = "From")]
    from: Option<NaiveDate>,
    #[serde(alias = "To")]
    to: Option<NaiveDate>,
}

#[derive(Clone, Debug, Eq, PartialEq)]
struct DecimalText(String);

impl DecimalText {
    fn from_database(value: Option<String>) -> Self {
        let value = value.unwrap_or_else(|| "0".to_owned());
        // PostgreSQL numeric text is already a valid JSON number. Refuse its
        // special NaN/Infinity spellings rather than emitting malformed JSON.
        if serde_json::from_str::<Number>(&value).is_ok() {
            Self(value)
        } else {
            tracing::warn!(value, "invalid PostgreSQL numeric text in GiaCong response");
            Self("0".to_owned())
        }
    }

    fn zero() -> Self {
        Self("0".to_owned())
    }

    fn as_json(&self) -> &str {
        &self.0
    }
}

#[derive(Debug, FromRow)]
struct StatsRow {
    phieu_id: i64,
    so_mat_hang: i32,
    so_luong_xuat: Option<String>,
    so_luong_nhap: Option<String>,
    so_luong_con_tai_cong_ty: Option<String>,
    tien_gia_cong_phai_tra: Option<String>,
}

#[derive(Clone, Debug, Eq, PartialEq)]
struct GiaCongStats {
    count: i32,
    so_luong_xuat: DecimalText,
    so_luong_nhap: DecimalText,
    so_luong_con_tai_cong_ty: DecimalText,
    tien_gia_cong_phai_tra: DecimalText,
}

impl GiaCongStats {
    fn empty() -> Self {
        Self {
            count: 0,
            so_luong_xuat: DecimalText::zero(),
            so_luong_nhap: DecimalText::zero(),
            so_luong_con_tai_cong_ty: DecimalText::zero(),
            tien_gia_cong_phai_tra: DecimalText::zero(),
        }
    }
}

impl From<StatsRow> for GiaCongStats {
    fn from(row: StatsRow) -> Self {
        Self {
            count: row.so_mat_hang,
            so_luong_xuat: DecimalText::from_database(row.so_luong_xuat),
            so_luong_nhap: DecimalText::from_database(row.so_luong_nhap),
            so_luong_con_tai_cong_ty: DecimalText::from_database(row.so_luong_con_tai_cong_ty),
            tien_gia_cong_phai_tra: DecimalText::from_database(row.tien_gia_cong_phai_tra),
        }
    }
}

#[derive(Debug, FromRow)]
struct ListRow {
    id: i64,
    ma_phieu: Option<String>,
    loai_phieu: Option<String>,
    doi_tac: Option<String>,
    nhan_vien: Option<String>,
    ngay_lap: Option<NaiveDate>,
    han_hoan_thanh: Option<NaiveDate>,
}

#[derive(Debug, Eq, PartialEq)]
struct ListDto {
    id: i64,
    ma_phieu: String,
    loai_phieu: String,
    doi_tac: String,
    nhan_vien_phu_trach: String,
    ngay_lap: NaiveDate,
    han_hoan_thanh: Option<NaiveDate>,
    stats: GiaCongStats,
}

#[derive(Debug, FromRow)]
struct DetailRow {
    id: i64,
    ma_phieu: Option<String>,
    loai_phieu: Option<String>,
    doi_tac: Option<String>,
    nhan_vien: Option<String>,
    ngay_lap: Option<NaiveDate>,
    han_hoan_thanh: Option<NaiveDate>,
    ghi_chu: Option<String>,
}

#[derive(Debug, FromRow)]
struct DetailLineRow {
    id: i64,
    ma_hang: Option<String>,
    ten_hang: Option<String>,
    quy_cach: Option<String>,
    don_vi_tinh: Option<String>,
    so_luong: Option<String>,
    don_gia_gia_cong: Option<String>,
    thanh_tien: Option<String>,
    ghi_chu: Option<String>,
}

#[derive(Debug, Eq, PartialEq)]
struct LineDto {
    id: i64,
    // Detail intentionally exposes the header type, not the stored loai_dong,
    // because that is what the existing C# constructor does.
    loai_dong: String,
    ma_hang: String,
    ten_hang: String,
    quy_cach: String,
    don_vi_tinh: String,
    so_luong: DecimalText,
    don_gia_gia_cong: DecimalText,
    ghi_chu: String,
    thanh_tien: DecimalText,
}

#[derive(Debug, Eq, PartialEq)]
struct DetailDto {
    id: i64,
    ma_phieu: String,
    loai_phieu: String,
    doi_tac: String,
    nhan_vien_phu_trach: String,
    ngay_lap: NaiveDate,
    han_hoan_thanh: Option<NaiveDate>,
    ghi_chu: String,
    lines: Vec<LineDto>,
}

#[derive(Debug, FromRow)]
struct AggregateRow {
    so_mat_hang: i32,
    so_luong_xuat: Option<String>,
    so_luong_nhap: Option<String>,
    so_luong_con_tai_cong_ty: Option<String>,
    tien_gia_cong_phai_tra: Option<String>,
}

impl From<AggregateRow> for GiaCongStats {
    fn from(row: AggregateRow) -> Self {
        Self {
            count: row.so_mat_hang,
            so_luong_xuat: DecimalText::from_database(row.so_luong_xuat),
            so_luong_nhap: DecimalText::from_database(row.so_luong_nhap),
            so_luong_con_tai_cong_ty: DecimalText::from_database(row.so_luong_con_tai_cong_ty),
            tien_gia_cong_phai_tra: DecimalText::from_database(row.tien_gia_cong_phai_tra),
        }
    }
}

#[derive(Debug, FromRow)]
struct ReportPartnerRow {
    doi_tac: Option<String>,
    so_mat_hang: i32,
    so_luong_xuat: Option<String>,
    so_luong_nhap: Option<String>,
    so_luong_con_tai_cong_ty: Option<String>,
    tien_gia_cong_phai_tra: Option<String>,
}

#[derive(Debug, Eq, PartialEq)]
struct ReportPartnerDto {
    doi_tac: String,
    stats: GiaCongStats,
}

#[derive(Debug, FromRow)]
struct ReportItemRow {
    doi_tac: Option<String>,
    ten_hang: Option<String>,
    quy_cach: Option<String>,
    don_vi_tinh: Option<String>,
    so_mat_hang: i32,
    so_luong_xuat: Option<String>,
    so_luong_nhap: Option<String>,
    so_luong_con_tai_cong_ty: Option<String>,
    tien_gia_cong_phai_tra: Option<String>,
}

#[derive(Debug, Eq, PartialEq)]
struct ReportItemDto {
    doi_tac: String,
    ten_hang: String,
    quy_cach: String,
    don_vi_tinh: String,
    stats: GiaCongStats,
}

#[derive(Debug, Eq, PartialEq)]
struct ReportDto {
    total: GiaCongStats,
    partners: Vec<ReportPartnerDto>,
    items: Vec<ReportItemDto>,
}

/// Tiny JSON writer used only to preserve PostgreSQL/.NET decimal scale. The
/// current Cargo feature set intentionally has no arbitrary-precision decimal
/// serializer; converting numeric(18,2) through f64 would lose large values
/// and turn `1.20` into a different wire representation.
struct JsonObjectWriter {
    value: String,
    first: bool,
}

impl JsonObjectWriter {
    fn new() -> Self {
        Self {
            value: "{".to_owned(),
            first: true,
        }
    }

    fn field_prefix(&mut self, name: &str) {
        if !self.first {
            self.value.push(',');
        }
        self.first = false;
        self.value
            .push_str(&serde_json::to_string(name).expect("JSON key serialization cannot fail"));
        self.value.push(':');
    }

    fn string(&mut self, name: &str, value: &str) {
        self.field_prefix(name);
        self.value.push_str(
            &serde_json::to_string(value).expect("JSON string serialization cannot fail"),
        );
    }

    fn i64(&mut self, name: &str, value: i64) {
        self.raw(name, &value.to_string());
    }

    fn i32(&mut self, name: &str, value: i32) {
        self.raw(name, &value.to_string());
    }

    fn decimal(&mut self, name: &str, value: &DecimalText) {
        self.raw(name, value.as_json());
    }

    fn raw(&mut self, name: &str, value: &str) {
        self.field_prefix(name);
        self.value.push_str(value);
    }

    fn finish(mut self) -> String {
        self.value.push('}');
        self.value
    }
}

fn json_array<T>(values: &[T], convert: impl Fn(&T) -> String) -> String {
    let mut output = String::from("[");
    for (index, value) in values.iter().enumerate() {
        if index > 0 {
            output.push(',');
        }
        output.push_str(&convert(value));
    }
    output.push(']');
    output
}

fn date_wire(value: NaiveDate) -> String {
    value.format("%Y-%m-%d").to_string()
}

impl ListDto {
    fn wire_json(&self) -> String {
        let mut object = JsonObjectWriter::new();
        object.i64("id", self.id);
        object.string("maPhieu", &self.ma_phieu);
        object.string("loaiPhieu", &self.loai_phieu);
        object.string("doiTac", &self.doi_tac);
        object.string("nhanVienPhuTrach", &self.nhan_vien_phu_trach);
        object.string("ngayLap", &date_wire(self.ngay_lap));
        if let Some(value) = self.han_hoan_thanh {
            object.string("hanHoanThanh", &date_wire(value));
        }
        object.i32("soMatHang", self.stats.count);
        object.decimal("tongGiaTri", &self.stats.tien_gia_cong_phai_tra);
        object.decimal("soLuongXuat", &self.stats.so_luong_xuat);
        object.decimal("soLuongNhap", &self.stats.so_luong_nhap);
        object.decimal("soLuongConTaiCongTy", &self.stats.so_luong_con_tai_cong_ty);
        object.decimal("tienGiaCongPhaiTra", &self.stats.tien_gia_cong_phai_tra);
        object.finish()
    }
}

impl LineDto {
    fn wire_json(&self) -> String {
        let mut object = JsonObjectWriter::new();
        object.i64("id", self.id);
        object.string("loaiDong", &self.loai_dong);
        object.string("maHang", &self.ma_hang);
        object.string("tenHang", &self.ten_hang);
        object.string("quyCach", &self.quy_cach);
        object.string("donViTinh", &self.don_vi_tinh);
        object.decimal("soLuong", &self.so_luong);
        object.decimal("donGiaGiaCong", &self.don_gia_gia_cong);
        object.string("ghiChu", &self.ghi_chu);
        object.decimal("thanhTien", &self.thanh_tien);
        object.finish()
    }
}

impl DetailDto {
    fn wire_json(&self) -> String {
        let mut object = JsonObjectWriter::new();
        object.i64("id", self.id);
        object.string("maPhieu", &self.ma_phieu);
        object.string("loaiPhieu", &self.loai_phieu);
        object.string("doiTac", &self.doi_tac);
        object.string("nhanVienPhuTrach", &self.nhan_vien_phu_trach);
        object.string("ngayLap", &date_wire(self.ngay_lap));
        if let Some(value) = self.han_hoan_thanh {
            object.string("hanHoanThanh", &date_wire(value));
        }
        object.string("ghiChu", &self.ghi_chu);
        object.raw("lines", &json_array(&self.lines, LineDto::wire_json));
        object.finish()
    }
}

impl ReportPartnerDto {
    fn wire_json(&self) -> String {
        let mut object = JsonObjectWriter::new();
        object.string("doiTac", &self.doi_tac);
        object.decimal("soLuongXuat", &self.stats.so_luong_xuat);
        object.decimal("soLuongNhap", &self.stats.so_luong_nhap);
        object.decimal("soLuongConTaiCongTy", &self.stats.so_luong_con_tai_cong_ty);
        object.decimal("tienGiaCongPhaiTra", &self.stats.tien_gia_cong_phai_tra);
        object.finish()
    }
}

impl ReportItemDto {
    fn wire_json(&self) -> String {
        let mut object = JsonObjectWriter::new();
        object.string("doiTac", &self.doi_tac);
        object.string("tenHang", &self.ten_hang);
        object.string("quyCach", &self.quy_cach);
        object.string("donViTinh", &self.don_vi_tinh);
        object.decimal("soLuongXuat", &self.stats.so_luong_xuat);
        object.decimal("soLuongNhap", &self.stats.so_luong_nhap);
        object.decimal("soLuongConTaiCongTy", &self.stats.so_luong_con_tai_cong_ty);
        object.decimal("tienGiaCongPhaiTra", &self.stats.tien_gia_cong_phai_tra);
        object.finish()
    }
}

impl ReportDto {
    fn wire_json(&self) -> String {
        let mut object = JsonObjectWriter::new();
        object.decimal("soLuongXuat", &self.total.so_luong_xuat);
        object.decimal("soLuongNhap", &self.total.so_luong_nhap);
        object.decimal("soLuongConTaiCongTy", &self.total.so_luong_con_tai_cong_ty);
        object.decimal("tienGiaCongPhaiTra", &self.total.tien_gia_cong_phai_tra);
        object.raw(
            "partners",
            &json_array(&self.partners, ReportPartnerDto::wire_json),
        );
        object.raw("items", &json_array(&self.items, ReportItemDto::wire_json));
        object.finish()
    }
}

fn exact_json(value: String) -> Response {
    let mut response = Response::new(Body::from(value));
    response.headers_mut().insert(
        header::CONTENT_TYPE,
        HeaderValue::from_static("application/json"),
    );
    response
}

async fn list_phieu(
    State(state): State<Arc<AppState>>,
    query: Result<Query<ListQuery>, QueryRejection>,
) -> Response {
    let Query(query) = match query {
        Ok(query) => query,
        Err(_) => return StatusCode::BAD_REQUEST.into_response(),
    };
    let filter = normalize_list_filter(query.filter.as_deref());
    let search = query
        .search
        .filter(|value| !value.trim().is_empty())
        .map(|value| format!("%{value}%"));

    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => {
            return database_failure("acquire connection to list GiaCong records", error);
        }
    };

    let stats_rows = match sqlx::query_as::<_, StatsRow>(STATS_BY_PHIEU_SQL)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read GiaCong list statistics", error),
    };
    let stats = stats_rows
        .into_iter()
        .map(|row| (row.phieu_id, GiaCongStats::from(row)))
        .collect::<HashMap<_, _>>();

    let rows = match sqlx::query_as::<_, ListRow>(LIST_SQL)
        .bind(filter.sql_key())
        .bind(search.as_deref())
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read GiaCong list", error),
    };

    let list = rows
        .into_iter()
        .map(|row| {
            let row_stats = stats
                .get(&row.id)
                .cloned()
                .unwrap_or_else(GiaCongStats::empty);
            ListDto {
                id: row.id,
                ma_phieu: row.ma_phieu.unwrap_or_default(),
                loai_phieu: row.loai_phieu.unwrap_or_default(),
                doi_tac: row.doi_tac.unwrap_or_default(),
                nhan_vien_phu_trach: row.nhan_vien.unwrap_or_default(),
                ngay_lap: row.ngay_lap.unwrap_or_else(dotnet_min_date),
                han_hoan_thanh: row.han_hoan_thanh,
                stats: row_stats,
            }
        })
        .collect::<Vec<_>>();

    exact_json(json_array(&list, ListDto::wire_json))
}

async fn get_phieu(State(state): State<Arc<AppState>>, Path(raw_id): Path<String>) -> Response {
    let Some(id) = parse_long_id(&raw_id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for GiaCong detail", error),
    };
    let row = match sqlx::query_as::<_, DetailRow>(DETAIL_SQL)
        .bind(id)
        .fetch_optional(&mut *connection)
        .await
    {
        Ok(Some(row)) => row,
        Ok(None) => return StatusCode::NOT_FOUND.into_response(),
        Err(error) => return database_failure("read GiaCong detail", error),
    };

    let loai_phieu = row.loai_phieu.unwrap_or_default();
    let line_rows = match sqlx::query_as::<_, DetailLineRow>(DETAIL_LINES_SQL)
        .bind(id)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read GiaCong detail lines", error),
    };
    let lines = line_rows
        .into_iter()
        .map(|line| LineDto {
            id: line.id,
            loai_dong: loai_phieu.clone(),
            ma_hang: line.ma_hang.unwrap_or_default(),
            ten_hang: line.ten_hang.unwrap_or_default(),
            quy_cach: line.quy_cach.unwrap_or_default(),
            don_vi_tinh: line.don_vi_tinh.unwrap_or_default(),
            so_luong: DecimalText::from_database(line.so_luong),
            don_gia_gia_cong: DecimalText::from_database(line.don_gia_gia_cong),
            ghi_chu: line.ghi_chu.unwrap_or_default(),
            thanh_tien: DecimalText::from_database(line.thanh_tien),
        })
        .collect();
    let detail = DetailDto {
        id: row.id,
        ma_phieu: row.ma_phieu.unwrap_or_default(),
        loai_phieu,
        doi_tac: row.doi_tac.unwrap_or_default(),
        nhan_vien_phu_trach: row.nhan_vien.unwrap_or_default(),
        ngay_lap: row.ngay_lap.unwrap_or_else(dotnet_min_date),
        han_hoan_thanh: row.han_hoan_thanh,
        ghi_chu: row.ghi_chu.unwrap_or_default(),
        lines,
    };
    exact_json(detail.wire_json())
}

const REPORT_FILTERS_SQL: &str = r#"
       AND ($1::text IS NULL OR p.doi_tac ILIKE $1)
       AND ($2::date IS NULL OR p.ngay_lap >= $2)
       AND ($3::date IS NULL OR p.ngay_lap <= $3)
"#;

const AGGREGATE_COLUMNS_TEXT: &str = r#"
    COALESCE(SUM(CASE WHEN loai = 'Xuat' THEN so_luong ELSE 0 END), 0)::text
        AS so_luong_xuat,
    COALESCE(SUM(CASE WHEN loai = 'Nhap' THEN so_luong ELSE 0 END), 0)::text
        AS so_luong_nhap,
    COALESCE(CASE
        WHEN SUM(CASE WHEN loai = 'Xuat' THEN so_luong ELSE 0 END)
            - SUM(CASE WHEN loai = 'Nhap' THEN so_luong ELSE 0 END) < 0 THEN 0
        ELSE SUM(CASE WHEN loai = 'Xuat' THEN so_luong ELSE 0 END)
            - SUM(CASE WHEN loai = 'Nhap' THEN so_luong ELSE 0 END)
    END, 0)::text AS so_luong_con_tai_cong_ty,
    COALESCE(SUM(CASE
        WHEN loai = 'Nhap' THEN so_luong * don_gia_gia_cong ELSE 0
    END), 0)::text AS tien_gia_cong_phai_tra
"#;

fn report_sql(select: &str) -> String {
    format!("WITH typed AS ({TYPED_ROWS_SELECT}{REPORT_FILTERS_SQL}) {select}")
}

fn report_partner_pattern(value: Option<String>) -> Option<String> {
    value
        .filter(|value| !value.trim().is_empty())
        .map(|value| format!("%{}%", value.trim()))
}

fn aggregate_from_partner(row: &ReportPartnerRow) -> GiaCongStats {
    GiaCongStats {
        count: row.so_mat_hang,
        so_luong_xuat: DecimalText::from_database(row.so_luong_xuat.clone()),
        so_luong_nhap: DecimalText::from_database(row.so_luong_nhap.clone()),
        so_luong_con_tai_cong_ty: DecimalText::from_database(row.so_luong_con_tai_cong_ty.clone()),
        tien_gia_cong_phai_tra: DecimalText::from_database(row.tien_gia_cong_phai_tra.clone()),
    }
}

fn aggregate_from_item(row: &ReportItemRow) -> GiaCongStats {
    GiaCongStats {
        count: row.so_mat_hang,
        so_luong_xuat: DecimalText::from_database(row.so_luong_xuat.clone()),
        so_luong_nhap: DecimalText::from_database(row.so_luong_nhap.clone()),
        so_luong_con_tai_cong_ty: DecimalText::from_database(row.so_luong_con_tai_cong_ty.clone()),
        tien_gia_cong_phai_tra: DecimalText::from_database(row.tien_gia_cong_phai_tra.clone()),
    }
}

async fn report(
    State(state): State<Arc<AppState>>,
    query: Result<Query<ReportQuery>, QueryRejection>,
) -> Response {
    let Query(query) = match query {
        Ok(query) => query,
        Err(_) => return StatusCode::BAD_REQUEST.into_response(),
    };
    let partner = report_partner_pattern(query.doi_tac);
    let mut connection = match state.pool.acquire().await {
        Ok(connection) => connection,
        Err(error) => return database_failure("acquire connection for GiaCong report", error),
    };

    let total_sql = report_sql(&format!(
        "SELECT COUNT(*)::integer AS so_mat_hang, {AGGREGATE_COLUMNS_TEXT} \
         FROM typed WHERE loai IN ('Xuat', 'Nhap')"
    ));
    let total = match sqlx::query_as::<_, AggregateRow>(AssertSqlSafe(total_sql))
        .bind(partner.as_deref())
        .bind(query.from)
        .bind(query.to)
        .fetch_one(&mut *connection)
        .await
    {
        Ok(row) => GiaCongStats::from(row),
        Err(error) => return database_failure("read GiaCong report totals", error),
    };

    let partners_sql = report_sql(&format!(
        r#"
        SELECT COALESCE(NULLIF(doi_tac, ''), 'Chưa nhập đối tác') AS doi_tac,
               COUNT(*)::integer AS so_mat_hang, {AGGREGATE_COLUMNS_TEXT}
        FROM typed
        WHERE loai IN ('Xuat', 'Nhap')
        GROUP BY COALESCE(NULLIF(doi_tac, ''), 'Chưa nhập đối tác')
        ORDER BY doi_tac
        "#
    ));
    let partner_rows = match sqlx::query_as::<_, ReportPartnerRow>(AssertSqlSafe(partners_sql))
        .bind(partner.as_deref())
        .bind(query.from)
        .bind(query.to)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read GiaCong report partners", error),
    };
    let partners = partner_rows
        .iter()
        .map(|row| ReportPartnerDto {
            doi_tac: row.doi_tac.clone().unwrap_or_default(),
            stats: aggregate_from_partner(row),
        })
        .collect();

    let items_sql = report_sql(&format!(
        r#"
        SELECT COALESCE(NULLIF(doi_tac, ''), 'Chưa nhập đối tác') AS doi_tac,
               COALESCE(NULLIF(ten_hang, ''), 'Chưa nhập tên hàng') AS ten_hang,
               quy_cach, don_vi_tinh, COUNT(*)::integer AS so_mat_hang,
               {AGGREGATE_COLUMNS_TEXT}
        FROM typed
        WHERE loai IN ('Xuat', 'Nhap')
        GROUP BY COALESCE(NULLIF(doi_tac, ''), 'Chưa nhập đối tác'),
                 COALESCE(NULLIF(ten_hang, ''), 'Chưa nhập tên hàng'),
                 quy_cach, don_vi_tinh
        ORDER BY doi_tac, ten_hang, quy_cach
        "#
    ));
    let item_rows = match sqlx::query_as::<_, ReportItemRow>(AssertSqlSafe(items_sql))
        .bind(partner.as_deref())
        .bind(query.from)
        .bind(query.to)
        .fetch_all(&mut *connection)
        .await
    {
        Ok(rows) => rows,
        Err(error) => return database_failure("read GiaCong report items", error),
    };
    let items = item_rows
        .iter()
        .map(|row| ReportItemDto {
            doi_tac: row.doi_tac.clone().unwrap_or_default(),
            ten_hang: row.ten_hang.clone().unwrap_or_default(),
            quy_cach: row.quy_cach.clone().unwrap_or_default(),
            don_vi_tinh: row.don_vi_tinh.clone().unwrap_or_default(),
            stats: aggregate_from_item(row),
        })
        .collect();

    exact_json(
        ReportDto {
            total,
            partners,
            items,
        }
        .wire_json(),
    )
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq)]
#[serde(transparent)]
struct JsonDecimal(Number);

impl Default for JsonDecimal {
    fn default() -> Self {
        Self(Number::from(0))
    }
}

impl JsonDecimal {
    fn sql_text(&self) -> String {
        self.0.to_string()
    }
}

#[derive(Debug, Default, Deserialize, Eq, PartialEq)]
#[serde(default, rename_all = "camelCase")]
struct SaveLineRequest {
    #[serde(rename = "id", alias = "Id")]
    _id: i64,
    #[serde(rename = "loaiDong", alias = "LoaiDong")]
    _loai_dong: Option<String>,
    #[serde(alias = "MaHang")]
    ma_hang: Option<String>,
    #[serde(alias = "TenHang")]
    ten_hang: Option<String>,
    #[serde(alias = "QuyCach")]
    quy_cach: Option<String>,
    #[serde(alias = "DonViTinh")]
    don_vi_tinh: Option<String>,
    #[serde(alias = "SoLuong")]
    so_luong: JsonDecimal,
    #[serde(alias = "DonGiaGiaCong")]
    don_gia_gia_cong: JsonDecimal,
    #[serde(alias = "GhiChu")]
    ghi_chu: Option<String>,
}

fn dotnet_min_date() -> NaiveDate {
    NaiveDate::from_ymd_opt(1, 1, 1).expect("year one is a valid chrono date")
}

#[derive(Debug, Deserialize, Eq, PartialEq)]
#[serde(default, rename_all = "camelCase")]
struct SaveRequest {
    #[serde(alias = "LoaiPhieu")]
    loai_phieu: Option<String>,
    #[serde(alias = "DoiTac")]
    doi_tac: Option<String>,
    #[serde(alias = "NhanVienPhuTrach")]
    nhan_vien_phu_trach: Option<String>,
    #[serde(default = "dotnet_min_date", alias = "NgayLap")]
    ngay_lap: NaiveDate,
    #[serde(alias = "HanHoanThanh")]
    han_hoan_thanh: Option<NaiveDate>,
    #[serde(alias = "GhiChu")]
    ghi_chu: Option<String>,
    #[serde(alias = "Lines")]
    lines: Option<Vec<SaveLineRequest>>,
}

impl Default for SaveRequest {
    fn default() -> Self {
        Self {
            loai_phieu: None,
            doi_tac: None,
            nhan_vien_phu_trach: None,
            ngay_lap: dotnet_min_date(),
            han_hoan_thanh: None,
            ghi_chu: None,
            lines: None,
        }
    }
}

#[derive(Debug, Eq, PartialEq)]
struct SaveValues {
    loai_phieu: String,
    doi_tac: String,
    nhan_vien: String,
    ngay_lap: NaiveDate,
    han_hoan_thanh: Option<NaiveDate>,
    ghi_chu: String,
    lines: Vec<SaveLineRequest>,
}

impl From<SaveRequest> for SaveValues {
    fn from(request: SaveRequest) -> Self {
        Self {
            loai_phieu: normalize_phieu_type(request.loai_phieu.as_deref()),
            doi_tac: request.doi_tac.unwrap_or_default(),
            nhan_vien: request.nhan_vien_phu_trach.unwrap_or_default(),
            ngay_lap: request.ngay_lap,
            han_hoan_thanh: request.han_hoan_thanh,
            ghi_chu: request.ghi_chu.unwrap_or_default(),
            lines: request.lines.unwrap_or_default(),
        }
    }
}

fn normalize_phieu_type(value: Option<&str>) -> String {
    let value = value.unwrap_or_default().trim();
    if value.is_empty() {
        return LOAI_XUAT.to_owned();
    }
    let folded = value.to_lowercase();
    if folded.contains("nhập") {
        LOAI_NHAP.to_owned()
    } else {
        // Unknown values and every spelling containing `Xuất` both normalize
        // to the same default in the existing implementation.
        LOAI_XUAT.to_owned()
    }
}

fn is_nhap(value: &str) -> bool {
    value.to_lowercase().contains("nhập")
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum GiaCongJsonError {
    PayloadTooLarge,
    Status(StatusCode),
}

impl IntoResponse for GiaCongJsonError {
    fn into_response(self) -> Response {
        match self {
            Self::PayloadTooLarge => (
                StatusCode::PAYLOAD_TOO_LARGE,
                Json(json!({ "message": PAYLOAD_TOO_LARGE_MESSAGE })),
            )
                .into_response(),
            Self::Status(status) => status.into_response(),
        }
    }
}

fn json_payload(
    payload: Result<Json<SaveRequest>, JsonRejection>,
) -> Result<SaveRequest, GiaCongJsonError> {
    match payload {
        Ok(Json(request)) => Ok(request),
        Err(rejection) if rejection.status() == StatusCode::PAYLOAD_TOO_LARGE => {
            Err(GiaCongJsonError::PayloadTooLarge)
        }
        Err(rejection) => {
            let status = if rejection.status() == StatusCode::UNPROCESSABLE_ENTITY {
                StatusCode::BAD_REQUEST
            } else {
                rejection.status()
            };
            Err(GiaCongJsonError::Status(status))
        }
    }
}

#[derive(Debug, Serialize)]
struct SavedResponse {
    id: i64,
}

async fn create_phieu(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    payload: Result<Json<SaveRequest>, JsonRejection>,
) -> Response {
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    save_phieu(&state.pool, &auth.username, None, request).await
}

async fn update_phieu(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(raw_id): Path<String>,
    payload: Result<Json<SaveRequest>, JsonRejection>,
) -> Response {
    let Some(id) = parse_long_id(&raw_id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    let request = match json_payload(payload) {
        Ok(request) => request,
        Err(error) => return error.into_response(),
    };
    save_phieu(&state.pool, &auth.username, Some(id), request).await
}

#[derive(Debug)]
enum SaveDatabaseError {
    NotFound,
    Sql(sqlx::Error),
}

impl From<sqlx::Error> for SaveDatabaseError {
    fn from(error: sqlx::Error) -> Self {
        Self::Sql(error)
    }
}

async fn write_phieu(
    transaction: &mut Transaction<'_, Postgres>,
    id: Option<i64>,
    values: &SaveValues,
) -> Result<i64, SaveDatabaseError> {
    let phieu_id = if let Some(phieu_id) = id {
        let updated = sqlx::query(
            r#"
            UPDATE gia_cong_phieu
            SET loai_phieu = $2,
                doi_tac = $3,
                nhan_vien = $4,
                ngay_lap = $5,
                han_hoan_thanh = $6,
                ghi_chu = $7,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = $1
            "#,
        )
        .bind(phieu_id)
        .bind(&values.loai_phieu)
        .bind(&values.doi_tac)
        .bind(&values.nhan_vien)
        .bind(values.ngay_lap)
        .bind(values.han_hoan_thanh)
        .bind(&values.ghi_chu)
        .execute(&mut **transaction)
        .await?;
        if updated.rows_affected() == 0 {
            return Err(SaveDatabaseError::NotFound);
        }
        sqlx::query("DELETE FROM gia_cong_hang_hoa WHERE phieu_id = $1")
            .bind(phieu_id)
            .execute(&mut **transaction)
            .await?;
        phieu_id
    } else {
        let phieu_id = sqlx::query_scalar::<_, i64>(
            "SELECT nextval(pg_get_serial_sequence('gia_cong_phieu', 'id'))",
        )
        .fetch_one(&mut **transaction)
        .await?;
        let ma_phieu = format!("GC{phieu_id:06}");
        sqlx::query(
            r#"
            INSERT INTO gia_cong_phieu
                (id, ma_phieu, loai_phieu, doi_tac, nhan_vien, ngay_lap,
                 han_hoan_thanh, ghi_chu, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, CURRENT_TIMESTAMP)
            "#,
        )
        .bind(phieu_id)
        .bind(ma_phieu)
        .bind(&values.loai_phieu)
        .bind(&values.doi_tac)
        .bind(&values.nhan_vien)
        .bind(values.ngay_lap)
        .bind(values.han_hoan_thanh)
        .bind(&values.ghi_chu)
        .execute(&mut **transaction)
        .await?;
        phieu_id
    };

    for line in &values.lines {
        let quantity = line.so_luong.sql_text();
        let unit_price = if is_nhap(&values.loai_phieu) {
            line.don_gia_gia_cong.sql_text()
        } else {
            "0".to_owned()
        };
        sqlx::query(
            r#"
            INSERT INTO gia_cong_hang_hoa
                (phieu_id, loai_dong, ma_hang, ten_hang, quy_cach, don_vi_tinh,
                 so_luong, don_gia_gia_cong, ghi_chu)
            VALUES ($1, $2, $3, $4, $5, $6, $7::text::numeric,
                    $8::text::numeric, $9)
            "#,
        )
        .bind(phieu_id)
        .bind(&values.loai_phieu)
        .bind(line.ma_hang.as_deref().unwrap_or_default())
        .bind(line.ten_hang.as_deref().unwrap_or_default())
        .bind(line.quy_cach.as_deref().unwrap_or_default())
        .bind(line.don_vi_tinh.as_deref().unwrap_or_default())
        .bind(quantity)
        .bind(unit_price)
        .bind(line.ghi_chu.as_deref().unwrap_or_default())
        .execute(&mut **transaction)
        .await?;
    }
    Ok(phieu_id)
}

async fn save_phieu(
    pool: &PgPool,
    username: &str,
    id: Option<i64>,
    request: SaveRequest,
) -> Response {
    let is_create = id.is_none();
    let values = SaveValues::from(request);
    let mut transaction = match pool.begin().await {
        Ok(transaction) => transaction,
        Err(error) => return database_failure("begin GiaCong save transaction", error),
    };

    let phieu_id = match write_phieu(&mut transaction, id, &values).await {
        Ok(phieu_id) => phieu_id,
        Err(SaveDatabaseError::NotFound) => {
            if let Err(error) = transaction.rollback().await {
                tracing::warn!(%error, "rollback failed after missing GiaCong update target");
            }
            return StatusCode::NOT_FOUND.into_response();
        }
        Err(SaveDatabaseError::Sql(error)) => {
            tracing::warn!(%error, "native GiaCong save rejected by PostgreSQL");
            if let Err(rollback_error) = transaction.rollback().await {
                tracing::warn!(%rollback_error, "rollback failed after GiaCong save error");
            }
            return save_invalid_response();
        }
    };
    if let Err(error) = transaction.commit().await {
        tracing::warn!(%error, "native GiaCong transaction commit failed");
        return save_invalid_response();
    }

    let action = if is_create {
        AUDIT_CREATE
    } else {
        AUDIT_UPDATE
    };
    let details = if is_create {
        AUDIT_CREATE_DETAILS
    } else {
        AUDIT_UPDATE_DETAILS
    };
    record_audit(pool, username, action, phieu_id, details).await;
    Json(SavedResponse { id: phieu_id }).into_response()
}

async fn delete_phieu(
    State(state): State<Arc<AppState>>,
    Extension(auth): Extension<AuthContext>,
    Path(raw_id): Path<String>,
) -> Response {
    let Some(id) = parse_long_id(&raw_id) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    let result = match sqlx::query("DELETE FROM gia_cong_phieu WHERE id = $1")
        .bind(id)
        .execute(&state.pool)
        .await
    {
        Ok(result) => result,
        Err(error) => return database_failure("delete GiaCong record", error),
    };
    if result.rows_affected() == 0 {
        return StatusCode::NOT_FOUND.into_response();
    }
    record_audit(
        &state.pool,
        &auth.username,
        AUDIT_DELETE,
        id,
        AUDIT_DELETE_DETAILS,
    )
    .await;
    StatusCode::NO_CONTENT.into_response()
}

async fn record_audit(
    pool: &PgPool,
    username: &str,
    action: &'static str,
    id: i64,
    details: &'static str,
) {
    if let Err(error) = sqlx::query(AUDIT_SQL)
        .bind(username)
        .bind(action)
        .bind(id.to_string())
        .bind(details)
        .execute(pool)
        .await
    {
        // RecordAudit is deliberately best-effort in the .NET API. Business
        // data has already committed and must not be reported as failed.
        tracing::warn!(%error, action, gia_cong_id = id, "could not record GiaCong audit event");
    }
}

fn parse_long_id(raw: &str) -> Option<i64> {
    raw.parse().ok()
}

fn save_invalid_response() -> Response {
    (
        StatusCode::BAD_REQUEST,
        Json(json!({ "message": SAVE_INVALID_MESSAGE })),
    )
        .into_response()
}

fn database_failure(operation: &'static str, error: sqlx::Error) -> Response {
    tracing::warn!(%error, operation, "native GiaCong database operation failed");
    (
        StatusCode::SERVICE_UNAVAILABLE,
        Json(json!({ "message": DATABASE_UNAVAILABLE_MESSAGE })),
    )
        .into_response()
}

#[cfg(test)]
mod tests {
    use super::*;
    use http_body_util::BodyExt;
    use std::collections::BTreeSet;

    #[test]
    fn route_matrix_matches_all_six_dotnet_contracts() {
        assert_eq!(
            ROUTE_CONTRACTS,
            &[
                ("GET", "/api/giacong/"),
                ("GET", "/api/giacong/report"),
                ("GET", "/api/giacong/{id:long}"),
                ("POST", "/api/giacong/"),
                ("PUT", "/api/giacong/{id:long}"),
                ("DELETE", "/api/giacong/{id:long}"),
            ]
        );
        assert_eq!(MAX_JSON_BODY_BYTES, 16_777_216);
    }

    #[test]
    fn list_filter_is_intentionally_case_sensitive_like_the_csharp_switch() {
        assert_eq!(normalize_list_filter(Some("nhap")), ListFilter::Nhap);
        assert_eq!(normalize_list_filter(Some("xuat")), ListFilter::Xuat);
        assert_eq!(normalize_list_filter(Some("NHAP")), ListFilter::All);
        assert_eq!(normalize_list_filter(Some(" nhập ")), ListFilter::All);
        assert_eq!(normalize_list_filter(None), ListFilter::All);
    }

    #[test]
    fn voucher_type_normalization_preserves_business_literals() {
        assert_eq!(normalize_phieu_type(None), LOAI_XUAT);
        assert_eq!(normalize_phieu_type(Some("   ")), LOAI_XUAT);
        assert_eq!(normalize_phieu_type(Some("NHẬP thành phẩm")), LOAI_NHAP);
        assert_eq!(normalize_phieu_type(Some("phiếu XuẤT")), LOAI_XUAT);
        assert_eq!(normalize_phieu_type(Some("unknown")), LOAI_XUAT);
        assert!(is_nhap("NhẬp gia công"));
        assert!(!is_nhap("Xuất gia công"));
    }

    #[test]
    fn request_defaults_and_pascal_case_aliases_match_dotnet_binding() {
        let empty: SaveRequest = serde_json::from_value(json!({})).unwrap();
        assert_eq!(empty, SaveRequest::default());
        assert_eq!(empty.ngay_lap, dotnet_min_date());

        let request: SaveRequest = serde_json::from_value(json!({
            "LoaiPhieu": " Nhập hàng ",
            "DoiTac": null,
            "NhanVienPhuTrach": "Lan",
            "NgayLap": "2026-08-24",
            "HanHoanThanh": null,
            "GhiChu": null,
            "Lines": [{
                "Id": 12,
                "LoaiDong": "ignored",
                "MaHang": null,
                "TenHang": "Áo",
                "QuyCach": "M",
                "DonViTinh": "cái",
                "SoLuong": 2.50,
                "DonGiaGiaCong": 1200.25,
                "GhiChu": null,
                "thanhTien": "ignored read-only property"
            }]
        }))
        .unwrap();
        let values = SaveValues::from(request);
        assert_eq!(values.loai_phieu, LOAI_NHAP);
        assert_eq!(values.doi_tac, "");
        assert_eq!(values.nhan_vien, "Lan");
        assert_eq!(
            values.ngay_lap,
            NaiveDate::from_ymd_opt(2026, 8, 24).unwrap()
        );
        assert_eq!(values.lines.len(), 1);
        assert_eq!(values.lines[0].so_luong.sql_text(), "2.5");
        assert_eq!(values.lines[0].don_gia_gia_cong.sql_text(), "1200.25");

        assert!(serde_json::from_value::<SaveRequest>(json!({ "ngayLap": null })).is_err());
        assert!(
            serde_json::from_value::<SaveRequest>(json!({
                "lines": [{ "soLuong": null }]
            }))
            .is_err()
        );
    }

    #[test]
    fn exact_list_json_keeps_decimal_scale_and_omits_null_dates() {
        let dto = ListDto {
            id: 7,
            ma_phieu: "GC000007".to_owned(),
            loai_phieu: LOAI_NHAP.to_owned(),
            doi_tac: "Xưởng A".to_owned(),
            nhan_vien_phu_trach: "Lan".to_owned(),
            ngay_lap: NaiveDate::from_ymd_opt(2026, 8, 24).unwrap(),
            han_hoan_thanh: None,
            stats: GiaCongStats {
                count: 2,
                so_luong_xuat: DecimalText("0.00".to_owned()),
                so_luong_nhap: DecimalText("3.50".to_owned()),
                so_luong_con_tai_cong_ty: DecimalText("0.00".to_owned()),
                tien_gia_cong_phai_tra: DecimalText("4200.8750".to_owned()),
            },
        };
        assert_eq!(
            dto.wire_json(),
            concat!(
                r#"{"id":7,"maPhieu":"GC000007","loaiPhieu":"Nhập gia công","#,
                r#""doiTac":"Xưởng A","nhanVienPhuTrach":"Lan","ngayLap":"2026-08-24","#,
                r#""soMatHang":2,"tongGiaTri":4200.8750,"soLuongXuat":0.00,"#,
                r#""soLuongNhap":3.50,"soLuongConTaiCongTy":0.00,"#,
                r#""tienGiaCongPhaiTra":4200.8750}"#
            )
        );
    }

    #[test]
    fn detail_lines_use_header_type_and_include_computed_amount() {
        let detail = DetailDto {
            id: 9,
            ma_phieu: "GC000009".to_owned(),
            loai_phieu: LOAI_XUAT.to_owned(),
            doi_tac: String::new(),
            nhan_vien_phu_trach: String::new(),
            ngay_lap: dotnet_min_date(),
            han_hoan_thanh: Some(NaiveDate::from_ymd_opt(2026, 9, 1).unwrap()),
            ghi_chu: "a\"b".to_owned(),
            lines: vec![LineDto {
                id: 1,
                loai_dong: LOAI_XUAT.to_owned(),
                ma_hang: String::new(),
                ten_hang: "Áo".to_owned(),
                quy_cach: "M".to_owned(),
                don_vi_tinh: "cái".to_owned(),
                so_luong: DecimalText("2.00".to_owned()),
                don_gia_gia_cong: DecimalText("0.00".to_owned()),
                ghi_chu: String::new(),
                thanh_tien: DecimalText("0.0000".to_owned()),
            }],
        };
        let wire = detail.wire_json();
        let value: serde_json::Value = serde_json::from_str(&wire).unwrap();
        assert_eq!(value["ngayLap"], "0001-01-01");
        assert_eq!(value["hanHoanThanh"], "2026-09-01");
        assert_eq!(value["lines"][0]["loaiDong"], LOAI_XUAT);
        assert_eq!(value["lines"][0]["thanhTien"], 0.0);
        assert!(wire.contains(r#""thanhTien":0.0000"#));
        assert!(wire.contains(r#""ghiChu":"a\"b""#));
    }

    #[test]
    fn report_filter_keeps_original_unparenthesized_precedence() {
        let sql = report_sql("SELECT 1 FROM typed");
        let last_original_term = "OR h.loai_dong ILIKE '%Nhập%'";
        let start = sql.find(last_original_term).unwrap();
        let suffix = &sql[start..];
        assert!(suffix.contains("OR h.loai_dong ILIKE '%Nhập%'\n\n       AND"));
        assert!(suffix.contains("$1::text IS NULL"));
        assert!(suffix.contains("$2::date IS NULL"));
        assert!(suffix.contains("$3::date IS NULL"));

        assert_eq!(report_partner_pattern(None), None);
        assert_eq!(report_partner_pattern(Some("   ".to_owned())), None);
        assert_eq!(
            report_partner_pattern(Some("  Xưởng A  ".to_owned())),
            Some("%Xưởng A%".to_owned())
        );
    }

    #[test]
    fn accounting_permission_and_long_constraint_fail_closed() {
        let none = BTreeSet::new();
        assert!(!may_access(&none));
        let allowed = BTreeSet::from([permissions::ACCOUNTING_ACCESS.to_owned()]);
        assert!(may_access(&allowed));

        assert_eq!(parse_long_id("9223372036854775807"), Some(i64::MAX));
        assert_eq!(parse_long_id("-1"), Some(-1));
        assert_eq!(parse_long_id("9223372036854775808"), None);
        assert_eq!(parse_long_id("not-a-long"), None);
    }

    #[test]
    fn decimal_text_preserves_valid_scale_and_never_emits_special_values() {
        assert_eq!(
            DecimalText::from_database(Some("1234567890123456.70".to_owned())),
            DecimalText("1234567890123456.70".to_owned())
        );
        assert_eq!(
            DecimalText::from_database(Some("NaN".to_owned())),
            DecimalText::zero()
        );
    }

    #[tokio::test]
    async fn payload_and_save_error_messages_match_dotnet() {
        let response = GiaCongJsonError::PayloadTooLarge.into_response();
        assert_eq!(response.status(), StatusCode::PAYLOAD_TOO_LARGE);
        let body = response.into_body().collect().await.unwrap().to_bytes();
        assert_eq!(
            serde_json::from_slice::<serde_json::Value>(&body).unwrap(),
            json!({ "message": "Payload vượt giới hạn 16777216 byte." })
        );

        let response = save_invalid_response();
        assert_eq!(response.status(), StatusCode::BAD_REQUEST);
        let body = response.into_body().collect().await.unwrap().to_bytes();
        assert_eq!(
            serde_json::from_slice::<serde_json::Value>(&body).unwrap(),
            json!({
                "message": "Không lưu được phiếu gia công (dữ liệu không hợp lệ hoặc trùng lặp)."
            })
        );
        assert_eq!(
            DATABASE_UNAVAILABLE_MESSAGE,
            "Khong ket noi duoc co so du lieu PostgreSQL."
        );
    }

    #[test]
    fn audit_contract_literals_are_exact() {
        assert!(AUDIT_SQL.contains("'GiaCong'"));
        assert_eq!(
            (
                AUDIT_CREATE,
                AUDIT_UPDATE,
                AUDIT_DELETE,
                AUDIT_CREATE_DETAILS,
                AUDIT_UPDATE_DETAILS,
                AUDIT_DELETE_DETAILS,
            ),
            (
                "Tạo phiếu gia công",
                "Cập nhật phiếu gia công",
                "Xóa phiếu gia công",
                "Tạo phiếu gia công (web).",
                "Cập nhật phiếu gia công (web).",
                "Xóa phiếu gia công (web).",
            )
        );
    }
}
