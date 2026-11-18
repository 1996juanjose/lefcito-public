using System.Text.Json;
using Lefcito.Application;
using Lefcito.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lefcito.Infrastructure;

public sealed class TournamentRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Document { get; set; } = "";
    public int Revision { get; set; }
    public DateTime CreatedAt { get; init; }
}

public sealed class TournamentDb(DbContextOptions<TournamentDb> options) : DbContext(options)
{
    public DbSet<TournamentRow> Tournaments => Set<TournamentRow>();
    public DbSet<PlayerRow> Players => Set<PlayerRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var player = builder.Entity<PlayerRow>();
        player.Property(x => x.Nickname).HasMaxLength(40);
        player.Property(x => x.Key).HasMaxLength(120);
        player.HasIndex(x => x.Key).IsUnique();
        var tournament = builder.Entity<TournamentRow>();
        tournament.Property(x => x.Document).HasColumnType("jsonb");
        tournament.Property(x => x.Revision).IsConcurrencyToken();
        tournament.Property(x => x.Name).HasMaxLength(80);
    }
}

public sealed class TournamentStore(TournamentDb db) : ITournaments
{
    public async Task<IReadOnlyList<TournamentSummary>> List(CancellationToken ct) =>
        await db.Tournaments.AsNoTracking().OrderByDescending(t => t.CreatedAt)
            .Select(t => new TournamentSummary(t.Id, t.Name)).ToListAsync(ct);

    public async Task<IReadOnlyList<Tournament>> ReadAll(CancellationToken ct)
    {
        var documents = await db.Tournaments.AsNoTracking().OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => t.Document).ToListAsync(ct);
        return documents.Select(document => JsonSerializer.Deserialize<Tournament>(document)!).ToArray();
    }

    public async Task<VersionedTournament?> Get(Guid id, CancellationToken ct)
    {
        var row = await db.Tournaments.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        return row is null ? null : new(JsonSerializer.Deserialize<Tournament>(row.Document)!, row.Revision);
    }

    public async Task Add(Tournament t, CancellationToken ct)
    {
        db.Tournaments.Add(new TournamentRow
        {
            Id = t.Id, Name = t.Name, Document = JsonSerializer.Serialize(t), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task Save(Tournament t, int revision, CancellationToken ct)
    {
        var row = new TournamentRow { Id = t.Id, Revision = revision };
        var entry = db.Attach(row);
        row.Document = JsonSerializer.Serialize(t);
        row.Revision++;
        entry.Property(x => x.Document).IsModified = true;
        entry.Property(x => x.Revision).IsModified = true;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RevisionConflict();
        }
    }
}
