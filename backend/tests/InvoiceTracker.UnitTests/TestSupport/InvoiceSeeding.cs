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
        var invoice = new Invoice
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
            Subtotal = 1000m,
            TaxRate = 0m,
            TaxAmount = 0m,
            Total = 1000m,
            LastModifiedDate = createdAt
        };

        // Status's setter is private (see Invoice.TransitionTo) — fast-forward a freshly
        // constructed (Draft) invoice to the requested starting status via legal transitions
        // only, which this project's status workflow makes reachable for every enum value
        // within at most two steps from Draft.
        FastForwardTo(invoice, status);

        return invoice;
    }

    private static void FastForwardTo(Invoice invoice, InvoiceStatus target)
    {
        if (target == InvoiceStatus.Draft)
        {
            return;
        }

        invoice.TransitionTo(InvoiceStatus.Unpaid);

        if (target != InvoiceStatus.Unpaid)
        {
            invoice.TransitionTo(target);
        }
    }
}
