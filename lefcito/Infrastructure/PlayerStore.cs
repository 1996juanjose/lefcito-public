using Lefcito.Application;
using Microsoft.EntityFrameworkCore;

namespace Lefcito.Infrastructure;

public sealed class PlayerRow
{
    public Guid Id { get; init; }
    public string Nickname { get; init; } = "";
    public string Key { get; init; } = "";
}

public sealed class PlayerStore(TournamentDb db) : IPlayers
{
    public async Task<IReadOnlyList<Player>> List(CancellationToken ct) =>
        await db.Players.AsNoTracking().OrderBy(p => p.Nickname)
            .Select(p => new Player(p.Id, p.Nickname)).ToListAsync(ct);

    public async Task<bool> Add(Player player, CancellationToken ct)
    {
        var key = player.Nickname.ToUpperInvariant();
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Players" ("Id", "Nickname", "Key")
            VALUES ({player.Id}, {player.Nickname}, {key})
            ON CONFLICT ("Key") DO NOTHING
            """, ct);
        return rows == 1;
    }

    public static async Task Initialize(TournamentDb db, IPlayers players, ITournaments tournaments)
    {
        await db.Database.EnsureCreatedAsync();
        // Existing Lefcito databases predate the player catalog.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Players" (
                "Id" uuid PRIMARY KEY,
                "Nickname" varchar(40) NOT NULL,
                "Key" varchar(120) NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Players_Key" ON "Players" ("Key");
            """);
        var history = await tournaments.ReadAll(default);
        var nicknames = history.SelectMany(t => t.Teams).SelectMany(team => team.Players)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var nickname in nicknames)
            await players.Add(new Player(Guid.NewGuid(), nickname), default);
    }
}
