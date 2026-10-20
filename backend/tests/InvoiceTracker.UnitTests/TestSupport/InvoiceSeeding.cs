using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;

namespace InvoiceTracker.UnitTests.TestSupport;

/// <summary>
/// Shared entity-building helpers for unit tests that need a minimally-valid Business/Client/
/// Invoice graph to exercise command handlers against EF Core's in-memory provider. Mirrors the
/// seeding shape already used by <c>OverdueInvoiceScannerServiceTests</c>, factored out here so the
/// status-transition/totals/numbering handler tests don't each re-declare it.
/// </summary>
public static class InvoiceSeeding
{
    public static Business MakeBusiness(string name = "Acme Co", string email = "acme@example.com") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Email = email
    };

    public static Client MakeClient(Guid businessId, string name = "Wayne Enterprises") => new()
    {
        Id = Guid.NewGuid(),
        BusinessId = businessId,
        Name = name,
        Email = "billing@wayne.example",
        Phone = "555-1234",
        Address = "1 Wayne Way",
        CreatedAtDate = DateTime.UtcNow,
        LastModifiedDate = DateTime.UtcNow
    };

    public static Invoice MakeInvoice(
        Guid businessId,
        Guid clientId,
        InvoiceStatus status,
        string invoiceNumber = "INV-0001",
        DateTime? dueDate = null)
    {
        var createdAt = DateTime.UtcNow.AddDays(-10);
        return new Invoice
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            ClientId = clientId,
            InvoiceNumber = invoiceNumber,
            CreatedAtDate = createdAt,
            // Draft invoices never have an IssueDate yet (see requirements.md); everything else
            // in this fixture has already been "sent" at some point.
            IssueDate = status == InvoiceStatus.Draft ? null : createdAt,
            DueDate = dueDate ?? DateTime.UtcNow.AddDays(10),
            Status = status,
            Subtotal = 1000m,
            TaxRate = 0m,
            TaxAmount = 0m,
            Total = 1000m,
            LastModifiedDate = createdAt
        };
    }
}
