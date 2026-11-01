using BikeHub.Mail.Contracts;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;

namespace BikeHub.Mail.Transports;

public sealed class SmtpEmailTransport(IOptions<SmtpOptions> smtpConfiguration, IHostEnvironment environment) : IEmailTransport
{
    private const string ConnectStage = "connect";
    private const string PlainMechanism = "PLAIN";
    private const string LoginMechanism = "LOGIN";
    private const string UnsupportedAuthenticationMessage = "The SMTP server does not support password authentication over TLS.";
    private const int OperationTimeoutSeconds = 15;
    private const int ClientTimeoutMilliseconds = 10_000;
    private const string UnsafeSmtpMessage = "Unencrypted SMTP is restricted to Development without credentials.";

    public async Task SendAsync(SendEmailRequest message, CancellationToken cancellationToken)
    {
        var options = smtpConfiguration.Value;
        var socketOptions = SocketOptions(options, environment.IsDevelopment());
        using var mail = EmailMessage.Create(options.FromAddress, options.FromName, message);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        using var client = new SmtpClient { Timeout = ClientTimeoutMilliseconds };
        var stage = ConnectStage;
        try
        {
            await client.ConnectAsync(options.Host, options.Port, socketOptions, timeout.Token);
            if (socketOptions != SecureSocketOptions.None)
            {
                stage = Constants.EmailTransport.AuthenticateStage;
                // One password attempt over verified TLS, without fallback to another mechanism.
                await client.AuthenticateAsync(PasswordMechanism(client.AuthenticationMechanisms, options.Username, options.Password), timeout.Token);
            }
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

    public static bool AnonymousDevelopment(SmtpOptions options, bool development) =>
        development && options.Security == Constants.SmtpSecurity.None &&
        string.IsNullOrEmpty(options.Username) && string.IsNullOrEmpty(options.Password);

    public static SecureSocketOptions SocketOptions(SmtpOptions options, bool development) => options.Security switch
    {
        Constants.SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        Constants.SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        Constants.SmtpSecurity.None when AnonymousDevelopment(options, development) => SecureSocketOptions.None,
        _ => throw new InvalidOperationException(UnsafeSmtpMessage)
    };
}
