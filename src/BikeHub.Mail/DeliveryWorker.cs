using BikeHub.Mail.Contracts;
using BikeHub.Mail.Persistence;
using BikeHub.Mail.Transports;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BikeHub.Mail;

public sealed class DeliveryWorker(MailQueue queue, IEmailTransport transport, TimeProvider clock,
    IOptions<MailOptions> options, ILogger<DeliveryWorker> logger) : BackgroundService
{
    private const string WorkerFailureLog = "Mail worker failure ({FailureType})";
    private const string DeliveryLog = "Mail {MessageId}: {Status}; attempt={Attempt}; reason={Reason}";
    private const string UnknownFailure = "delivery_result_unknown";
    private const string SmtpCommandFailure = nameof(MailKit.Net.Smtp.SmtpCommandException);
    private static readonly ActivitySource Activities = new(Constants.ApplicationName);
    private static readonly Meter Meter = new(Constants.ApplicationName);
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("bikehub.mail.deliveries");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await queue.RecoverStaleAsync(stoppingToken);
                if (!await ProcessOneAsync(stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception error)
            {
                logger.LogError(WorkerFailureLog, error.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        var message = await queue.ClaimAsync(cancellationToken);
        if (message is null)
        {
            return false;
        }
        var status = DeliveryStatus.Sent;
        using var activity = Activities.StartActivity("mail.send");
        string? reason = null;
        var notBefore = clock.GetUtcNow();
        if (message.ExpiresAt <= notBefore)
        {
            status = DeliveryStatus.Expired;
        }
        else
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Constants.SendTimeoutSeconds));
                await transport.SendAsync(message.ToRequest(), timeout.Token);
            }
            catch (Exception error)
            {
                reason = error is EmailTransportException failure ? failure.Reason : UnknownFailure;
                var uncertain = error is not EmailTransportException known ||
                    (known.Stage == Constants.EmailTransport.SendStage &&
                     (known.SmtpStatus is null ||
                      (known.SmtpStatus >= 500 && known.FailureType != SmtpCommandFailure) ||
                      known.Reason is Constants.EmailTransport.Timeout or Constants.EmailTransport.ConnectionError or Constants.EmailTransport.ProtocolError));
                if (uncertain)
                {
                    status = DeliveryStatus.Unknown;
                }
                else
                {
                    var retryable = reason is Constants.EmailTransport.Timeout or Constants.EmailTransport.ConnectionError or Constants.EmailTransport.GmailRateLimited ||
                        (error is EmailTransportException { SmtpStatus: >= 500 } beforeSend &&
                         beforeSend.Stage != Constants.EmailTransport.SendStage && reason != Constants.EmailTransport.AuthenticationRejected) ||
                        error is EmailTransportException { Reason: Constants.EmailTransport.SmtpRejected, SmtpStatus: >= 400 and < 500 };
                    notBefore = clock.GetUtcNow().AddSeconds(options.Value.RetrySeconds * Math.Pow(2, message.Attempts - 1));
                    status = retryable && message.Attempts < options.Value.MaxAttempts &&
                        (message.ExpiresAt is null || message.ExpiresAt > notBefore)
                        ? DeliveryStatus.Queued : DeliveryStatus.Failed;
                }
            }
        }
        // A client disconnect or host shutdown must not erase the known outcome of an attempted send.
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await queue.CompleteAsync(message.Id, status, reason, notBefore, completion.Token);
        logger.LogInformation(DeliveryLog, message.Id, status, message.Attempts, reason);
        Deliveries.Add(1, new KeyValuePair<string, object?>("status", status.ToString()));
        return true;
    }
}
