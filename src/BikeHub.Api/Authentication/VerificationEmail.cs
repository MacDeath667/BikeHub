using MimeKit;
using MimeKit.Utils;

namespace BikeHub.Api.Authentication;

public static class VerificationEmail
{
    public static MimeMessage Create(string fromAddress, string fromName, VerificationMessage message)
    {
        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(fromName, fromAddress));
        mail.To.Add(MailboxAddress.Parse(message.Destination));
        mail.MessageId = MimeUtils.GenerateMessageId(fromAddress.Split('@')[1]);
        mail.Subject = "Код для входа в BikeHub";
        mail.Body = new TextPart("plain")
        {
            Text = $"Код для входа в BikeHub: {message.Code}\n\n" +
                   $"Код действует {message.ValidMinutes} минут и подходит только для того браузера, где вы запросили вход.\n\n" +
                   "Если вы не запрашивали вход, проигнорируйте это письмо."
        };
        return mail;
    }
}
