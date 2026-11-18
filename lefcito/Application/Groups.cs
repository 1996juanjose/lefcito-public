using Lefcito.Domain;
using MediatR;

namespace Lefcito.Application;

public record GroupStanding(string Name, int Wins, int Losses, decimal Points);
public record SavedGroup(string Name, string[] Players, int Wins, int Losses, decimal Points);
public record ListGroups : IRequest<IReadOnlyList<SavedGroup>>;

public sealed class GroupQueries(ITournaments tournaments) : IRequestHandler<ListGroups, IReadOnlyList<SavedGroup>>
{
    public async Task<IReadOnlyList<SavedGroup>> Handle(ListGroups request, CancellationToken ct)
    {
        var history = await tournaments.ReadAll(ct);
        var standings = history.SelectMany(Calculate).ToLookup(row => row.Name, StringComparer.OrdinalIgnoreCase);
        return history.SelectMany(t => t.Teams).DistinctBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .Select(team =>
            {
                var rows = standings[team.Name];
                return new SavedGroup(team.Name, team.Players, rows.Sum(r => r.Wins),
                    rows.Sum(r => r.Losses), rows.Sum(r => r.Points));
            })
            .OrderByDescending(g => g.Points).ThenBy(g => g.Name).ToArray();
    }

    internal static IEnumerable<GroupStanding> Calculate(Tournament tournament)
    {
        var played = tournament.Bracket().Where(match => match.ScoringLeader is not null).ToArray();
        foreach (var team in tournament.Teams)
        {
            var wins = played.Count(match => match.ScoringLeader == team);
            var losses = played.Count(match => (match.A == team || match.B == team) && match.ScoringLeader != team);
            yield return new GroupStanding(team.Name, wins, losses,
                wins * tournament.Scoring.Win + losses * tournament.Scoring.Loss);
        }
    }
}
