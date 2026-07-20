using System.Net.Mail;

namespace Part12_ElectiveBranches;

public sealed class SmtpEmailClient(IConfiguration configuration, ILogger<SmtpEmailClient> logger) : IAsyncDisposable
{
    private readonly string _host = configuration["Notifications:SmtpHost"] ?? "localhost";
    private readonly int _port = configuration.GetValue("Notifications:SmtpPort", 1125);
    private readonly ILogger<SmtpEmailClient> _logger = logger;
    private SmtpClient? _client;
    private int _disposeState;

    private async Task<SmtpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is { } existing)
        {
            return existing;
        }
        var client = new SmtpClient(_host, _port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = false,
            Timeout = 5000,
        };
        _client = client;
        return await Task.FromResult(client);
    }

    public async Task<string> SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        string? textBody,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var message = new MailMessage
        {
            From = new MailAddress("campus@example.test", "Campus LearnAspNet"),
            Subject = subject,
            SubjectEncoding = System.Text.Encoding.UTF8,
            Body = htmlBody,
            BodyEncoding = System.Text.Encoding.UTF8,
            IsBodyHtml = true,
        };
        message.To.Add(new MailAddress(recipient));
        message.Headers.Add("X-Correlation-Id", correlationId);
        if (!string.IsNullOrEmpty(textBody))
        {
            var alternate = AlternateView.CreateAlternateViewFromString(
                textBody, System.Text.Encoding.UTF8, "text/plain");
            message.AlternateViews.Add(alternate);
        }

        var client = await GetClientAsync(cancellationToken);
        await client.SendMailAsync(message, cancellationToken);
        _logger.LogInformation(
            "Sent email to {Recipient} subject {Subject} correlation {CorrelationId}",
            recipient, subject, correlationId);
        return Guid.NewGuid().ToString("N");
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }
}
