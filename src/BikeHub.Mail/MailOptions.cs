using Confluent.Kafka;

namespace BikeHub.Mail;

public sealed class MailOptions
{
    public string Provider { get; set; } = Constants.SmtpProvider;
    public string ApiKey { get; set; } = string.Empty;
    public int MaxAttempts { get; set; } = 5;
    public int RetrySeconds { get; set; } = 30;
}

public sealed class KafkaOptions
{
    public bool Enabled { get; set; }
    public string BootstrapServers { get; set; } = string.Empty;
    public string Topic { get; set; } = "bikehub.mail.send.v1";
    public string GroupId { get; set; } = "bikehub-mail-v1";
    public SecurityProtocol SecurityProtocol { get; set; } = SecurityProtocol.Plaintext;
    public SaslMechanism SaslMechanism { get; set; } = SaslMechanism.Plain;
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }
    public string? SslCaLocation { get; set; }
}

public sealed class GmailApiOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "BikeHub";
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string Security { get; set; } = Constants.SmtpSecurity.StartTls;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "BikeHub";
}
