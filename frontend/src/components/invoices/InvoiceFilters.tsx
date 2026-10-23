import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useClientsQuery } from "@/hooks/useClients";
import type { InvoiceStatus } from "@/types/invoice";

const ALL_STATUSES: InvoiceStatus[] = ["Draft", "Unpaid", "Overdue", "Paid", "Cancelled"];
const ALL_VALUE = "all";

// Capped at the backend's ListClientsQueryValidator max (InclusiveBetween(1, 100)) — requesting
// more than this 400s. This is still just one page, not a guarantee every active client fits: the
// real fix for a business with >100 active clients is a searchable/combobox-style client select,
// not a bigger fixed page size. The existing shadcn select.tsx primitive (plain Radix Select) has
// no built-in search/combobox behavior, so that's left as follow-up work rather than done here.
const CLIENT_FILTER_PAGE_SIZE = 100;

export interface InvoiceFiltersValue {
  status?: InvoiceStatus;
  clientId?: string;
  dateFrom?: string;
  dateTo?: string;
}

interface InvoiceFiltersProps {
  value: InvoiceFiltersValue;
  onChange: (value: InvoiceFiltersValue) => void;
}

export function InvoiceFilters({ value, onChange }: InvoiceFiltersProps) {
  const { data: clients, isError: isClientsError } = useClientsQuery({
    pageNumber: 1,
    pageSize: CLIENT_FILTER_PAGE_SIZE,
  });

  return (
    <div className="flex flex-col gap-4 md:flex-row md:flex-wrap md:items-end">
      <div className="space-y-1.5">
        <Label htmlFor="invoice-filter-status">Status</Label>
        <Select
          value={value.status ?? ALL_VALUE}
          onValueChange={(next) =>
            onChange({ ...value, status: next === ALL_VALUE ? undefined : (next as InvoiceStatus) })
          }
        >
          <SelectTrigger id="invoice-filter-status" className="w-full md:w-40">
            <SelectValue placeholder="All statuses" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_VALUE}>All statuses</SelectItem>
            {ALL_STATUSES.map((status) => (
              <SelectItem key={status} value={status}>
                {status}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="invoice-filter-client">Client</Label>
        <Select
          value={value.clientId ?? ALL_VALUE}
          onValueChange={(next) =>
            onChange({ ...value, clientId: next === ALL_VALUE ? undefined : next })
          }
          disabled={isClientsError}
        >
          <SelectTrigger
            id="invoice-filter-client"
            className="w-full md:w-48"
            aria-invalid={isClientsError}
            aria-describedby={isClientsError ? "invoice-filter-client-load-error" : undefined}
          >
            <SelectValue placeholder="All clients" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_VALUE}>All clients</SelectItem>
            {clients?.items.map((client) => (
              <SelectItem key={client.id} value={client.id}>
                {client.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {isClientsError && (
          <p id="invoice-filter-client-load-error" className="text-sm text-destructive">
            Couldn&apos;t load clients.
          </p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="invoice-filter-date-from">Due from</Label>
        <Input
          id="invoice-filter-date-from"
          type="date"
          value={value.dateFrom ?? ""}
          onChange={(event) => onChange({ ...value, dateFrom: event.target.value || undefined })}
          className="w-full md:w-40"
        />
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="invoice-filter-date-to">Due to</Label>
        <Input
          id="invoice-filter-date-to"
          type="date"
          value={value.dateTo ?? ""}
          onChange={(event) => onChange({ ...value, dateTo: event.target.value || undefined })}
          className="w-full md:w-40"
        />
      </div>
    </div>
  );
}
