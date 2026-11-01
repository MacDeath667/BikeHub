using MimeKit;
using MimeKit.Utils;

namespace BikeHub.Api.Authentication;

public static class VerificationEmail
{
    private const string PlainTextSubtype = "plain";
    private const string BodyTemplate = "Код для входа в BikeHub: {0}\n\n" +
        "Код действует {1} минут и подходит только для того браузера, где вы запросили вход.\n\n" +
        "Если вы не запрашивали вход, проигнорируйте это письмо.";

    public static MimeMessage Create(string fromAddress, string fromName, VerificationMessage message)
    {
        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(fromName, fromAddress));
        mail.To.Add(MailboxAddress.Parse(message.Destination));
        mail.MessageId = MimeUtils.GenerateMessageId(fromAddress.Split('@')[1]);
        mail.Subject = Constants.VerificationEmailSubject;
        mail.Body = new TextPart(PlainTextSubtype)
        {
            Text = string.Format(BodyTemplate, message.Code, message.ValidMinutes)
        };
        return mail;
    }
}
