namespace Part12_ElectiveBranches;

public sealed class EmailSchedulerService : BackgroundService
{
    private readonly EmailJobStore _store;
    private readonly SmtpEmailClient _smtp;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailSchedulerService> _logger;
    private readonly int _maxAttempts;
    private readonly int _baseBackoffMs;
    private readonly int _maxBackoffMs;
    private readonly TimeSpan _pollInterval;

    public EmailSchedulerService(
        EmailJobStore store,
        SmtpEmailClient smtp,
        IConfiguration configuration,
        ILogger<EmailSchedulerService> logger)
    {
        _store = store;
        _smtp = smtp;
        _configuration = configuration;
        _logger = logger;
        _maxAttempts = configuration.GetValue("Notifications:MaxAttempts", 5);
        _baseBackoffMs = configuration.GetValue("Notifications:BaseBackoffMs", 500);
        _maxBackoffMs = configuration.GetValue("Notifications:MaxBackoffMs", 30000);
        var pollMs = configuration.GetValue("Notifications:PollIntervalMs", 2000);
        _pollInterval = TimeSpan.FromMilliseconds(pollMs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runScheduler = _configuration.GetValue("Notifications:RunScheduler", true);
        if (!runScheduler)
        {
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var row = await _store.AcquireNextAsync(stoppingToken);
                if (row is not null)
                {
                    await ProcessJobAsync(row, stoppingToken);
                }
                else
                {
                    await Task.Delay(_pollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email scheduler iteration failed");
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(EmailJobRow row, CancellationToken cancellationToken)
    {
        if (row.Attempts > _maxAttempts)
        {
            _logger.LogWarning("Email job {JobId} exceeded max attempts {Max}", row.JobId, _maxAttempts);
            return;
        }
        try
        {
            var correlationId = row.JobId.ToString("N");
            var providerId = await _smtp.SendAsync(
                row.Recipient, row.Subject, row.HtmlBody, row.TextBody, correlationId, cancellationToken);
            await _store.MarkCompletedAsync(row.JobId, providerId, cancellationToken);
            _logger.LogInformation("Email job {JobId} completed on attempt {Attempt}", row.JobId, row.Attempts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email job {JobId} failed on attempt {Attempt}", row.JobId, row.Attempts);
            await _store.MarkFailedAsync(row.JobId, cancellationToken);
            var backoff = Math.Min(_baseBackoffMs * (int)Math.Pow(2, row.Attempts), _maxBackoffMs);
            await Task.Delay(backoff, cancellationToken);
        }
    }
}
