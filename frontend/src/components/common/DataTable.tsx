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
import { useIsMobile } from "@/hooks/useIsMobile";
import { cn } from "@/lib/utils";

export interface DataTableColumn<TRow> {
  /** Unique key for this column, also used as the React key for its header/cell. */
  key: string;
  header: ReactNode;
  /** Renders a cell's content for a given row. */
  cell: (row: TRow) => ReactNode;
  className?: string;
}

/** One field in a card-view layout below the ~768px breakpoint — see `DataTableMobileCard`. */
export interface DataTableMobileField<TRow> {
  /** Unique key for this field, also used as the React key. */
  key: string;
  /** Renders this field's label alongside its value (e.g. "Due date"). Omit for an `emphasis`
   * field, which is meant to read as the card's own heading rather than a labeled data point. */
  label?: ReactNode;
  /** Renders this field's value for a given row. */
  value: (row: TRow) => ReactNode;
  /**
   * Renders this field without a label, in a larger weight, stacked above the labeled fields —
   * for whatever most identifies the row at a glance (e.g. an invoice number, a client's name).
   * Multiple `emphasis` fields stack in the order given.
   */
  emphasis?: boolean;
  className?: string;
}

/**
 * Card-view field config for a `DataTable` caller — see architecture.md's "Mobile
 * responsiveness" paragraph. `DataTable` is generic over column config, so it can't derive a
 * readable phone-width card from `columns` alone (desktop tables often have 4-6 columns, more
 * than a card can show without becoming unreadable); each calling list page instead picks a
 * deliberate subset/order of fields via this config.
 */
export interface DataTableMobileCard<TRow> {
  fields: DataTableMobileField<TRow>[];
  /** Optional row-actions area (e.g. a "⋯" dropdown), pinned to the top-right of each card. */
  actions?: (row: TRow) => ReactNode;
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
  /**
   * Card-per-row field config rendered instead of the table below the ~768px breakpoint. When
   * omitted, the table renders unconditionally at every viewport width — the pre-existing
   * behavior — so this is strictly additive/opt-in per caller, not a breaking change.
   */
  mobileCard?: DataTableMobileCard<TRow>;
}

/** Number of placeholder rows/cards shown while `isLoading` — enough to read as a list, not a spinner. */
const SKELETON_ROW_COUNT = 5;

/**
 * Small generic table wrapper over shadcn's `Table` primitive — a columns config + rows + empty
 * state, nothing more. Not an enterprise table framework: no built-in sorting/virtualization,
 * just what Clients (and, shortly, Invoices) need.
 *
 * Below the app's single ~768px breakpoint, and only when the caller supplies `mobileCard`, this
 * renders a card-per-row layout instead of the table (never both at once — see `useIsMobile`'s
 * doc comment for why rendering is swapped rather than CSS-hidden: Vitest's jsdom environment
 * doesn't evaluate real CSS, so a `hidden md:table` style approach would mount both layouts'
 * text into the DOM simultaneously and break every `getByText`/`findByText` assertion that
 * expects a single match).
 */
export function DataTable<TRow>({
  columns,
  rows,
  getRowId,
  onRowClick,
  emptyMessage = "No results.",
  isLoading = false,
  mobileCard,
}: DataTableProps<TRow>) {
  const isMobile = useIsMobile();

  if (isMobile && mobileCard) {
    return (
      <MobileCardList
        rows={rows}
        getRowId={getRowId}
        onRowClick={onRowClick}
        emptyMessage={emptyMessage}
        isLoading={isLoading}
        mobileCard={mobileCard}
      />
    );
  }

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

function MobileCardList<TRow>({
  rows,
  getRowId,
  onRowClick,
  emptyMessage,
  isLoading,
  mobileCard,
}: {
  rows: TRow[];
  getRowId: (row: TRow) => string;
  onRowClick?: (row: TRow) => void;
  emptyMessage: ReactNode;
  isLoading: boolean;
  mobileCard: DataTableMobileCard<TRow>;
}) {
  if (isLoading) {
    return (
      <div className="flex flex-col gap-3" data-testid="data-table-mobile-cards">
        {Array.from({ length: SKELETON_ROW_COUNT }).map((_, index) => (
          <div key={`mobile-skeleton-${index}`} className="rounded-lg border border-border p-4">
            <Skeleton className="h-4 w-2/3" />
            <Skeleton className="mt-2 h-4 w-1/2" />
          </div>
        ))}
      </div>
    );
  }

  if (rows.length === 0) {
    return (
      <div
        className="rounded-lg border border-border p-6 text-center text-sm text-muted-foreground"
        data-testid="data-table-mobile-cards"
      >
        {emptyMessage}
      </div>
    );
  }

  const emphasisFields = mobileCard.fields.filter((field) => field.emphasis);
  const detailFields = mobileCard.fields.filter((field) => !field.emphasis);

  return (
    <div className="flex flex-col gap-3" data-testid="data-table-mobile-cards">
      {rows.map((row) => (
        <div
          key={getRowId(row)}
          role={onRowClick ? "button" : undefined}
          tabIndex={onRowClick ? 0 : undefined}
          onClick={onRowClick ? () => onRowClick(row) : undefined}
          onKeyDown={
            onRowClick
              ? (event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    onRowClick(row);
                  }
                }
              : undefined
          }
          className={cn(
            "rounded-lg border border-border p-4",
            onRowClick && "cursor-pointer transition-colors hover:bg-muted/50",
          )}
        >
          <div className="flex items-start justify-between gap-3">
            <div className="flex min-w-0 flex-1 flex-col gap-1">
              {emphasisFields.map((field) => (
                <div key={field.key} className={cn("text-base font-medium", field.className)}>
                  {field.value(row)}
                </div>
              ))}
            </div>
            {mobileCard.actions && (
              <div onClick={(event) => event.stopPropagation()} className="shrink-0">
                {mobileCard.actions(row)}
              </div>
            )}
          </div>
          {detailFields.length > 0 && (
            <dl className="mt-2 flex flex-col gap-1 text-sm text-muted-foreground">
              {detailFields.map((field) => (
                <div key={field.key} className={cn("flex items-center gap-1.5", field.className)}>
                  {field.label && <dt className="font-medium text-foreground">{field.label}:</dt>}
                  <dd>{field.value(row)}</dd>
                </div>
              ))}
            </dl>
          )}
        </div>
      ))}
    </div>
  );
}
