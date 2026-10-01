using BikeHub.Dal;
using Microsoft.AspNetCore.Mvc;
using GetBike = BikeHub.Application.Queries.GetBike;
using GetBikes = BikeHub.Application.Queries.GetBikes;

var builder = WebApplication.CreateBuilder(args);

builder.AddDapper("BikeHub");
builder.Services.AddScoped<GetBike.Handler>();
builder.Services.AddScoped<GetBikes.Handler>();


var app = builder.Build();

app.MapGet("/api/bikes", () => Results.Ok());
app.MapGet("/api/bikes/{id}/history", (Guid id) => Results.Ok(id));


app.MapGet("/api/bikes/{id:guid}", async (
    Guid id,
    [FromServices] GetBike.Handler handler,
    CancellationToken cancellationToken) =>
{
    var query = new GetBike.Query(id);

    var result = await handler.Handle(query, cancellationToken);

    return Results.Ok(result);
});


app.MapGet("/api/user/{userId:guid}/bikes", async (
    Guid userId,
    [FromServices] GetBikes.Handler handler,
    CancellationToken cancellationToken) =>
{
    var query = new GetBikes.Query(userId);
    var result = await handler.Handle(query, cancellationToken);

    return Results.Ok(result);
});


app.Run();