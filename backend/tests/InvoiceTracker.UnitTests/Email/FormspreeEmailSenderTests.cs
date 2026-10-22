using System.Net;
using FluentAssertions;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvoiceTracker.UnitTests.Email;

/// <summary>
/// Unit tests for <see cref="FormspreeEmailSender"/> per architecture.md's test plan: builds a
/// correct <c>multipart/form-data</c> request when attachments are present, and the original plain
/// JSON POST when there are none — against a fake <see cref="HttpMessageHandler"/>, never a real
/// Formspree call.
/// </summary>
public class FormspreeEmailSenderTests
{
    private const string Endpoint = "https://formspree.io/f/test-form-id";

    private record CapturedPart(string? Name, string? FileName, string? ContentType);

    /// <summary>
    /// Captures the single outgoing request's shape and returns a canned response for it.
    /// Deliberately snapshots everything it needs (content-type, and per-part name/filename/
    /// content-type for multipart requests) *during* <see cref="SendAsync"/>, while the request's
    /// <see cref="MultipartFormDataContent"/> is still alive — the production code disposes it
    /// (<c>using var content = ...</c>) immediately after this call returns, and
    /// <c>MultipartFormDataContent.Dispose()</c> actually clears its nested-content list, so
    /// inspecting <c>HttpRequestMessage.Content</c> again *after* awaiting the call would otherwise
    /// see an empty collection.
    /// </summary>
    private class RecordingHandler : HttpMessageHandler
    {
        public string? CapturedContentType { get; private set; }
        public IReadOnlyList<CapturedPart> CapturedParts { get; private set; } = Array.Empty<CapturedPart>();
        public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedContentType = request.Content?.Headers.ContentType?.MediaType;

            if (request.Content is MultipartFormDataContent multipart)
            {
                CapturedParts = multipart
                    .Select(part => new CapturedPart(
                        part.Headers.ContentDisposition?.Name?.Trim('"'),
                        part.Headers.ContentDisposition?.FileName?.Trim('"'),
                        part.Headers.ContentType?.MediaType))
                    .ToList();
            }

            return Task.FromResult(new HttpResponseMessage(ResponseStatusCode));
        }
    }

    private static FormspreeEmailSender CreateSender(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FormspreeSettings { FormEndpoint = Endpoint });
        return new FormspreeEmailSender(httpClient, options, NullLogger<FormspreeEmailSender>.Instance);
    }

    [Fact]
    public async Task NoAttachments_PostsPlainJson()
    {
        var handler = new RecordingHandler();
        var sender = CreateSender(handler);

        await sender.SendAsync("client@example.com", "Subject", "Body", Array.Empty<EmailAttachment>(), CancellationToken.None);

        handler.CapturedContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task WithAttachments_PostsMultipartFormData_IncludingTheFileParts()
    {
        var handler = new RecordingHandler();
        var sender = CreateSender(handler);

        var attachment = new EmailAttachment("INV-0001.pdf", new byte[] { 1, 2, 3 }, "application/pdf");

        await sender.SendAsync("client@example.com", "Subject", "Body", new[] { attachment }, CancellationToken.None);

        handler.CapturedContentType.Should().Be("multipart/form-data");

        var parts = handler.CapturedParts;

        // Text fields.
        parts.Should().Contain(p => p.Name == "email");
        parts.Should().Contain(p => p.Name == "subject");
        parts.Should().Contain(p => p.Name == "message");

        // File part: named "attachment0" (indexed field name — see FormspreeEmailSender's doc
        // comment for why), with the original filename preserved.
        var filePart = parts.Single(p => p.Name == "attachment0");
        filePart.FileName.Should().Be("INV-0001.pdf");
        filePart.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task WithMultipleAttachments_EachGetsItsOwnIndexedFieldName()
    {
        var handler = new RecordingHandler();
        var sender = CreateSender(handler);

        var attachments = new[]
        {
            new EmailAttachment("first.pdf", new byte[] { 1 }, "application/pdf"),
            new EmailAttachment("second.pdf", new byte[] { 2 }, "application/pdf")
        };

        await sender.SendAsync("client@example.com", "Subject", "Body", attachments, CancellationToken.None);

        var parts = handler.CapturedParts;

        parts.Should().Contain(p => p.Name == "attachment0" && p.FileName == "first.pdf");
        parts.Should().Contain(p => p.Name == "attachment1" && p.FileName == "second.pdf");
    }

    [Fact]
    public async Task NonSuccessStatusCode_Throws_WithAttachments()
    {
        var handler = new RecordingHandler { ResponseStatusCode = HttpStatusCode.InternalServerError };
        var sender = CreateSender(handler);

        var attachment = new EmailAttachment("INV-0001.pdf", new byte[] { 1 }, "application/pdf");

        var act = () => sender.SendAsync("client@example.com", "Subject", "Body", new[] { attachment }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task NonSuccessStatusCode_Throws_WithoutAttachments()
    {
        var handler = new RecordingHandler { ResponseStatusCode = HttpStatusCode.NotFound };
        var sender = CreateSender(handler);

        var act = () => sender.SendAsync("client@example.com", "Subject", "Body", Array.Empty<EmailAttachment>(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
