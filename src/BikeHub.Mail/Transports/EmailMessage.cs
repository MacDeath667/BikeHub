using BikeHub.Mail.Contracts;
using MimeKit;

namespace BikeHub.Mail.Transports;

public static class EmailMessage
{
    private const string PlainTextSubtype = "plain";

    public static MimeMessage Create(string fromAddress, string fromName, SendEmailRequest message)
    {
        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(fromName, fromAddress));
        mail.To.Add(MailboxAddress.Parse(message.Destination));
        // Stable for diagnostics; SMTP servers do not guarantee deduplication by Message-ID.
        mail.MessageId = $"{message.MessageId:N}@{fromAddress.Split('@')[1]}";
        mail.Subject = message.Subject;
        mail.Body = new TextPart(PlainTextSubtype) { Text = message.TextBody };
        return mail;
    }
}
