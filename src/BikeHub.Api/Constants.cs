namespace BikeHub.Api;

internal static class Constants
{
    public const string ApplicationName = "BikeHub";
    public const string CookiePath = "/";
    public const string AuthenticationScheme = ApplicationName;
    public const string AuthenticationName = "BikeHub.Authentication";
    public const string BrowserBindingPurpose = "browser";
    public const string EmailChannel = "email";
    public const string VerificationEmailSubject = "Код для входа в BikeHub";
    public const string AuthenticationUnavailableTitle = "Вход пока недоступен.";
    public const string MigrateOnlyArgument = "--migrate-only";
    public const string IndexDocument = "index.html";
    public const int MaximumEmailLength = 254;
    public const int MinimumDisplayNameLength = 2;
    public const int BrowserBindingByteLength = 32;
    public const int BrowserBindingLength = BrowserBindingByteLength * 2;

    public static class AuthenticationStores
    {
        public const string Memory = "Memory";
        public const string Postgres = "Postgres";
    }

    public static class EmailProviders
    {
        public const string Development = "Development";
        public const string Smtp = "Smtp";
        public const string GmailApi = "GmailApi";
    }

    public static class SmtpSecurity
    {
        public const string StartTls = "StartTls";
        public const string SslOnConnect = "SslOnConnect";
    }

    public static class Claims
    {
        public const string SessionId = "session_id";
        public const string EmailVerified = "email_verified";
        public const string VerifiedValue = "true";
    }

    public static class RateLimitPolicies
    {
        public const string RequestCode = "auth-request";
        public const string VerifyCode = "auth-verify";
    }

    public static class EmailTransport
    {
        public const string AuthenticateStage = "authenticate";
        public const string OAuthStage = "oauth";
        public const string SendStage = "send";
        public const string AuthenticationRejected = "auth_rejected";
        public const string GmailRateLimited = "gmail_rate_limited";
    }

    public static class FeedTabs
    {
        public const string Following = "following";
        public const string Saved = "saved";
    }

    public static class Images
    {
        public const string Road = "/images/road.jpg";
        public const string Trail = "/images/trail.jpg";
        public const string Gravel = "/images/gravel.jpg";
        public const string City = "/images/city.jpg";
        public const string Adventure = "/images/adventure.jpg";
    }

    public static class MockData
    {
        public static readonly Guid FeaturedBikeId = new("20000000-0000-0000-0000-000000000001");
        public const string DefaultUserColor = "sage";
    }
}
