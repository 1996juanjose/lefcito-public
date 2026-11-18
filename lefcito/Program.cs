using Lefcito.Application;
using Lefcito.Domain;
using Lefcito.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionFile = builder.Configuration.GetConnectionString("PostgresFile");
var connection = string.IsNullOrWhiteSpace(connectionFile)
    ? builder.Configuration.GetConnectionString("Postgres")
    : File.ReadAllText(connectionFile).Trim();
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("Configura ConnectionStrings__Postgres o ConnectionStrings__PostgresFile.");
builder.Services.AddDbContext<TournamentDb>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<ITournaments, TournamentStore>();
builder.Services.AddScoped<IPlayers, PlayerStore>();
builder.Services.AddMediatR(config => config.RegisterServicesFromAssemblyContaining<TournamentCommands>());
var app = builder.Build();

app.Use(async (context, next) =>
{
    try { await next(); }
    catch (TournamentException error)
    {
        await Results.Problem(error.Message, statusCode: 400).ExecuteAsync(context);
    }
    catch (RevisionConflict)
    {
        await Results.Problem("El torneo cambió. Recarga el marcador antes de guardar.", statusCode: 409).ExecuteAsync(context);
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/jugadores", (ISender sender, CancellationToken ct) => sender.Send(new ListPlayers(), ct));
app.MapPost("/api/jugadores", async (CreatePlayer command, ISender sender, CancellationToken ct) =>
{
    var player = await sender.Send(command, ct);
    return Results.Json(player, statusCode: 201);
});
app.MapGet("/api/grupos", (ISender sender, CancellationToken ct) => sender.Send(new ListGroups(), ct));
app.MapGet("/api/ranking", (ISender sender, CancellationToken ct) => sender.Send(new GetRanking(), ct));
var api = app.MapGroup("/api/torneos");
api.MapGet("", (ISender sender, CancellationToken ct) => sender.Send(new ListTournaments(), ct));
api.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
    await sender.Send(new GetTournament(id), ct) is { } view ? Results.Ok(view) : Results.NotFound());
api.MapPost("", async (CreateTournament command, ISender sender, CancellationToken ct) =>
{
    var id = await sender.Send(command, ct);
    return Results.Created($"/api/torneos/{id}", new { id });
});
api.MapPut("/{id:guid}/reglas", async (Guid id, SaveScoring data, ISender sender, CancellationToken ct) =>
    await sender.Send(new UpdateScoring(id, data.Revision, data.Scoring), ct) ? Results.NoContent() : Results.NotFound());
api.MapPost("/{id:guid}/cruces", async (Guid id, AddMatchData data, ISender sender, CancellationToken ct) =>
    await sender.Send(new AddEncounter(id, data.Revision), ct) ? Results.NoContent() : Results.NotFound());
api.MapPut("/{id:guid}/cruces/{match}", async (Guid id, string match, SaveScore data, ISender sender, CancellationToken ct) =>
    data.Score is null ? Results.BadRequest() :
    await sender.Send(new RegisterScore(id, match, data.Revision, data.Score), ct) ? Results.NoContent() : Results.NotFound());

if (args.Contains("--check-db"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TournamentDb>();
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await db.Database.OpenConnectionAsync(timeout.Token);
        Console.WriteLine("Conexión PostgreSQL verificada.");
    }
    catch (Npgsql.PostgresException error)
    {
        Console.Error.WriteLine($"PostgreSQL rechazó la conexión (SQLSTATE {error.SqlState}).");
        Environment.ExitCode = 1;
    }
    catch (Exception error) when (error is Npgsql.NpgsqlException or System.Security.Authentication.AuthenticationException or OperationCanceledException)
    {
        Console.Error.WriteLine("No se pudo conectar. Comprueba el host, el puerto y el certificado TLS.");
        Environment.ExitCode = 1;
    }
    return;
}

if (args.Contains("--init-db"))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    await PlayerStore.Initialize(services.GetRequiredService<TournamentDb>(), services.GetRequiredService<IPlayers>(),
        services.GetRequiredService<ITournaments>());
    return;
}
await app.RunAsync();
record AddMatchData(int Revision);
record SaveScore(int Revision, Score Score);
record SaveScoring(int Revision, Scoring Scoring);
