import type { ReactNode } from "react";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

export interface DataTableColumn<TRow> {
  /** Unique key for this column, also used as the React key for its header/cell. */
  key: string;
  header: ReactNode;
  /** Renders a cell's content for a given row. */
  cell: (row: TRow) => ReactNode;
  className?: string;
}

interface DataTableProps<TRow> {
  columns: DataTableColumn<TRow>[];
  rows: TRow[];
  /** Stable identity for each row, used as the React key. */
  getRowId: (row: TRow) => string;
  onRowClick?: (row: TRow) => void;
  emptyMessage?: ReactNode;
}

/**
 * Small generic table wrapper over shadcn's `Table` primitive — a columns config + rows + empty
 * state, nothing more. Not an enterprise table framework: no built-in sorting/virtualization,
 * just what Clients (and, shortly, Invoices) need.
 */
export function DataTable<TRow>({
  columns,
  rows,
  getRowId,
  onRowClick,
  emptyMessage = "No results.",
}: DataTableProps<TRow>) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          {columns.map((column) => (
            <TableHead key={column.key} className={column.className}>
              {column.header}
            </TableHead>
          ))}
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.length === 0 ? (
          <TableRow>
            <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
              {emptyMessage}
            </TableCell>
          </TableRow>
        ) : (
          rows.map((row) => (
            <TableRow
              key={getRowId(row)}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              className={onRowClick ? "cursor-pointer" : undefined}
            >
              {columns.map((column) => (
                <TableCell key={column.key} className={column.className}>
                  {column.cell(row)}
                </TableCell>
              ))}
            </TableRow>
          ))
        )}
      </TableBody>
    </Table>
  );
}
