namespace InvoiceTracker.Domain.Entities;

public class Client
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTime CreatedAtDate { get; set; }
    public DateTime LastModifiedDate { get; set; }

    // null = active, non-null = archived (and records when). Never hard-deleted.
    public DateTime? ArchivedAt { get; set; }
}
