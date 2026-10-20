namespace InvoiceTracker.Application.Common.Models;

/// <summary>
/// A page of results plus enough metadata for a client to render pagination controls. Used by
/// every list query (<c>ListClientsQuery</c>, <c>ListInvoicesQuery</c>, ...).
/// </summary>
public class PaginatedResult<T>
{
    public PaginatedResult(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; }

    public int TotalCount { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
