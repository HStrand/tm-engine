using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Engine;

/// <summary>
/// A player's final score breakdown.
/// </summary>
public sealed record PlayerScore(
    int PlayerId,
    int TerraformRating,
    int MilestonePoints,
    int AwardPoints,
    int GreeneryPoints,
    int CityPoints,
    int CardPoints,
    int TotalScore,
    int Tiebreaker); // MC for tiebreaking

/// <summary>
/// Computes final scores at game end.
/// </summary>
public static class Scoring
{
    /// <summary>
    /// Calculate final scores for all players. Returns scores sorted by rank (highest first).
    /// </summary>
    public static ImmutableList<PlayerScore> CalculateFinalScores(GameState state)
    {
        var scores = ImmutableList.CreateBuilder<PlayerScore>();

        foreach (var player in state.Players)
        {
            var tr = player.TerraformRating;
            var milestones = CalculateMilestonePoints(state, player.PlayerId);
            var awards = CalculateAwardPoints(state, player.PlayerId);
            var greeneries = CalculateGreeneryPoints(state, player.PlayerId);
            var cities = CalculateCityPoints(state, player.PlayerId);
            var cards = CalculateCardPoints(state, player);
            var total = tr + milestones + awards + greeneries + cities + cards;

            scores.Add(new PlayerScore(
                PlayerId: player.PlayerId,
                TerraformRating: tr,
                MilestonePoints: milestones,
                AwardPoints: awards,
                GreeneryPoints: greeneries,
                CityPoints: cities,
                CardPoints: cards,
                TotalScore: total,
                Tiebreaker: player.Resources.MegaCredits));
        }

        // Sort by total score descending, then MC for tiebreaker
        return scores.OrderByDescending(s => s.TotalScore)
            .ThenByDescending(s => s.Tiebreaker)
            .ToImmutableList();
    }

    // ── Milestones ─────────────────────────────────────────────

    private static int CalculateMilestonePoints(GameState state, int playerId)
    {
        return state.ClaimedMilestones.Count(m => m.PlayerId == playerId) * Constants.MilestoneVP;
    }

    // ── Awards ─────────────────────────────────────────────────

    private static int CalculateAwardPoints(GameState state, int playerId)
    {
        int total = 0;
        bool isTwoPlayer = state.Players.Count == 2;

        foreach (var funded in state.FundedAwards)
        {
            // Score each player for this award
            var playerScores = state.Players
                .Select(p => (PlayerId: p.PlayerId, Score: MilestoneAndAwardLogic.GetAwardScore(state, p, funded.AwardName)))
                .OrderByDescending(x => x.Score)
                .ToList();

            var topScore = playerScores[0].Score;
            var firstPlaceWinners = playerScores.Where(x => x.Score == topScore).ToList();

            if (firstPlaceWinners.Any(w => w.PlayerId == playerId))
            {
                total += Constants.AwardFirstPlaceVP;
            }
            else if (!isTwoPlayer)
            {
                // Second place: only if no tie for first (ties for first → no second place)
                if (firstPlaceWinners.Count == 1)
                {
                    var secondScore = playerScores.Where(x => x.Score < topScore).MaxBy(x => x.Score);
                    if (secondScore != default && secondScore.PlayerId == playerId)
                    {
                        total += Constants.AwardSecondPlaceVP;
                    }
                    else if (secondScore != default)
                    {
                        // Check for tie at second place — all tied players get 2nd place VP
                        var secondPlaceWinners = playerScores.Where(x => x.Score == secondScore.Score).ToList();
                        if (secondPlaceWinners.Any(w => w.PlayerId == playerId))
                        {
                            total += Constants.AwardSecondPlaceVP;
                        }
                    }
                }
            }
        }

        return total;
    }

    // ── Board VP ───────────────────────────────────────────────

    /// <summary>Each greenery tile is worth 1 VP to its owner.</summary>
    private static int CalculateGreeneryPoints(GameState state, int playerId)
    {
        return state.PlacedTiles.Values.Count(t =>
            t.OwnerId == playerId && t.Type == TileType.Greenery);
    }

    /// <summary>
    /// Each city tile is worth 1 VP per adjacent greenery tile (regardless of greenery owner).
    /// Capital city also scores 1 VP per adjacent ocean (handled by card VP, not here).
    /// </summary>
    private static int CalculateCityPoints(GameState state, int playerId)
    {
        int total = 0;

        foreach (var (coord, tile) in state.PlacedTiles)
        {
            if (tile.OwnerId != playerId) continue;
            if (tile.Type != TileType.City && tile.Type != TileType.Capital) continue;

            total += BoardLogic.CountAdjacentGreeneries(state, coord);
        }

        return total;
    }

    // ── Card VP ────────────────────────────────────────────────

    /// <summary>
    /// Sum VP from all played cards (including events) and card resources.
    /// </summary>
    private static int CalculateCardPoints(GameState state, PlayerState player) =>
        player.PlayedCards.Concat(player.PlayedEvents).Sum(cardId => CalculateCardPoints(state, player, cardId));

    /// <summary>
    /// VP a single played card is worth to its owner in the current state.
    /// </summary>
    public static int CalculateCardPoints(GameState state, PlayerState player, string cardId)
    {
        if (!CardRegistry.TryGet(cardId, out var entry) || entry.Definition.VictoryPoints is not { } vp)
            return 0;

        return vp.Calculate(new VictoryPointContext(
            GetCardResources: id => player.CardResources.GetValueOrDefault(id, 0),
            CountTags: tag => player.CountTag(tag, CardRegistry.GetTags),
            CountOnBoard: feature => CountOnBoard(state, player.PlayerId, feature)));
    }

    private static int CountOnBoard(GameState state, int playerId, BoardFeature feature)
    {
        PlacedTile? OwnTile(TileType type) =>
            state.PlacedTiles.Values.FirstOrDefault(t => t.Type == type && t.OwnerId == playerId);

        return feature switch
        {
            BoardFeature.OceansAdjacentToCapital =>
                OwnTile(TileType.Capital) is { } capital ? BoardLogic.CountAdjacentOceans(state, capital.Location) : 0,
            BoardFeature.CitiesAdjacentToCommercialDistrict =>
                OwnTile(TileType.CommercialDistrict) is { } district ? BoardLogic.CountAdjacentCities(state, district.Location) : 0,
            BoardFeature.CitiesInPlay => BoardLogic.CountCitiesInPlay(state),
            _ => 0,
        };
    }
}
