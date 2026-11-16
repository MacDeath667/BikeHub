namespace BikeHub.Dal.Entities;

public sealed class ContentDraft
{
    public Guid OwnerId { get; set; }
    public string Scope { get; set; } = string.Empty;
    public long Revision { get; set; }
    public string? StateJson { get; set; }
    public Guid[] ImageIds { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}
