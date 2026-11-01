using System.Text.RegularExpressions;
using MailKit.Net.Smtp;

namespace BikeHub.Api.Authentication;

// Keep raw SMTP exceptions out of logs: their messages can contain account details.
public sealed class EmailTransportException(string stage, string failureType, string reason,
    int? smtpStatus, string? enhancedStatus) : Exception("Verification email transport failed.")
{
    public string Stage { get; } = stage;
    public string FailureType { get; } = failureType;
    public string Reason { get; } = reason;
    public int? SmtpStatus { get; } = smtpStatus;
    public string? EnhancedStatus { get; } = enhancedStatus;

    public static EmailTransportException From(string stage, Exception error)
    {
        var statusMatch = Regex.Match(error.Message, @"\b([45]\d{2})[: -]");
        int? status = error is SmtpCommandException command ? (int)command.StatusCode
            : statusMatch.Success ? int.Parse(statusMatch.Groups[1].Value) : null;
        var enhanced = Regex.Match(error.Message, @"\b[45]\.\d\.\d{1,3}\b");
        var reason = error switch
        {
            MailKit.Security.AuthenticationException => "auth_rejected",
            _ when stage == "authenticate" && status is 534 or 535 => "auth_rejected",
            OperationCanceledException or TimeoutException => "timeout",
            MailKit.Security.SslHandshakeException => "tls_error",
            SmtpProtocolException => "protocol_error",
            SmtpCommandException => "smtp_rejected",
            IOException or System.Net.Sockets.SocketException => "connection_error",
            _ => "transport_error"
        };
        return new(stage, error.GetType().Name, reason, status, enhanced.Success ? enhanced.Value : null);
    }
}
