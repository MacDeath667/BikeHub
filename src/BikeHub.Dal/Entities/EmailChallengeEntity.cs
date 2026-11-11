namespace BikeHub.Dal.Entities;

public sealed class EmailChallengeEntity
{
    public string? Nickname { get; set; }
    public AuthenticationPurpose Purpose { get; set; }
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public string BindingHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public string? ProtectedMessage { get; set; }
    public bool EventPublished { get; set; }
    public DateTimeOffset? LastResendAt { get; set; }
    public bool Delivered { get; set; }
    public bool Consumed { get; set; }
}
