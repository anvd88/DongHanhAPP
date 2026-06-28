import type { ReactNode } from "react";
import "../features/giacong/giacong.css";

export interface Column<T> {
  header: string;
  cell: (row: T, index: number) => ReactNode;
  align?: "left" | "right" | "center";
  className?: string;
}

export function Table<T>({
  columns,
  rows,
  keyOf,
  onRowClick,
  empty,
  loading,
  skeletonRows = 6,
}: {
  columns: Column<T>[];
  rows: T[];
  keyOf: (row: T, i: number) => string | number;
  onRowClick?: (row: T) => void;
  empty?: ReactNode;
  loading?: boolean;
  skeletonRows?: number;
}) {
  const alignCls = (a?: string) => (a === "right" ? "text-right" : a === "center" ? "text-center" : "");
  return (
    <div className="gc-scroll overflow-auto">
      <table className="gc-table">
        <thead>
          <tr>
            {columns.map((c, i) => (
              <th key={i} className={alignCls(c.align)}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {loading ? (
            Array.from({ length: skeletonRows }).map((_, i) => (
              <tr key={`sk-${i}`} style={{ cursor: "default" }}>
                {columns.map((c, j) => (
                  <td key={j} className={alignCls(c.align)}>
                    <span
                      className={`gc-skeleton h-4 ${c.align === "right" ? "ml-auto" : c.align === "center" ? "mx-auto" : ""}`}
                      style={{ display: "block", width: c.align === "right" || c.align === "center" ? "48%" : "82%" }}
                    />
                  </td>
                ))}
              </tr>
            ))
          ) : rows.length === 0 ? (
            <tr style={{ cursor: "default" }}>
              <td colSpan={columns.length} className="py-12 text-center text-sm text-[var(--gc-text-muted)]">
                {empty ?? "Chưa có dữ liệu"}
              </td>
            </tr>
          ) : (
            rows.map((row, i) => (
              <tr
                key={keyOf(row, i)}
                onClick={() => onRowClick?.(row)}
                style={{ cursor: onRowClick ? "pointer" : "default" }}
              >
                {columns.map((c, j) => (
                  <td key={j} className={`${alignCls(c.align)} ${c.className ?? ""}`}>
                    {c.cell(row, i)}
                  </td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}
