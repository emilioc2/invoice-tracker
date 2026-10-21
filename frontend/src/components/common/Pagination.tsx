import { Button } from "@/components/ui/button";

interface PaginationProps {
  pageNumber: number;
  totalPages: number;
  onPageChange: (pageNumber: number) => void;
}

/** Simple prev/next + "page X of Y" — no page-number jump list, no size selector. */
export function Pagination({ pageNumber, totalPages, onPageChange }: PaginationProps) {
  const hasPrevious = pageNumber > 1;
  const hasNext = pageNumber < totalPages;

  return (
    <div className="flex items-center justify-between">
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={!hasPrevious}
        onClick={() => onPageChange(pageNumber - 1)}
      >
        Previous
      </Button>
      <span className="text-sm text-muted-foreground">
        Page {pageNumber} of {Math.max(totalPages, 1)}
      </span>
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={!hasNext}
        onClick={() => onPageChange(pageNumber + 1)}
      >
        Next
      </Button>
    </div>
  );
}
