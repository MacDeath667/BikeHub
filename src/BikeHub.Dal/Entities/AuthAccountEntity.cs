namespace BikeHub.Dal.Entities;

public sealed class AuthAccountEntity
{
    public string? Handle { get; set; }
    public string City { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public Guid? AvatarId { get; set; }
    public bool IsBlocked { get; set; }
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset EmailConfirmedAt { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTimeOffset? PhoneConfirmedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
