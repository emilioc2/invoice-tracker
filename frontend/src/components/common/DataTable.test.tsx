import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  DataTable,
  type DataTableColumn,
  type DataTableMobileCard,
} from "@/components/common/DataTable";
import { mockViewport } from "@/test/test-utils";

// Covers user_testing_defects.md item #18's shared-shell pass: at/above the ~768px breakpoint,
// `DataTable` must keep rendering its existing table exactly as before (desktop-unchanged); below
// it, it must switch to the card-per-row layout a caller opts into via `mobileCard` — never both
// at once, since jsdom doesn't evaluate real CSS and rendering both simultaneously would break
// every exact-match text query a calling page's own tests rely on (see `DataTable.tsx`'s doc
// comment for the full reasoning).

interface Row {
  id: string;
  name: string;
  email: string;
}

const ROWS: Row[] = [
  { id: "row-1", name: "Acme Co", email: "acme@example.com" },
  { id: "row-2", name: "Globex Inc", email: "globex@example.com" },
];

const COLUMNS: DataTableColumn<Row>[] = [
  { key: "name", header: "Name", cell: (row) => row.name },
  { key: "email", header: "Email", cell: (row) => row.email },
];

const MOBILE_CARD: DataTableMobileCard<Row> = {
  fields: [
    { key: "name", value: (row) => row.name, emphasis: true },
    { key: "email", label: "Email", value: (row) => row.email },
  ],
  actions: (row) => <button aria-label={`Actions for ${row.name}`}>Actions</button>,
};

afterEach(() => {
  vi.restoreAllMocks();
});

describe("DataTable at/above the breakpoint (desktop, the default in every test)", () => {
  it("renders the table when no mobileCard config is supplied", () => {
    render(<DataTable columns={COLUMNS} rows={ROWS} getRowId={(row) => row.id} />);

    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.getByText("Acme Co")).toBeInTheDocument();
    expect(screen.queryByTestId("data-table-mobile-cards")).not.toBeInTheDocument();
  });

  it("still renders the table, not cards, even when a mobileCard config is supplied", () => {
    render(
      <DataTable columns={COLUMNS} rows={ROWS} getRowId={(row) => row.id} mobileCard={MOBILE_CARD} />,
    );

    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.queryByTestId("data-table-mobile-cards")).not.toBeInTheDocument();
  });
});

describe("DataTable below the breakpoint (mobile)", () => {
  it("still renders the table when no mobileCard config is supplied (opt-in only)", () => {
    mockViewport(true);
    render(<DataTable columns={COLUMNS} rows={ROWS} getRowId={(row) => row.id} />);

    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.queryByTestId("data-table-mobile-cards")).not.toBeInTheDocument();
  });

  it("renders a card per row, using the mobileCard field config, when supplied", () => {
    mockViewport(true);
    render(
      <DataTable columns={COLUMNS} rows={ROWS} getRowId={(row) => row.id} mobileCard={MOBILE_CARD} />,
    );

    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.getByTestId("data-table-mobile-cards")).toBeInTheDocument();
    expect(screen.getByText("Acme Co")).toBeInTheDocument();
    expect(screen.getAllByText("Email:")).toHaveLength(ROWS.length);
    expect(screen.getByText("acme@example.com")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Actions for Acme Co" })).toBeInTheDocument();
  });

  it("renders the emptyMessage in card view when there are no rows", () => {
    mockViewport(true);
    render(
      <DataTable
        columns={COLUMNS}
        rows={[]}
        getRowId={(row) => row.id}
        mobileCard={MOBILE_CARD}
        emptyMessage="Nothing here."
      />,
    );

    expect(screen.getByText("Nothing here.")).toBeInTheDocument();
  });

  it("calls onRowClick when a card is clicked or activated via keyboard", async () => {
    mockViewport(true);
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    render(
      <DataTable
        columns={COLUMNS}
        rows={ROWS}
        getRowId={(row) => row.id}
        mobileCard={MOBILE_CARD}
        onRowClick={onRowClick}
      />,
    );

    const card = screen.getByText("Acme Co").closest('[role="button"]')!;
    await user.click(card);
    expect(onRowClick).toHaveBeenCalledWith(ROWS[0]);

    card.focus();
    await user.keyboard("{Enter}");
    expect(onRowClick).toHaveBeenCalledTimes(2);
  });

  it("does not propagate a click on the actions area to the card's own onRowClick", async () => {
    mockViewport(true);
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    render(
      <DataTable
        columns={COLUMNS}
        rows={ROWS}
        getRowId={(row) => row.id}
        mobileCard={MOBILE_CARD}
        onRowClick={onRowClick}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Actions for Acme Co" }));
    expect(onRowClick).not.toHaveBeenCalled();
  });
});
