using System.Text.RegularExpressions;
using MailKit.Net.Smtp;

namespace BikeHub.Api.Authentication;

// Keep raw SMTP exceptions out of logs: their messages can contain account details.
public sealed class EmailTransportException(string stage, string failureType, string reason,
    int? smtpStatus, string? enhancedStatus) : Exception(TransportFailureMessage)
{
    private const string TransportFailureMessage = "Verification email transport failed.";
    private const string StatusPattern = @"\b([45]\d{2})[: -]";
    private const string EnhancedStatusPattern = @"\b[45]\.\d\.\d{1,3}\b";
    private const string TimeoutReason = "timeout";
    private const string TlsErrorReason = "tls_error";
    private const string ProtocolErrorReason = "protocol_error";
    private const string SmtpRejectedReason = "smtp_rejected";
    private const string ConnectionErrorReason = "connection_error";
    private const string TransportErrorReason = "transport_error";

    public string Stage { get; } = stage;
    public string FailureType { get; } = failureType;
    public string Reason { get; } = reason;
    public int? SmtpStatus { get; } = smtpStatus;
    public string? EnhancedStatus { get; } = enhancedStatus;

    public static EmailTransportException From(string stage, Exception error)
    {
        var statusMatch = Regex.Match(error.Message, StatusPattern);
        int? status = error is SmtpCommandException command ? (int)command.StatusCode
            : statusMatch.Success ? int.Parse(statusMatch.Groups[1].Value) : null;
        var enhanced = Regex.Match(error.Message, EnhancedStatusPattern);
        var reason = error switch
        {
            MailKit.Security.AuthenticationException => Constants.EmailTransport.AuthenticationRejected,
            _ when stage == Constants.EmailTransport.AuthenticateStage && status is 534 or 535 => Constants.EmailTransport.AuthenticationRejected,
            OperationCanceledException or TimeoutException => TimeoutReason,
            MailKit.Security.SslHandshakeException => TlsErrorReason,
            SmtpProtocolException => ProtocolErrorReason,
            SmtpCommandException => SmtpRejectedReason,
            IOException or System.Net.Sockets.SocketException => ConnectionErrorReason,
            _ => TransportErrorReason
        };
        return new(stage, error.GetType().Name, reason, status, enhanced.Success ? enhanced.Value : null);
    }
}
