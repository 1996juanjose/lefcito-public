using Lefcito.Domain;
using MediatR;

namespace Lefcito.Application;

public record Player(Guid Id, string Nickname);
public interface IPlayers
{
    Task<IReadOnlyList<Player>> List(CancellationToken ct);
    Task<bool> Add(Player player, CancellationToken ct);
}
public record ListPlayers : IRequest<IReadOnlyList<Player>>;
public record CreatePlayer(string Nickname) : IRequest<Player>;
public record GetRanking : IRequest<IReadOnlyList<RankingEntry>>;
public record RankingEntry(Guid Id, string Nickname, int Wins, int Losses, decimal ResultPoints, decimal EventPoints)
{
    public decimal Total => ResultPoints + EventPoints;
}

public sealed class PlayerHandlers(IPlayers players, ITournaments tournaments) :
    IRequestHandler<ListPlayers, IReadOnlyList<Player>>, IRequestHandler<CreatePlayer, Player>,
    IRequestHandler<GetRanking, IReadOnlyList<RankingEntry>>
{
    public Task<IReadOnlyList<Player>> Handle(ListPlayers request, CancellationToken ct) => players.List(ct);

    public async Task<Player> Handle(CreatePlayer request, CancellationToken ct)
    {
        var nickname = request.Nickname?.Trim() ?? "";
        if (nickname.Length is < 1 or > 40 || nickname.Any(char.IsControl))
            throw new TournamentException("Escribe un nickname de 1 a 40 caracteres.");
        var player = new Player(Guid.NewGuid(), nickname);
        if (!await players.Add(player, ct))
            throw new TournamentException("Ya existe un jugador con ese nickname.");
        return player;
    }

    public async Task<IReadOnlyList<RankingEntry>> Handle(GetRanking request, CancellationToken ct)
    {
        var registered = await players.List(ct);
        var documents = await tournaments.ReadAll(ct);
        var points = documents.SelectMany(TournamentQueries.CalculateStandings)
            .ToLookup(p => p.Name, StringComparer.OrdinalIgnoreCase);
        return registered.Select(player =>
        {
            var scores = points[player.Nickname];
            return new RankingEntry(player.Id, player.Nickname, scores.Sum(s => s.Wins),
                scores.Sum(s => s.Losses), scores.Sum(s => s.ResultPoints), scores.Sum(s => s.EventPoints));
        }).OrderByDescending(p => p.Total).ThenBy(p => p.Nickname).ToArray();
    }
}
