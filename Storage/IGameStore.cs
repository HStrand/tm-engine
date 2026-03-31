using System.Threading.Tasks;
using TmEngine.Domain.Models;

namespace tm_engine.Storage;

public interface IGameStore
{
    Task<GameState> LoadStateAsync(string gameId);
    Task SaveStateAsync(string gameId, GameState state);
    Task CreateGameAsync(GameState state);
}
