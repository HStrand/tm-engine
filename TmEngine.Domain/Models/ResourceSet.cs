namespace TmEngine.Domain.Models;

/// <summary>
/// Tracks the amount of each standard resource a player holds.
/// </summary>
public sealed record ResourceSet(
    int MegaCredits = 0,
    int Steel = 0,
    int Titanium = 0,
    int Plants = 0,
    int Energy = 0,
    int Heat = 0)
{
    public static readonly ResourceSet Zero = new();

    public int Get(ResourceType type) => type switch
    {
        ResourceType.MegaCredits => MegaCredits,
        ResourceType.Steel => Steel,
        ResourceType.Titanium => Titanium,
        ResourceType.Plants => Plants,
        ResourceType.Energy => Energy,
        ResourceType.Heat => Heat,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public ResourceSet With(ResourceType type, int value) => type switch
    {
        ResourceType.MegaCredits => this with { MegaCredits = value },
        ResourceType.Steel => this with { Steel = value },
        ResourceType.Titanium => this with { Titanium = value },
        ResourceType.Plants => this with { Plants = value },
        ResourceType.Energy => this with { Energy = value },
        ResourceType.Heat => this with { Heat = value },
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public ResourceSet Add(ResourceType type, int amount) =>
        With(type, Get(type) + amount);

    public static ResourceSet operator +(ResourceSet a, ResourceSet b) => new(
        a.MegaCredits + b.MegaCredits,
        a.Steel + b.Steel,
        a.Titanium + b.Titanium,
        a.Plants + b.Plants,
        a.Energy + b.Energy,
        a.Heat + b.Heat);
}

/// <summary>
/// Tracks the production level of each standard resource.
/// MC production can go negative (minimum -5).
/// </summary>
public sealed record ProductionSet(
    int MegaCredits = 0,
    int Steel = 0,
    int Titanium = 0,
    int Plants = 0,
    int Energy = 0,
    int Heat = 0)
{
    public static readonly ProductionSet Zero = new();

    public int Get(ResourceType type) => type switch
    {
        ResourceType.MegaCredits => MegaCredits,
        ResourceType.Steel => Steel,
        ResourceType.Titanium => Titanium,
        ResourceType.Plants => Plants,
        ResourceType.Energy => Energy,
        ResourceType.Heat => Heat,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public ProductionSet With(ResourceType type, int value) => type switch
    {
        ResourceType.MegaCredits => this with { MegaCredits = value },
        ResourceType.Steel => this with { Steel = value },
        ResourceType.Titanium => this with { Titanium = value },
        ResourceType.Plants => this with { Plants = value },
        ResourceType.Energy => this with { Energy = value },
        ResourceType.Heat => this with { Heat = value },
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public ProductionSet Add(ResourceType type, int amount) =>
        With(type, Get(type) + amount);
}
