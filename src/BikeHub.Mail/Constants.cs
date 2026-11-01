namespace BikeHub.Mail;

internal static class Constants
{
    public const string ApplicationName = "BikeHub.Mail";
    public const string ConnectionStringName = "Mail";
    public const string MigrateOnlyArgument = "--migrate-only";
    public const string ApiKeyHeader = "X-Api-Key";
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string Schema = "mail";
    public const string SmtpProvider = "Smtp";
    public const string GmailProvider = "GmailApi";
    public const string DevelopmentProvider = "Development";
    public const int SendTimeoutSeconds = 20;
    public const int StaleSendingSeconds = 120;

    public static class SmtpSecurity
    {
        public const string None = "None";
        public const string StartTls = "StartTls";
        public const string SslOnConnect = "SslOnConnect";
    }

    public static class EmailTransport
    {
        public const string AuthenticateStage = "authenticate";
        public const string OAuthStage = "oauth";
        public const string SendStage = "send";
        public const string AuthenticationRejected = "auth_rejected";
        public const string GmailRateLimited = "gmail_rate_limited";
        public const string Timeout = "timeout";
        public const string ConnectionError = "connection_error";
        public const string ProtocolError = "protocol_error";
        public const string SmtpRejected = "smtp_rejected";
    }
}
