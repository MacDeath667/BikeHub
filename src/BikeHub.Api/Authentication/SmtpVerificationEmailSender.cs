using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Authentication;

public sealed class SmtpVerificationEmailSender(IOptions<AuthOptions> configuration) : IVerificationEmailSender
{
    public async Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
    {
        var options = configuration.Value.Smtp;
        using var mail = VerificationEmail.Create(options.FromAddress, options.FromName, message);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var client = new SmtpClient { Timeout = 10_000 };
        var stage = "connect";
        try
        {
            await client.ConnectAsync(options.Host, options.Port,
                options.Security == "SslOnConnect" ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, timeout.Token);
            stage = "authenticate";
            // One password attempt over verified TLS. Automatic fallback can hide a 535 rejection
            // behind a disconnect while trying a second mechanism with the same credentials.
            await client.AuthenticateAsync(PasswordMechanism(client.AuthenticationMechanisms, options.Username, options.Password), timeout.Token);
            stage = "send";
            await client.SendAsync(mail, timeout.Token);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            throw EmailTransportException.From(stage, error);
        }
        // The server has accepted the email. A QUIT/connection cleanup failure must not invalidate its code.
        try { await client.DisconnectAsync(true, timeout.Token); }
        catch (Exception error) when (error is IOException or MailKit.Net.Smtp.SmtpProtocolException or OperationCanceledException) { }
    }

    public static SaslMechanism PasswordMechanism(ISet<string> supported, string username, string password) =>
        supported.Contains("PLAIN") ? new SaslMechanismPlain(username, password) :
        supported.Contains("LOGIN") ? new SaslMechanismLogin(username, password) :
        throw new NotSupportedException("The SMTP server does not support password authentication over TLS.");
}
