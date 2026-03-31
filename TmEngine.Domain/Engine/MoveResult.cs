namespace TmEngine.Domain.Engine;

/// <summary>
/// Result of applying a move to a game state.
/// </summary>
public abstract record MoveResult
{
    public bool IsSuccess => this is Success;
    public bool IsError => this is Error;
}

/// <summary>Move was applied successfully.</summary>
public sealed record Success(string Message = "") : MoveResult;

/// <summary>Move was rejected due to validation failure.</summary>
public sealed record Error(string Message) : MoveResult;
