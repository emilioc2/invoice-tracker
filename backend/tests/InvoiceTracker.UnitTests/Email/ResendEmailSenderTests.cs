using System.Net;
using System.Text.Json;
using FluentAssertions;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvoiceTracker.UnitTests.Email;

/// <summary>
/// Unit tests for <see cref="ResendEmailSender"/> per architecture.md's test plan: builds a
/// correct JSON request with base64-encoded attachments when present (and without when absent) —
/// against a fake <see cref="HttpMessageHandler"/>, never a real Resend call.
/// </summary>
public class ResendEmailSenderTests
{
    private const string ApiKey = "test-resend-api-key";
    private const string FromAddress = "onboarding@resend.dev";

    /// <summary>
    /// Captures the single outgoing request's shape (method, headers, JSON body) for assertions.
    /// </summary>
    private class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public JsonElement CapturedBody { get; private set; }
        public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;

            if (request.Content is not null)
            {
                var json = await request.Content.ReadAsStringAsync(cancellationToken);
                CapturedBody = JsonSerializer.Deserialize<JsonElement>(json);
            }

            return new HttpResponseMessage(ResponseStatusCode);
        }
    }

    private static ResendEmailSender CreateSender(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new ResendSettings { ApiKey = ApiKey, FromAddress = FromAddress });
        return new ResendEmailSender(httpClient, options, NullLogger<ResendEmailSender>.Instance);
    }

    [Fact]
    public async Task NoAttachments_PostsJson_WithoutAttachmentsField()
    {
        var handler = new RecordingHandler();
        var sender = CreateSender(handler);

        await sender.SendAsync("client@example.com", "Subject", "Body", Array.Empty<EmailAttachment>(), CancellationToken.None);

        handler.CapturedRequest!.RequestUri!.ToString().Should().Be("https://api.resend.com/emails");
        handler.CapturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.CapturedRequest.Headers.Authorization!.Parameter.Should().Be(ApiKey);

        handler.CapturedBody.GetProperty("from").GetString().Should().Be(FromAddress);
        handler.CapturedBody.GetProperty("to")[0].GetString().Should().Be("client@example.com");
        handler.CapturedBody.GetProperty("subject").GetString().Should().Be("Subject");
        handler.CapturedBody.GetProperty("text").GetString().Should().Be("Body");
        handler.CapturedBody.TryGetProperty("attachments", out var attachments).Should().BeTrue();
        attachments.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task WithAttachments_PostsJson_WithBase64EncodedAttachmentContent()
    {
        var handler = new RecordingHandler();
        var sender = CreateSender(handler);

        var attachment = new EmailAttachment("INV-0001.pdf", new byte[] { 1, 2, 3 }, "application/pdf");

        await sender.SendAsync("client@example.com", "Subject", "Body", new[] { attachment }, CancellationToken.None);

        var attachments = handler.CapturedBody.GetProperty("attachments");
        attachments.GetArrayLength().Should().Be(1);

        var capturedAttachment = attachments[0];
        capturedAttachment.GetProperty("filename").GetString().Should().Be("INV-0001.pdf");
        capturedAttachment.GetProperty("content_type").GetString().Should().Be("application/pdf");
        capturedAttachment.GetProperty("content").GetString().Should().Be(Convert.ToBase64String(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task WithMultipleAttachments_EachIsIncludedInTheSameJsonArray()
    {
        var handler = new RecordingHandler();
        var sender = CreateSender(handler);

        var attachments = new[]
        {
            new EmailAttachment("first.pdf", new byte[] { 1 }, "application/pdf"),
            new EmailAttachment("second.pdf", new byte[] { 2 }, "application/pdf")
        };

        await sender.SendAsync("client@example.com", "Subject", "Body", attachments, CancellationToken.None);

        var capturedAttachments = handler.CapturedBody.GetProperty("attachments");
        capturedAttachments.GetArrayLength().Should().Be(2);
        capturedAttachments[0].GetProperty("filename").GetString().Should().Be("first.pdf");
        capturedAttachments[1].GetProperty("filename").GetString().Should().Be("second.pdf");
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
