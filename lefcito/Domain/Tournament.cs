namespace Lefcito.Domain;

public record Team(string Name, string[] Players);
public record Rule(string Name, decimal Points);
public record Scoring(decimal Win, decimal Loss, Rule[] Events);
public record ActionEntry(string Player, string Rule, int Count);
public record Score(int A, int B, ActionEntry[] Events, int? Format = null, bool Closed = false);
public record Match(string Id, string Stage, Team? A, Team? B, int BestOf, Score? Score)
{
    public int Format => Score?.Format ?? BestOf;
    public Team? ScoringLeader => Format == 0 && Score is not null
        ? Score.A == Score.B ? null : Score.A > Score.B ? A : B
        : Winner;
    public bool IsLocked => Format == 0 && Score?.Closed == true;
    public int? Target => Format == 0 ? null : Format / 2 + 1;
    public Team? Winner => Score is null ? null : Format == 0
        ? !Score.Closed || Score.A == Score.B ? null : Score.A > Score.B ? A : B
        : Score.A == Target ? A : Score.B == Target ? B : null;
}

public record Tournament(Guid Id, string Name, Team[] Teams, int SemiBestOf, int FinalBestOf,
    Scoring Scoring, Dictionary<string, Score> Scores, int ExtraMatches = 0, string? Mode = null)
{
    public IReadOnlyList<Match> Bracket()
    {
        var matches = new List<Match>();
        Team? first = Teams[0];
        Team? second = Teams[^1];
        if (Teams.Length >= 3)
        {
            var semi = Build("semi-1", "Semifinal 1", Teams[0], Teams[1], SemiBestOf);
            matches.Add(semi);
            first = semi.Winner;
        }
        if (Teams.Length == 4)
        {
            var semi = Build("semi-2", "Semifinal 2", Teams[2], Teams[3], SemiBestOf);
            matches.Add(semi);
            second = semi.Winner;
        }
        var title = Mode == "partida" ? "Partida" : Mode is null && Teams.Length == 2 ? "Enfrentamiento 1" : "Final";
        matches.Add(Build("final", title, first, second, Mode == "partida" ? 0 : FinalBestOf));
        for (var i = 1; i <= ExtraMatches; i++)
            matches.Add(Build($"extra-{i}", $"Enfrentamiento {i + 1}", Teams[0], Teams[1], 0));
        return matches;
    }

    private Match Build(string id, string stage, Team? a, Team? b, int bestOf) =>
        new(id, stage, a, b, bestOf, Scores.GetValueOrDefault(id));

    public void ValidateSetup()
    {
        Require(!string.IsNullOrWhiteSpace(Name) && Name.Length <= 80, "Escribe el nombre del torneo (hasta 80 caracteres).");
        Require(Teams is { Length: >= 2 and <= 4 }, "Forma entre 2 y 4 equipos.");
        Require(Mode is null or "partida" or "torneo", "Selecciona Partida o Torneo.");
        Require(Mode != "partida" || Teams.Length == 2, "Una partida requiere dos equipos.");
        Require(Teams.All(t => t is not null && !string.IsNullOrWhiteSpace(t.Name) && t.Name.Length <= 40 &&
            t.Players is { Length: >= 1 and <= 12 }), "Cada equipo necesita un nombre y entre 1 y 12 jugadores.");
        Require(Unique(Teams.Select(t => t.Name)), "Los nombres de equipo deben ser únicos.");
        var players = Teams.SelectMany(t => t.Players).ToArray();
        Require(players.All(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 40) && Unique(players),
            "Usa nombres únicos de hasta 40 caracteres. Cada jugador pertenece a un equipo.");
        Require(ValidFormat(SemiBestOf) && ValidFormat(FinalBestOf), "El formato debe ser BO1, BO3, BO5, BO7 o BO9.");
        Require(Scoring is not null && Scoring.Win >= 0 && Scoring.Loss <= 0 &&
            Math.Abs(Scoring.Win) <= 10000 && Math.Abs(Scoring.Loss) <= 10000, "Revisa los puntos de victoria y derrota.");
        Require(Scoring!.Events is { Length: <= 30 } &&
            Scoring.Events.All(r => r is not null && !string.IsNullOrWhiteSpace(r.Name) && r.Name.Length <= 60 &&
                r.Points >= -10000 && r.Points <= 10000) && Unique(Scoring.Events.Select(r => r.Name)),
            "Define hasta 30 eventos con nombres distintos y puntos entre −10000 y 10000.");
    }

    public Tournament AddMatch()
    {
        Require(Mode is null && Teams.Length == 2, "Este tipo de partida admite un solo enfrentamiento.");
        Require(ExtraMatches < 99, "Esta partida ya tiene 100 enfrentamientos.");
        return this with { ExtraMatches = ExtraMatches + 1 };
    }

    public void Register(string id, Score score)
    {
        var match = Bracket().SingleOrDefault(m => m.Id == id);
        Require(match?.A is not null && match.B is not null, "El cruce todavía no tiene dos equipos.");
        Require(!match!.IsLocked, "El enfrentamiento está cerrado y su marcador ya no se puede modificar.");
        var updated = match with { Score = score with { Format = score.Format ?? match!.Format } };
        Require(Mode != "partida" || updated.Format == 0, "Una partida usa marcador libre.");
        Require(updated.Format == 0 || ValidFormat(updated.Format), "Selecciona un formato válido.");
        Require(score.A is >= 0 and <= 1000000 && score.B is >= 0 and <= 1000000,
            "Usa marcadores entre 0 y 1 000 000.");
        if (updated.Target is int target)
            Require(score.A <= target && score.B <= target && (score.A != target || score.B != target),
                "Marcador inválido para el formato de la serie.");
        else
            Require(!score.Closed || score.A != score.B, "Para cerrar el enfrentamiento, un equipo debe quedar arriba.");
        var players = match!.A!.Players.Concat(match.B!.Players).ToHashSet();
        var rules = Scoring.Events.Select(r => r.Name).ToHashSet();
        Require(score.Events is { Length: <= 250 } && score.Events.All(e => e is not null &&
            e.Count is >= 1 and <= 1000 && players.Contains(e.Player) && rules.Contains(e.Rule)),
            "Revisa el jugador, el tipo de evento y la cantidad.");
        var previousWinner = match.Winner;
        var newWinner = updated.Winner;
        Require(!id.StartsWith("semi-", StringComparison.Ordinal) || !Scores.ContainsKey("final") || previousWinner == newWinner,
            "El ganador de esta semifinal ya figura en una final registrada.");
        Scores[id] = updated.Score!;
    }

    private static bool ValidFormat(int value) => value is 1 or 3 or 5 or 7 or 9;

    private static bool Unique(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return values.All(seen.Add);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new TournamentException(message);
    }
}

public sealed class TournamentException(string message) : Exception(message);
