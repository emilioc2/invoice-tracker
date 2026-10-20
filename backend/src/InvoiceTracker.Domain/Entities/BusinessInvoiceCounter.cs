namespace InvoiceTracker.Domain.Entities;

// One row per business. Incremented under row-level locking within the same
// transaction as invoice creation to generate per-business invoice numbers
// (e.g. INV-0001, INV-0002) race-safely. The locking/incrementing logic
// itself is Application-layer work for a later CRUD step.
public class BusinessInvoiceCounter
{
    public Guid BusinessId { get; set; }
    public int LastNumber { get; set; }
}
