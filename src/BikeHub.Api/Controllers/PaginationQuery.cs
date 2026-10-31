using System.ComponentModel.DataAnnotations;

namespace BikeHub.Api.Controllers;

public sealed class PaginationQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 50)] public int PageSize { get; init; } = 10;
}
