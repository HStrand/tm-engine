using System.Collections.Generic;
using System.Threading.Tasks;
using TmEngine.BgaImport;
using TmEngine.Domain.Models;

namespace tm_engine.Storage;

public interface IGameStore
{
    Task<GameState> LoadStateAsync(string gameId);
    Task SaveStateAsync(string gameId, GameState state);
    Task CreateGameAsync(GameState state);

    /// <summary>The state as it was after the given move number. Every state of a game is kept.</summary>
    Task<GameState> LoadStateAtAsync(string gameId, int moveNumber);

    /// <summary>Who sits where and where the game came from, for games imported from a BGA replay.</summary>
    Task SaveMetaAsync(string gameId, GameMeta meta);

    Task<GameMeta?> LoadMetaAsync(string gameId);

    /// <summary>Every state of the game from the first to the latest, in move order.</summary>
    Task<IReadOnlyList<GameState>> LoadAllStatesAsync(string gameId);
}
