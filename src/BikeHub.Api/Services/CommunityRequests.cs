using System.ComponentModel.DataAnnotations;
using BikeHub.Dal.Entities;

namespace BikeHub.Api.Services;

public sealed record UpdateProfileRequest(
    [Required, StringLength(60, MinimumLength = 2)] string Name,
    [Required, RegularExpression("^[a-z0-9_]{3,32}$")] string Handle,
    [StringLength(100)] string City,
    [StringLength(2000)] string Bio,
    Guid? AvatarId = null);
public sealed record ReportRequest([EnumDataType(typeof(ReportTarget))] ReportTarget TargetType,
    Guid TargetId, [Required, StringLength(2000, MinimumLength = 5)] string Reason);
public sealed record ResolveReportRequest([Required, StringLength(2000, MinimumLength = 2)] string Resolution);
public sealed record ReportDto(Guid Id, ReportTarget TargetType, Guid TargetId, string Reason,
    DateTimeOffset CreatedAt, bool Resolved, string? Resolution, Guid ReporterId);
public sealed record VisibilityRequest(bool Hidden);
public sealed record BlockUserRequest(bool Blocked);
public sealed record MediaDto(Guid Id, string ImageUrl, int Width, int Height);

public sealed class CommunityProblem(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
