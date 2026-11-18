using Lefcito.Domain;
using MediatR;

namespace Lefcito.Application;

public interface ITournaments
{
    Task<IReadOnlyList<TournamentSummary>> List(CancellationToken ct);
    Task<IReadOnlyList<Tournament>> ReadAll(CancellationToken ct);
    Task<VersionedTournament?> Get(Guid id, CancellationToken ct);
    Task Add(Tournament tournament, CancellationToken ct);
    Task Save(Tournament tournament, int revision, CancellationToken ct);
}

public record TournamentSummary(Guid Id, string Name);
public record VersionedTournament(Tournament Tournament, int Revision);
public record ListTournaments : IRequest<IReadOnlyList<TournamentSummary>>;
public record GetTournament(Guid Id) : IRequest<TournamentView?>;
public record CreateTournament(string Name, Team[] Teams, int SemiBestOf, int FinalBestOf, Scoring Scoring, string? Mode = null) : IRequest<Guid>;
public record UpdateScoring(Guid Id, int Revision, Scoring Scoring) : IRequest<bool>;
public record AddEncounter(Guid Id, int Revision) : IRequest<bool>;
public record RegisterScore(Guid Id, string Match, int Revision, Score Score) : IRequest<bool>;
public record PlayerStanding(string Name, string Team, int Wins, int Losses, decimal ResultPoints, decimal EventPoints)
{
    public decimal Total => ResultPoints + EventPoints;
}
public record TournamentView(Guid Id, string Name, Team[] Teams, Scoring Scoring, int Revision,
    IReadOnlyList<Match> Matches, IReadOnlyList<PlayerStanding> Standings, IReadOnlyList<GroupStanding> GroupStandings, string? Mode);

public sealed class TournamentQueries(ITournaments store) :
    IRequestHandler<ListTournaments, IReadOnlyList<TournamentSummary>>,
    IRequestHandler<GetTournament, TournamentView?>
{
    public Task<IReadOnlyList<TournamentSummary>> Handle(ListTournaments request, CancellationToken ct) => store.List(ct);

    public async Task<TournamentView?> Handle(GetTournament request, CancellationToken ct)
    {
        var stored = await store.Get(request.Id, ct);
        if (stored is null) return null;
        var (t, revision) = stored;
        return new(t.Id, t.Name, t.Teams, t.Scoring, revision, t.Bracket(), CalculateStandings(t),
            GroupQueries.Calculate(t).OrderByDescending(g => g.Points).ThenBy(g => g.Name).ToArray(), t.Mode);
    }

    internal static IReadOnlyList<PlayerStanding> CalculateStandings(Tournament t)
    {
        var matches = t.Bracket();
        var decided = matches.Where(m => m.ScoringLeader is not null).ToArray();
        var scoredEvents = matches.Where(m => m.Score is not null && (m.Format == 0 || m.Winner is not null))
            .SelectMany(m => m.Score!.Events).ToLookup(e => e.Player);
        var points = t.Scoring.Events.ToDictionary(r => r.Name, r => r.Points);
        return t.Teams.SelectMany(team => team.Players.Select(player =>
        {
            var wins = decided.Count(m => m.ScoringLeader == team);
            var losses = decided.Count(m => (m.A == team || m.B == team) && m.ScoringLeader != team);
            var events = scoredEvents[player];
            return new PlayerStanding(player, team.Name, wins, losses, wins * t.Scoring.Win + losses * t.Scoring.Loss,
                events.Sum(e => e.Count * points[e.Rule]));
        })).OrderByDescending(p => p.Total).ThenBy(p => p.Name).ToArray();
    }
}

public sealed class TournamentCommands(ITournaments store, IPlayers players) :
    IRequestHandler<CreateTournament, Guid>, IRequestHandler<RegisterScore, bool>, IRequestHandler<UpdateScoring, bool>, IRequestHandler<AddEncounter, bool>
{
    public async Task<Guid> Handle(CreateTournament request, CancellationToken ct)
    {
        var teams = request.Teams?.Select(t => t is null ? null! :
            new Team(t.Name?.Trim() ?? "", t.Players?.Select(p => p?.Trim() ?? "").ToArray()!)).ToArray();
        var t = new Tournament(Guid.NewGuid(), request.Name?.Trim() ?? "", teams!, request.SemiBestOf,
            request.FinalBestOf, request.Scoring, [], Mode: request.Mode);
        t.ValidateSetup();
        var catalog = (await players.List(ct)).ToDictionary(p => p.Nickname, StringComparer.OrdinalIgnoreCase);
        if (t.Teams.SelectMany(team => team.Players).Any(name => !catalog.ContainsKey(name)))
            throw new TournamentException("Agrega al registro los jugadores nuevos antes de formar el equipo.");
        var assigned = t.Teams.Select(team =>
            team with { Players = team.Players.Select(name => catalog[name].Nickname).ToArray() }).ToArray();
        await store.Add(t with { Teams = assigned }, ct);
        return t.Id;
    }

    public async Task<bool> Handle(UpdateScoring request, CancellationToken ct)
    {
        var stored = await store.Get(request.Id, ct);
        if (stored is null) return false;
        if (stored.Revision != request.Revision) throw new RevisionConflict();
        var updated = stored.Tournament with { Scoring = request.Scoring };
        updated.ValidateSetup();
        var names = updated.Scoring.Events.Select(rule => rule.Name).ToHashSet();
        if (updated.Scores.Values.SelectMany(score => score.Events).Any(entry => !names.Contains(entry.Rule)))
            throw new TournamentException("Conserva el nombre de cada evento que ya se registró.");
        await store.Save(updated, stored.Revision, ct);
        return true;
    }

    public async Task<bool> Handle(AddEncounter request, CancellationToken ct)
    {
        var stored = await store.Get(request.Id, ct);
        if (stored is null) return false;
        if (stored.Revision != request.Revision) throw new RevisionConflict();
        await store.Save(stored.Tournament.AddMatch(), stored.Revision, ct);
        return true;
    }

    public async Task<bool> Handle(RegisterScore request, CancellationToken ct)
    {
        var stored = await store.Get(request.Id, ct);
        if (stored is null) return false;
        if (stored.Revision != request.Revision) throw new RevisionConflict();
        stored.Tournament.Register(request.Match, request.Score);
        await store.Save(stored.Tournament, stored.Revision, ct);
        return true;
    }
}

public sealed class RevisionConflict : Exception;
