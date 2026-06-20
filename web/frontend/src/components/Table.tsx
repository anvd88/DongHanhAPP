import type { ReactNode } from "react";

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
}: {
  columns: Column<T>[];
  rows: T[];
  keyOf: (row: T, i: number) => string | number;
  onRowClick?: (row: T) => void;
  empty?: ReactNode;
}) {
  const alignCls = (a?: string) => (a === "right" ? "text-right" : a === "center" ? "text-center" : "text-left");
  return (
    <div className="scroll-thin overflow-x-auto">
      <table className="w-full border-collapse text-sm">
        <thead>
          <tr className="km-table-head-row">
            {columns.map((c, i) => (
              <th
                key={i}
                className={`whitespace-nowrap px-3 py-2 text-xs font-bold tracking-wide text-[var(--text-muted)] ${alignCls(c.align)}`}
              >
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length} className="py-12 text-center text-sm text-[var(--text-muted)]">
                {empty ?? "Chưa có dữ liệu"}
              </td>
            </tr>
          ) : (
            rows.map((row, i) => (
              <tr
                key={keyOf(row, i)}
                onClick={() => onRowClick?.(row)}
                className={`km-table-row ${
                  onRowClick ? "cursor-pointer" : ""
                }`}
              >
                {columns.map((c, j) => (
                  <td key={j} className={`px-3 py-2 text-[var(--text)] ${alignCls(c.align)} ${c.className ?? ""}`}>
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
