using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace BikeHub.Api.Authentication;

public sealed class SmtpVerificationEmailSender(IOptions<AuthOptions> configuration) : IVerificationEmailSender
{
    public async Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
    {
        var options = configuration.Value.Smtp;
        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mail.To.Add(MailboxAddress.Parse(message.Destination));
        mail.MessageId = MimeUtils.GenerateMessageId(options.FromAddress.Split('@')[1]);
        mail.Subject = "Код для входа в BikeHub";
        mail.Body = new TextPart("plain")
        {
            Text = $"Код для входа в BikeHub: {message.Code}\n\n" +
                   $"Код действует {message.ValidMinutes} минут и подходит только для того браузера, где вы запросили вход.\n\n" +
                   "Если вы не запрашивали вход, проигнорируйте это письмо."
        };
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
