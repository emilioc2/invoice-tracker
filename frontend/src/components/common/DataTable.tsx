import type { ReactNode } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { cn } from "@/lib/utils";

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
  /**
   * When true, renders a handful of shimmering placeholder rows (matching `columns.length`)
   * instead of `rows`/`emptyMessage` — the "real skeleton, not a loading string" treatment.
   * `emptyMessage` is reserved purely for the genuinely-empty case once loading has finished.
   */
  isLoading?: boolean;
}

/** Number of placeholder rows shown while `isLoading` — enough to read as a table, not a spinner. */
const SKELETON_ROW_COUNT = 5;

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
  isLoading = false,
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
        {isLoading ? (
          Array.from({ length: SKELETON_ROW_COUNT }).map((_, rowIndex) => (
            <TableRow key={`skeleton-row-${rowIndex}`}>
              {columns.map((column) => (
                <TableCell key={column.key} className={column.className}>
                  <Skeleton className="h-4 w-full max-w-40" />
                </TableCell>
              ))}
            </TableRow>
          ))
        ) : rows.length === 0 ? (
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
              className={cn(onRowClick && "cursor-pointer transition-colors hover:bg-muted/50")}
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
