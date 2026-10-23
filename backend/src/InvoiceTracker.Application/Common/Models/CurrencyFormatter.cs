using System.Globalization;

namespace InvoiceTracker.Application.Common.Models;

/// <summary>
/// Shared currency-formatting helper, used by both the QuestPDF invoice generator
/// (<c>QuestPdfInvoiceGenerator</c>, in Infrastructure) and the invoice email bodies
/// (<see cref="InvoiceTracker.Application.Invoices.Common.InvoiceEmailContentBuilder"/>, in this
/// same layer) so a client sees the exact same "Amount due" formatting whether they're looking at
/// the PDF or the email. Lives in Application rather than Infrastructure because Application can't
/// depend on Infrastructure (wrong direction per this project's Clean Architecture layering), while
/// Infrastructure is allowed to depend on Application.
///
/// Formats amounts as "R 0,00" (ZAR, en-ZA style: Rand symbol + space, comma decimal separator,
/// space-grouped thousands) to match the frontend's Intl.NumberFormat("en-ZA", { currency: "ZAR" })
/// output — this project has no multi-currency support (explicitly out of scope per
/// requirements.md) and PDFs are rendered server-side on an EC2 host whose OS locale isn't
/// something this app controls, so hard-coding en-ZA here avoids amounts silently rendering in
/// the wrong currency/format depending on wherever this happens to run. Note:
/// decimal.ToString("C", en-ZA) renders "R250,00" with no space after the symbol, which doesn't
/// match the frontend's "R 250,00" — so the symbol/space is applied manually around "N2".
/// </summary>
public static class CurrencyFormatter
{
    public static string Format(decimal amount) =>
        $"R {amount.ToString("N2", CultureInfo.GetCultureInfo("en-ZA"))}";
}
