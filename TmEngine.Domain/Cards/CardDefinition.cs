using System.Collections.Immutable;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Cards;

/// <summary>
/// Static definition of a card — its metadata, requirements, and victory point formula.
/// Does not include effects (those are registered separately in the card effect system).
/// </summary>
public sealed record CardDefinition
{
    /// <summary>Unique identifier matching cards.json (e.g., "001", "CORP01", "P01").</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
    public required CardType Type { get; init; }

    /// <summary>Cost in MC to play from hand. 0 for corporations and preludes.</summary>
    public required int Cost { get; init; }

    public required ImmutableArray<Tag> Tags { get; init; }
    public required Expansion Expansion { get; init; }

    /// <summary>Requirement that must be met to play this card. Null if no requirement.</summary>
    public Requirement? Requirement { get; init; }

    /// <summary>Victory point value. Null if the card has no VP.</summary>
    public VictoryPoints? VictoryPoints { get; init; }

    /// <summary>Human-readable description of the card's effects.</summary>
    public required string Description { get; init; }
}

/// <summary>
/// A requirement that must be met to play a card.
/// Multiple conditions can be combined (e.g., oxygen AND tag count).
/// </summary>
public sealed record Requirement
{
    /// <summary>Human-readable description (e.g., "max 5% O2", "3 Oceans").</summary>
    public required string Description { get; init; }

    /// <summary>If true, the numeric thresholds are maximums; otherwise minimums.</summary>
    public bool IsMax { get; init; }

    // Global parameter requirements (null = no requirement for that parameter)
    public int? Oxygen { get; init; }
    public int? Temperature { get; init; }
    public int? Oceans { get; init; }

    // Tag count requirements
    public int? ScienceTags { get; init; }
    public int? EarthTags { get; init; }
    public int? JovianTags { get; init; }

    // Production requirements
    public int? PowerProduction { get; init; }

    // Other requirements
    public int? TitaniumProduction { get; init; }
    public int? PlantProduction { get; init; }
    public int? EnergyProduction { get; init; }
}

/// <summary>
/// Describes the victory points a card provides at end of game.
/// </summary>
public abstract record VictoryPoints
{
    /// <summary>Calculate the VP value given the game context.</summary>
    public abstract int Calculate(VictoryPointContext context);
}

/// <summary>A fixed number of victory points.</summary>
public sealed record FixedVictoryPoints(int Points) : VictoryPoints
{
    public override int Calculate(VictoryPointContext context) => Points;
}

/// <summary>Victory points based on resources on this card (e.g., "1 VP per 2 Animals").</summary>
public sealed record PerResourceVictoryPoints(int PointsPer, int ResourcesPer, string CardId) : VictoryPoints
{
    public override int Calculate(VictoryPointContext context)
    {
        var resources = context.GetCardResources(CardId);
        return (resources / ResourcesPer) * PointsPer;
    }
}

/// <summary>Victory points per tag of a certain type.</summary>
public sealed record PerTagVictoryPoints(int PointsPer, Tag TagType) : VictoryPoints
{
    public override int Calculate(VictoryPointContext context)
    {
        return context.CountTags(TagType) * PointsPer;
    }
}

/// <summary>Context needed to calculate variable VP at end of game.</summary>
public sealed record VictoryPointContext(
    Func<string, int> GetCardResources,
    Func<Tag, int> CountTags,
    Func<int> CountAdjacentOceans);
