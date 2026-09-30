using TmEngine.Domain.Models;

namespace TmEngine.BgaImport.Tests;

public class MapResolverTests
{
    [Theory]
    [InlineData(Fixtures.Elysium2p, MapName.Elysium)]
    [InlineData(Fixtures.Hellas2p, MapName.Hellas)]
    [InlineData(Fixtures.Tharsis2p, MapName.Tharsis)]
    [InlineData(Fixtures.Tharsis3p, MapName.Tharsis)]
    public void Resolve_ReturnsLabelledMap_WhenMilestonesAgree(string fixture, MapName expected)
    {
        var result = MapResolver.Resolve(Fixtures.Load(fixture));

        Assert.Null(result.Problem);
        Assert.Equal(expected, result.Map);
    }

    [Fact]
    public void Resolve_RejectsLog_WhenMilestonesBelongToAnotherMap()
    {
        var log = Fixtures.Load(Fixtures.MislabelledVastitas);
        Assert.Equal("Tharsis", log.Map);

        var result = MapResolver.Resolve(log);

        Assert.Null(result.Map);
        Assert.Contains("another map", result.Problem);
    }

    [Fact]
    public void Resolve_RejectsUnsupportedMap()
    {
        var log = Fixtures.Load(Fixtures.Tharsis2p) with { Map = "Vastitas Borealis" };

        var result = MapResolver.Resolve(log);

        Assert.Null(result.Map);
        Assert.Contains("not supported", result.Problem);
    }

    [Fact]
    public void Resolve_IdentifiesMapFromMilestones_WhenLabelIsMissing()
    {
        var log = Fixtures.Load(Fixtures.Hellas2p) with { Map = null };

        Assert.Equal(MapName.Hellas, MapResolver.Resolve(log).Map);
    }

    [Theory]
    [InlineData(MapName.Hellas, "POLAR EXPLORER", "Polar Explorer")]
    [InlineData(MapName.Hellas, "Eccentric", "Excentric")]
    [InlineData(MapName.Tharsis, "Planner", "Planner")]
    [InlineData(MapName.Tharsis, "TYCOON", null)]
    public void ResolveMilestoneOrAward_MapsBgaSpellingToEngineName(MapName map, string bgaName, string? expected)
    {
        Assert.Equal(expected, MapResolver.ResolveMilestoneOrAward(map, bgaName));
    }
}
