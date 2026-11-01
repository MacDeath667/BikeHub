using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Authentication;

public sealed class SmtpVerificationEmailSender(IOptions<AuthOptions> configuration) : IVerificationEmailSender
{
    private const string ConnectStage = "connect";
    private const string PlainMechanism = "PLAIN";
    private const string LoginMechanism = "LOGIN";
    private const string UnsupportedAuthenticationMessage = "The SMTP server does not support password authentication over TLS.";
    private const int OperationTimeoutSeconds = 15;
    private const int ClientTimeoutMilliseconds = 10_000;

    public async Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
    {
        var options = configuration.Value.Smtp;
        using var mail = VerificationEmail.Create(options.FromAddress, options.FromName, message);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        using var client = new SmtpClient { Timeout = ClientTimeoutMilliseconds };
        var stage = ConnectStage;
        try
        {
            await client.ConnectAsync(options.Host, options.Port,
                options.Security == Constants.SmtpSecurity.SslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, timeout.Token);
            stage = Constants.EmailTransport.AuthenticateStage;
            // One password attempt over verified TLS. Automatic fallback can hide a 535 rejection
            // behind a disconnect while trying a second mechanism with the same credentials.
            await client.AuthenticateAsync(PasswordMechanism(client.AuthenticationMechanisms, options.Username, options.Password), timeout.Token);
            stage = Constants.EmailTransport.SendStage;
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
        supported.Contains(PlainMechanism) ? new SaslMechanismPlain(username, password) :
        supported.Contains(LoginMechanism) ? new SaslMechanismLogin(username, password) :
        throw new NotSupportedException(UnsupportedAuthenticationMessage);
}
