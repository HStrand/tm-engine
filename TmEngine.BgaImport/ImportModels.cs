using System.Collections.Immutable;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport;

public enum IssueSeverity
{
    /// <summary>The imported position may differ from the real one in a way the player should know about.</summary>
    Warning,

    /// <summary>The position cannot be trusted; no state is produced.</summary>
    Blocking,
}

public sealed record ImportIssue(IssueSeverity Severity, string Code, string Message)
{
    public static ImportIssue Blocking(string code, string message) => new(IssueSeverity.Blocking, code, message);
    public static ImportIssue Warning(string code, string message) => new(IssueSeverity.Warning, code, message);
}

public enum ForkKind
{
    /// <summary>The cards have been dealt and nobody has chosen a corporation, preludes or opening cards yet.</summary>
    GameStart,

    /// <summary>Start of a generation's action phase: research is done and nobody has acted yet.</summary>
    GenerationStart,

    /// <summary>Inside the action phase, with a player about to begin an action.</summary>
    ActionStart,
}

/// <summary>A position in a BGA log that can be turned into an engine state: the state after move <see cref="MoveIndex"/>.</summary>
public sealed record ForkPoint(int MoveIndex, int? MoveNumber, ForkKind Kind, int Generation, string ActiveBgaPlayerId);

public sealed record BgaImportOptions(int MoveIndex, int ShuffleSeed = 0, string? GameId = null);

/// <summary>Who sits in each engine seat. The engine knows players only as 0..n-1.</summary>
public sealed record SeatMeta(int Seat, string BgaPlayerId, string Name, string? Color);

public sealed record SourceRef(string? TableId, string? Perspective, int MoveIndex, int? MoveNumber);

/// <summary>Everything about an imported game that is not engine state.</summary>
public sealed record GameMeta(ImmutableList<SeatMeta> Seats, SourceRef Source, int ShuffleSeed);

public sealed record BgaImportResult(GameState? State, GameMeta? Meta, ForkPoint? Fork, ImmutableList<ImportIssue> Issues)
{
    public bool Success => State is not null;

    public IEnumerable<ImportIssue> BlockingIssues => Issues.Where(i => i.Severity == IssueSeverity.Blocking);
}

/// <summary>Whether a log can be played on from at all, and where.</summary>
public sealed record ForkPointsResult(ImmutableList<ForkPoint> ForkPoints, ImmutableList<ImportIssue> Issues)
{
    public bool Eligible => Issues.All(i => i.Severity != IssueSeverity.Blocking);
}
