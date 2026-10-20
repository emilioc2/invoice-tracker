using System.Globalization;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InvoiceTracker.Infrastructure.Pdf;

/// <summary>
/// Renders a simple, legible one-page invoice: a "from" (business) / "bill to" (client) header,
/// the invoice number/dates/status, a line-items table, and the subtotal/tax/total summary. This
/// is a personal/portfolio project, so the layout favors clarity over design polish — no logo
/// upload, multi-page pagination handling for very long line-item lists is left to QuestPDF's
/// own automatic page-breaking rather than anything bespoke.
/// </summary>
public class QuestPdfInvoiceGenerator : IPdfGenerator
{
    public Task<byte[]> GenerateInvoicePdfAsync(
        Invoice invoice,
        Client client,
        Business business,
        CancellationToken cancellationToken = default)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(headerContainer => ComposeHeader(headerContainer, invoice, client, business));
                page.Content().Element(contentContainer => ComposeContent(contentContainer, invoice));
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });

        var bytes = document.GeneratePdf();
        return Task.FromResult(bytes);
    }

    private static void ComposeHeader(IContainer container, Invoice invoice, Client client, Business business)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(fromColumn =>
                {
                    fromColumn.Item().Text(business.Name).FontSize(16).Bold();
                    fromColumn.Item().Text(business.Email).FontSize(10);
                });

                row.RelativeItem().Column(invoiceInfoColumn =>
                {
                    invoiceInfoColumn.Item().AlignRight().Text("INVOICE").FontSize(20).Bold();
                    invoiceInfoColumn.Item().AlignRight().Text(invoice.InvoiceNumber).FontSize(12);
                    invoiceInfoColumn.Item().AlignRight().Text(invoice.Status.ToString());
                });
            });

            column.Item().PaddingTop(15).LineHorizontal(1);

            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(billToColumn =>
                {
                    billToColumn.Item().Text("Bill To").FontSize(11).Bold();
                    billToColumn.Item().Text(client.Name);
                    billToColumn.Item().Text(client.Email);
                    if (!string.IsNullOrWhiteSpace(client.Address))
                    {
                        billToColumn.Item().Text(client.Address);
                    }
                });

                row.RelativeItem().Column(datesColumn =>
                {
                    datesColumn.Item().AlignRight().Text(text =>
                    {
                        text.Span("Created: ").SemiBold();
                        text.Span(invoice.CreatedAtDate.ToString("yyyy-MM-dd"));
                    });

                    datesColumn.Item().AlignRight().Text(text =>
                    {
                        text.Span("Issue Date: ").SemiBold();
                        text.Span(invoice.IssueDate?.ToString("yyyy-MM-dd") ?? "-");
                    });

                    datesColumn.Item().AlignRight().Text(text =>
                    {
                        text.Span("Due Date: ").SemiBold();
                        text.Span(invoice.DueDate.ToString("yyyy-MM-dd"));
                    });
                });
            });
        });
    }

    private static void ComposeContent(IContainer container, Invoice invoice)
    {
        container.PaddingTop(20).Column(column =>
        {
            column.Item().Element(contentContainer => ComposeLineItemsTable(contentContainer, invoice));

            column.Item().PaddingTop(10).AlignRight().Width(220).Column(summaryColumn =>
            {
                summaryColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text("Subtotal");
                    row.ConstantItem(100).AlignRight().Text(FormatCurrency(invoice.Subtotal));
                });

                summaryColumn.Item().Row(row =>
                {
                    // TaxRate is stored as a 0-1 fraction (e.g. 0.085 for 8.5%), per
                    // CreateInvoiceCommandValidator's "must be between 0 and 1" rule — scale by
                    // 100 for display.
                    row.RelativeItem().Text($"Tax ({(invoice.TaxRate * 100).ToString("0.##", CultureInfo.InvariantCulture)}%)");
                    row.ConstantItem(100).AlignRight().Text(FormatCurrency(invoice.TaxAmount));
                });

                summaryColumn.Item().PaddingTop(5).LineHorizontal(1);

                summaryColumn.Item().PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Text("Total").Bold();
                    row.ConstantItem(100).AlignRight().Text(FormatCurrency(invoice.Total)).Bold();
                });
            });

            if (!string.IsNullOrWhiteSpace(invoice.Notes))
            {
                column.Item().PaddingTop(25).Column(notesColumn =>
                {
                    notesColumn.Item().Text("Notes").FontSize(11).Bold();
                    notesColumn.Item().Text(invoice.Notes);
                });
            }
        });
    }

    private static void ComposeLineItemsTable(IContainer container, Invoice invoice)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4);
                columns.RelativeColumn(1);
                columns.RelativeColumn(1.5f);
                columns.RelativeColumn(1.5f);
            });

            table.Header(header =>
            {
                header.Cell().Text("Description").Bold();
                header.Cell().AlignRight().Text("Qty").Bold();
                header.Cell().AlignRight().Text("Unit Price").Bold();
                header.Cell().AlignRight().Text("Line Total").Bold();

                header.Cell().ColumnSpan(4).PaddingTop(5).BorderBottom(1);
            });

            foreach (var lineItem in invoice.LineItems)
            {
                table.Cell().PaddingVertical(3).Text(lineItem.Description);
                table.Cell().PaddingVertical(3).AlignRight().Text(lineItem.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
                table.Cell().PaddingVertical(3).AlignRight().Text(FormatCurrency(lineItem.UnitPrice));
                table.Cell().PaddingVertical(3).AlignRight().Text(FormatCurrency(lineItem.LineTotal));
            }
        });
    }

    // Formats amounts as "$0.00" using InvariantCulture rather than decimal.ToString("C") with the
    // current/server culture — this project has no multi-currency support (explicitly out of
    // scope per requirements.md) and PDFs are rendered server-side on an EC2 host whose OS locale
    // isn't something this app controls, so a hard-coded "$" avoids amounts silently rendering as
    // "£"/"¤"/etc. depending on wherever this happens to run.
    private static string FormatCurrency(decimal amount) => $"${amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
