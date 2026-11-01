namespace BikeHub.Dal.Entities;

public sealed class AuthSessionEntity
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public AuthAccountEntity Account { get; set; } = null!;
}
