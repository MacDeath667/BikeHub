using BikeHub.Dal;
using Microsoft.AspNetCore.Mvc;
using GetBike = BikeHub.Application.Queries.GetBike;
using GetBikes = BikeHub.Application.Queries.GetBikes;
using AddBike = BikeHub.Application.Commands.AddBike;

var builder = WebApplication.CreateBuilder(args);

builder.AddDapper("BikeHub");
builder.Services.AddScoped<GetBike.Handler>();
builder.Services.AddScoped<GetBikes.Handler>();
builder.Services.AddScoped<AddBike.Handler>();
builder.Services.AddSingleton<DatabaseMigrator>();
builder.Services.AddSingleton<DatabaseBootstrapper>();


var app = builder.Build();

await app.Services
    .GetRequiredService<DatabaseBootstrapper>()
    .EnsureDatabaseAsync(CancellationToken.None);

await app.Services
    .GetRequiredService<DatabaseMigrator>()
    .MigrateAsync(CancellationToken.None);


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


app.MapPost("/api/bikes", async (
    [FromServices] AddBike.Handler handler,
    [FromBody] AddBike.Command command,
    CancellationToken cancellationToken) =>
{
    command = command with { OwnerId = Guid.NewGuid() };
    await handler.Handle(command, cancellationToken);

    return Results.Ok();
});


app.Run();