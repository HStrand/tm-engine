using System.Collections.Immutable;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Pure functions for raising global parameters (temperature, oxygen, oceans).
/// Each raise includes TR increase, bonus effects, and cascading triggers.
/// </summary>
public static class GlobalParameters
{
    /// <summary>
    /// Raise temperature by 1 step (+2°C). Includes:
    /// - TR increase (+1)
    /// - Bonus at -24°C and -20°C: gain an ocean tile placement
    /// - Bonus at 0°C: gain 1 heat production
    /// </summary>
    public static GameState RaiseTemperature(GameState state, int playerId)
    {
        var map = MapDefinitions.GetMap(state.Map);
        if (state.Temperature >= map.MaxTemperature)
            return state;

        var newTemp = state.Temperature + Constants.TemperatureStep;
        state = state with { Temperature = Math.Min(newTemp, map.MaxTemperature) };

        // TR increase
        state = state.UpdatePlayer(playerId, p => p with
        {
            TerraformRating = p.TerraformRating + 1,
            IncreasedTRThisGeneration = true,
        });

        // Bonus: at -24°C and -20°C, gain an ocean placement
        if (newTemp == Constants.TemperatureOceanBonus1 || newTemp == Constants.TemperatureOceanBonus2)
        {
            if (state.OceansPlaced < map.MaxOceans)
            {
                var validOceanHexes = BoardLogic.GetValidOceanPlacements(state);
                if (validOceanHexes.Length > 0)
                {
                    state = state with
                    {
                        PendingAction = new PlaceTilePending(TileType.Ocean, validOceanHexes),
                    };
                }
            }
        }

        // Bonus: at 0°C, gain 1 heat production
        if (newTemp == Constants.TemperatureHeatProductionBonus)
        {
            state = state.UpdatePlayer(playerId, p => p with
            {
                Production = p.Production.Add(ResourceType.Heat, 1),
            });
        }

        return state;
    }

    /// <summary>
    /// Raise oxygen by 1 step (+1%). Includes:
    /// - TR increase (+1)
    /// - Bonus at 8%: also raise temperature
    /// </summary>
    public static GameState RaiseOxygen(GameState state, int playerId)
    {
        var map = MapDefinitions.GetMap(state.Map);
        if (state.Oxygen >= map.MaxOxygen)
            return state;

        state = state with { Oxygen = state.Oxygen + 1 };

        // TR increase
        state = state.UpdatePlayer(playerId, p => p with
        {
            TerraformRating = p.TerraformRating + 1,
            IncreasedTRThisGeneration = true,
        });

        // Bonus: at 8%, also raise temperature
        if (state.Oxygen == Constants.OxygenTemperatureBonus)
        {
            state = RaiseTemperature(state, playerId);
        }

        return state;
    }

    /// <summary>
    /// Place an ocean tile. Includes:
    /// - TR increase (+1) if oceans not maxed
    /// - Placement bonuses for the hex
    /// </summary>
    public static GameState PlaceOcean(GameState state, int playerId, HexCoord location)
    {
        var map = MapDefinitions.GetMap(state.Map);

        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(location, new PlacedTile(TileType.Ocean, null, location)),
            OceansPlaced = state.OceansPlaced + 1,
        };

        // TR increase (only if within limit)
        if (state.OceansPlaced <= map.MaxOceans)
        {
            state = state.UpdatePlayer(playerId, p => p with
            {
                TerraformRating = p.TerraformRating + 1,
            IncreasedTRThisGeneration = true,
            });
        }

        // Placement bonuses
        state = BoardLogic.ApplyPlacementBonuses(state, playerId, location);

        return state;
    }

    /// <summary>
    /// Place a greenery tile. Includes:
    /// - Ownership marker
    /// - Raise oxygen (+1%, which includes TR)
    /// - Placement bonuses
    /// </summary>
    public static GameState PlaceGreenery(GameState state, int playerId, HexCoord location)
    {
        state = PlaceTileOnBoard(state, TileType.Greenery, playerId, location);
        state = RaiseOxygen(state, playerId);
        return state;
    }

    /// <summary>
    /// Place a city tile on Mars. Includes:
    /// - Ownership marker
    /// - Placement bonuses
    /// - Triggers: PlaceCityTileOnMars + PlaceAnyCityTile
    /// </summary>
    public static GameState PlaceCity(GameState state, int playerId, HexCoord location)
    {
        state = PlaceTileOnBoard(state, TileType.City, playerId, location);
        state = TriggerSystem.FireTrigger(state, playerId, TriggerCondition.PlaceCityTileOnMars);
        state = TriggerSystem.FireTrigger(state, playerId, TriggerCondition.PlaceAnyCityTile);
        return state;
    }

    /// <summary>
    /// Place any tile type on the board with ownership and placement bonuses.
    /// Does NOT fire city-specific triggers — callers handle those.
    /// </summary>
    public static GameState PlaceTileOnBoard(GameState state, TileType tileType, int playerId, HexCoord location)
    {
        var ownerId = tileType == TileType.Ocean ? null : (int?)playerId;
        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(location, new PlacedTile(tileType, ownerId, location)),
        };

        return BoardLogic.ApplyPlacementBonuses(state, playerId, location);
    }
}
