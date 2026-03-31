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
    private static int CalculateCardPoints(GameState state, PlayerState player)
    {
        int total = 0;

        var vpContext = new VictoryPointContext(
            GetCardResources: cardId => player.CardResources.GetValueOrDefault(cardId, 0),
            CountTags: tag => player.CountTag(tag, CardRegistry.GetTags),
            CountAdjacentOceans: () => 0); // Default — Capital overrides per-tile

        // Played cards (blue + green)
        foreach (var cardId in player.PlayedCards)
        {
            total += GetCardVP(cardId, state, player, vpContext);
        }

        // Played events (red) — events can have VP too
        foreach (var cardId in player.PlayedEvents)
        {
            total += GetCardVP(cardId, state, player, vpContext);
        }

        return total;
    }

    private static int GetCardVP(string cardId, GameState state, PlayerState player, VictoryPointContext baseContext)
    {
        if (!CardRegistry.TryGet(cardId, out var entry))
            return 0;

        var vp = entry.Definition.VictoryPoints;
        if (vp == null)
            return 0;

        // For Capital city: count adjacent oceans for VP
        if (entry.Definition.Name == "Capital")
        {
            var capitalTile = state.PlacedTiles.Values.FirstOrDefault(t =>
                t.Type == TileType.Capital && t.OwnerId == player.PlayerId);

            if (capitalTile != null)
            {
                var capitalContext = baseContext with
                {
                    CountAdjacentOceans = () => BoardLogic.CountAdjacentOceans(state, capitalTile.Location),
                };
                return vp.Calculate(capitalContext);
            }
        }

        return vp.Calculate(baseContext);
    }
}
