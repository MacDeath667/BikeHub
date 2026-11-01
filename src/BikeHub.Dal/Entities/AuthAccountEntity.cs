namespace BikeHub.Dal.Entities;

public sealed class AuthAccountEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset EmailConfirmedAt { get; set; }
    public string? PhoneE164 { get; set; }
    public DateTimeOffset? PhoneConfirmedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
