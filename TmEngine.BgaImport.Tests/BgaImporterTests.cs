using System.Collections.Immutable;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport.Tests;

public class BgaImporterTests
{
    /// <summary>Two-player logs on supported maps, in three different scraper output formats.</summary>
    public static readonly TheoryData<string> Importable = new() { Fixtures.Elysium2p, Fixtures.Hellas2p, Fixtures.Tharsis2p };

    // ── Fork points ────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Importable))]
    public void FindForkPoints_BeginsWithTheStartOfTheGame(string fixture)
    {
        var log = Fixtures.Load(fixture);

        var first = Fixtures.ForkPoints(fixture).ForkPoints[0];

        // The log's first move announces the game; the starting player of generation 1 holds seat 0.
        Assert.Equal(ForkKind.GameStart, first.Kind);
        Assert.Equal(0, first.MoveIndex);
        Assert.Equal(1, first.Generation);
        Assert.Equal(log.Moves[0].GameState!.StartingPlayer, first.ActiveBgaPlayerId);
        Assert.Single(Fixtures.ForkPoints(fixture).ForkPoints, f => f.Kind == ForkKind.GameStart);
    }

    [Fact]
    public void FindForkPoints_PicksGeneration1UpOnceThePreludesArePlayed()
    {
        // Moves 5-9 are the four preludes; cdman234 then plays Nuclear Zone (10-12) and Mineral Deposit (13),
        // and StrandedKnight plays Space Hotels (15-16) and Great Escarpment Consortium (17-18).
        var forks = Fixtures.ForkPoints(Fixtures.Elysium2p).ForkPoints
            .Where(f => f.MoveNumber is > 1 and <= 19)
            .Select(f => (f.MoveNumber, f.Kind, f.ActiveBgaPlayerId));

        const string cdman = "98490496", stranded = "86296239";
        Assert.Equal(
            [
                (9, ForkKind.GenerationStart, cdman),
                (12, ForkKind.ActionStart, cdman),
                (14, ForkKind.ActionStart, stranded),
                (16, ForkKind.ActionStart, stranded),
                (19, ForkKind.ActionStart, cdman),
            ],
            forks);
    }

    [Fact]
    public void FindForkPoints_StartsGeneration1WhereTheFirstActionBegins_NotWhereTheLastPreludeIsPlayed()
    {
        // jesse1288's last prelude is move 7; move 8 only announces Abtsein's turn, which begins with move 9.
        Assert.Equal(8, GenerationStart(Fixtures.Tharsis2p, generation: 1).MoveNumber);

        // Flaming Pile's UNMI Contractor (move 10) draws its card in move 11; Rutabaga00 acts in move 12.
        Assert.Equal(11, GenerationStart(Fixtures.Hellas2p, generation: 1).MoveNumber);
    }

    [Fact]
    public void FindForkPoints_HasNoMiddleOfGeneration1_WhenThePreludesCannotBeFollowed()
    {
        // With one prelude play missing from the log, the fourth "prelude" is cdman234's Nuclear Zone and
        // the end of the opening cannot be told.
        var log = Fixtures.Load(Fixtures.Elysium2p);
        var withoutOnePrelude = log with { Moves = log.Moves.Where(m => m.CardPlayed != "Mohole Excavation").ToList() };
        var (context, _) = BgaImportContext.Create(withoutOnePrelude);

        Assert.Null(context!.Opening);
        Assert.DoesNotContain(ForkPointFinder.Find(context), f => f.Generation == 1 && f.Kind != ForkKind.GameStart);
        Assert.Contains(ForkPointFinder.Find(context), f => f.Generation == 2);
    }

    [Fact]
    public void FindForkPoints_HasNoStartOfTheGame_WhenTheLogDoesNotRecordWhatWasDealt()
    {
        var forks = BgaImporter.FindForkPoints(WithoutStartingHands(Fixtures.Load(Fixtures.Tharsis2p))).ForkPoints;

        Assert.NotEmpty(forks);
        Assert.DoesNotContain(forks, f => f.Kind == ForkKind.GameStart);
    }

    [Theory]
    [MemberData(nameof(Importable))]
    public void FindForkPoints_IncludesTheStartOfEveryGeneration(string fixture)
    {
        var log = Fixtures.Load(fixture);

        var result = Fixtures.ForkPoints(fixture);

        Assert.True(result.Eligible, Describe(result.Issues));
        Assert.Equal(
            Enumerable.Range(1, log.Generations!.Value),
            result.ForkPoints.Where(f => f.Kind == ForkKind.GenerationStart).Select(f => f.Generation));
    }

    [Fact]
    public void FindForkPoints_EndsResearchOnTheLastBuy_NotOnTheFirstAction()
    {
        // Generation 4 of this game: moves 98-106 are the draft and the buys, move 108 is the first card played.
        var fork = GenerationStart(Fixtures.Elysium2p, generation: 4);

        Assert.Equal(106, fork.MoveNumber);
        Assert.Equal("86296239", fork.ActiveBgaPlayerId);
    }

    [Fact]
    public void FindForkPoints_IncludesPlayersWhoBuyNothing_InTheResearchPhase()
    {
        // Generation 7 of this game: both players skip buying (moves 227-228), logged as "other" moves.
        Assert.Equal(228, GenerationStart(Fixtures.Tharsis2p, generation: 7).MoveNumber);
    }

    [Fact]
    public void FindForkPoints_IncludesThePositionBeforeEachAction()
    {
        // Generation 5 of this game opens: 138-139 Abtsein uses Restricted Area, 140 claims Planner,
        // 142 jesse1288 converts heat, 143 ends the turn, 144 Abtsein plays Earth Catapult.
        var forks = Fixtures.ForkPoints(Fixtures.Tharsis2p).ForkPoints
            .Where(f => f.MoveNumber is >= 137 and <= 143)
            .Select(f => (f.MoveNumber, f.Kind, f.ActiveBgaPlayerId));

        const string abtsein = "83900777", jesse = "86465656";
        Assert.Equal(
            [
                (137, ForkKind.GenerationStart, abtsein),
                (139, ForkKind.ActionStart, abtsein),
                (141, ForkKind.ActionStart, jesse),
                (142, ForkKind.ActionStart, jesse),
                (143, ForkKind.ActionStart, abtsein),
            ],
            forks);
    }

    [Fact]
    public void FindForkPoints_SkipsPositionsInTheMiddleOfAnAction()
    {
        // Move 242 announces a patent sale and 243 lists the discards; 243 also logs the opponent passing.
        // The position between the two is mid-sale.
        var forks = Fixtures.ForkPoints(Fixtures.Elysium2p).ForkPoints;

        Assert.Contains(forks, f => f.MoveNumber == 241);
        Assert.DoesNotContain(forks, f => f.MoveNumber == 242);
    }

    [Fact]
    public void FindForkPoints_StopsAtTheFirstPassOfTheFinalGeneration()
    {
        // After everyone passes in the last generation the game goes to final greenery placement, which the
        // log does not mark.
        var log = Fixtures.Load(Fixtures.Tharsis2p);
        var firstPass = log.Moves.FindIndex(m => m.GameState?.Generation == log.Generations && m.Description.Contains(" passes"));

        Assert.All(Fixtures.ForkPoints(Fixtures.Tharsis2p).ForkPoints, f => Assert.True(f.MoveIndex < firstPass));
    }

    [Fact]
    public void SnapToForkPoint_PicksTheNearestForkAtOrBeforeTheMove()
    {
        var forkPoints = Fixtures.ForkPoints(Fixtures.Elysium2p);
        var generation5 = forkPoints.ForkPoints.Single(f => f.Generation == 5 && f.Kind == ForkKind.GenerationStart);
        var lastOfGeneration4 = forkPoints.ForkPoints.Last(f => f.Generation == 4);

        Assert.Equal(generation5, BgaImporter.SnapToForkPoint(forkPoints, generation5.MoveIndex));

        // The move before is in generation 5's research phase, where there is nothing to fork.
        Assert.Equal(lastOfGeneration4, BgaImporter.SnapToForkPoint(forkPoints, generation5.MoveIndex - 1));

        // While the preludes are being played, the only position to go back to is the deal.
        Assert.Equal(ForkKind.GameStart, BgaImporter.SnapToForkPoint(forkPoints, 5)!.Kind);
    }

    [Fact]
    public void ImportAtOrBefore_ImportsTheNearestEarlierPosition()
    {
        var log = Fixtures.Load(Fixtures.Elysium2p);
        var forkPoints = Fixtures.ForkPoints(Fixtures.Elysium2p);
        var inTheDraft = GenerationStart(Fixtures.Elysium2p, generation: 5).MoveIndex - 1;

        var result = BgaImporter.ImportAtOrBefore(log, new BgaImportOptions(inTheDraft));

        Assert.True(result.Success);
        Assert.Equal(BgaImporter.SnapToForkPoint(forkPoints, inTheDraft), result.Fork);
        Assert.Equal(result.Fork!.MoveIndex, result.Meta!.Source.MoveIndex);
    }

    [Fact]
    public void ImportAtOrBefore_DuringThePreludes_GoesBackToTheStartOfTheGame()
    {
        var result = BgaImporter.ImportAtOrBefore(Fixtures.Load(Fixtures.Elysium2p), new BgaImportOptions(MoveIndex: 5));

        Assert.True(result.Success, Describe(result.BlockingIssues));
        Assert.Equal(ForkKind.GameStart, result.Fork!.Kind);
        Assert.Equal(GamePhase.Setup, result.State!.Phase);
    }

    [Fact]
    public void ImportAtOrBefore_Fails_WhenThereIsNoEarlierPosition()
    {
        var log = WithoutStartingHands(Fixtures.Load(Fixtures.Elysium2p));

        var result = BgaImporter.ImportAtOrBefore(log, new BgaImportOptions(MoveIndex: 5));

        Assert.False(result.Success);
        Assert.Contains(result.Issues, i => i.Code == "NOT_A_FORK_POINT");
    }

    // ── Eligibility ────────────────────────────────────────────

    [Theory]
    [InlineData(Fixtures.MislabelledVastitas, "UNSUPPORTED_MAP")]
    [InlineData(Fixtures.OldFormat, "OLD_LOG_FORMAT")]
    public void FindForkPoints_RejectsLogsTheEngineCannotPlay(string fixture, string expectedCode)
    {
        var result = Fixtures.ForkPoints(fixture);

        Assert.False(result.Eligible);
        Assert.Empty(result.ForkPoints);
        Assert.Contains(result.Issues, i => i.Code == expectedCode && i.Severity == IssueSeverity.Blocking);
    }

    [Fact]
    public void FindForkPoints_RejectsColonies()
    {
        var log = Fixtures.Load(Fixtures.Tharsis2p) with { ColoniesOn = true };

        Assert.Contains(BgaImporter.FindForkPoints(log).Issues, i => i.Code == "COLONIES");
    }

    [Fact]
    public void FindForkPoints_RejectsLogsThatDoNotRecordTheirExpansions()
    {
        var log = Fixtures.Load(Fixtures.Tharsis2p) with { PreludeOn = null };

        Assert.Contains(BgaImporter.FindForkPoints(log).Issues, i => i.Code == "OPTIONS_UNKNOWN");
    }

    [Fact]
    public void FindForkPoints_RejectsGamesWithMoreThanTwoPlayers()
    {
        var result = Fixtures.ForkPoints(Fixtures.Tharsis3p);

        Assert.False(result.Eligible);
        Assert.Empty(result.ForkPoints);
        Assert.Contains(result.Issues, i => i.Code == "PLAYER_COUNT" && i.Severity == IssueSeverity.Blocking);
    }

    [Fact]
    public void FindForkPoints_WithoutLiveScoring_LeavesOnlyTheStartOfTheGame()
    {
        // Terraform ratings are unknown for every later position (jesse1288's preludes already raise his), but
        // the start needs none.
        var result = BgaImporter.FindForkPoints(WithoutLiveScoring(Fixtures.Load(Fixtures.Tharsis2p)));

        Assert.True(result.Eligible, Describe(result.Issues));
        Assert.Equal(ForkKind.GameStart, Assert.Single(result.ForkPoints).Kind);
    }

    [Fact]
    public void FindForkPoints_FindsNone_WhenNoPositionSurvivesValidation()
    {
        var result = BgaImporter.FindForkPoints(WithoutStartingHands(WithoutLiveScoring(Fixtures.Load(Fixtures.Tharsis2p))));

        Assert.False(result.Eligible);
        Assert.Empty(result.ForkPoints);
        Assert.Contains(result.Issues, i => i.Code == "NO_FORK_POINTS");
    }

    [Fact]
    public void Import_RejectsAMoveThatIsNotAForkPoint()
    {
        // A move in the middle of generation 5's draft.
        var moveIndex = GenerationStart(Fixtures.Elysium2p, generation: 5).MoveIndex - 3;

        var result = BgaImporter.Import(Fixtures.Load(Fixtures.Elysium2p), new BgaImportOptions(moveIndex));

        Assert.False(result.Success);
        Assert.Contains(result.Issues, i => i.Code == "NOT_A_FORK_POINT");
    }

    [Fact]
    public void Import_RejectsTablesPlayedWithoutLiveScoring()
    {
        // On such a table BGA's scoring table stays at TR 20 all game, so terraform ratings are unknown.
        var log = WithoutLiveScoring(Fixtures.Load(Fixtures.Tharsis2p));

        var result = BgaImporter.Import(log, new BgaImportOptions(GenerationStart(Fixtures.Tharsis2p, generation: 3).MoveIndex));

        Assert.False(result.Success);
        Assert.Contains(result.BlockingIssues, i => i.Code == "NO_LIVE_SCORING");
    }

    [Fact]
    public void Import_RejectsPositions_WhereTheLogsCountersContradictTheCardsFound()
    {
        // Some parses have all-zero counters for a player; tableau and hand then cannot be confirmed.
        const string abtsein = "83900777";
        var log = WithSnapshots(Fixtures.Load(Fixtures.Tharsis2p), s => s with
        {
            PlayerTrackers = s.PlayerTrackers?.ToDictionary(
                t => t.Key,
                t => t.Key == abtsein ? t.Value.ToDictionary(c => c.Key, _ => 0) : t.Value),
        });

        var result = BgaImporter.Import(log, new BgaImportOptions(GenerationStart(Fixtures.Tharsis2p, generation: 3).MoveIndex));

        Assert.False(result.Success);
        Assert.Contains(result.BlockingIssues, i => i.Code is "TAG_COUNT" or "HAND_COUNT");
    }

    // ── Imported state: start of the game ──────────────────────

    [Fact]
    public void Import_AtTheStartOfTheGame_DealsWhatTheRealGameDealt()
    {
        var (state, meta) = Import(Fixtures.Elysium2p, Fixtures.ForkPoints(Fixtures.Elysium2p).ForkPoints[0]);
        var stranded = meta.Seats.Single(s => s.Name == "StrandedKnight").Seat;
        var setup = state.Setup!;

        Assert.Equal(GamePhase.Setup, state.Phase);
        Assert.Equal(1, state.Generation);
        Assert.Equal(0, state.FirstPlayerId);
        Assert.Equal(Ids("Point Luna", "PhoboLog"), setup.DealtCorporations[stranded]);
        Assert.Equal(Ids("Huge Asteroid", "Mohole Excavation", "Polar Industries", "Research Network"), setup.DealtPreludes[stranded]);
        Assert.Equal(10, setup.DealtCards[stranded].Count);
        Assert.Contains(CardNameMap.Resolve("Space Hotels")!, setup.DealtCards[stranded]);
        Assert.All(setup.SubmittedMoves, Assert.Null);

        Assert.All(state.Players, p =>
        {
            Assert.Empty(p.Hand);
            Assert.Equal(Constants.StartingTR, p.TerraformRating);
            Assert.Equal(0, p.Resources.MegaCredits);
        });
        Assert.Empty(state.PlacedTiles);
        Assert.Empty(state.DiscardPile);

        // Every project card is either dealt or in the deck, and no dealt prelude is left in the prelude deck.
        var dealt = setup.DealtCards.SelectMany(c => c).ToList();
        Assert.Equal(20, dealt.Count);
        Assert.Empty(state.DrawPile.Intersect(dealt));
        Assert.Equal(state.DrawPile.Count, state.DrawPile.Distinct().Count());
        Assert.Empty(state.PreludeDeck.Intersect(setup.DealtPreludes.SelectMany(p => p)));
        Assert.Equal(Fixtures.ForkPoints(Fixtures.Elysium2p).ForkPoints[0].MoveIndex, meta.Source.MoveIndex);
    }

    [Theory]
    [MemberData(nameof(Importable))]
    public void Import_AtTheStartOfTheGame_TheRealOpeningLeadsToTheRealPosition(string fixture)
    {
        var log = Fixtures.Load(fixture);
        var result = BgaImporter.Import(log, new BgaImportOptions(MoveIndex: 0));
        Assert.True(result.Success, Describe(result.BlockingIssues));
        Assert.DoesNotContain(result.Issues, i => i.Code == "NEXT_MOVE_NOT_LEGAL");

        var (context, _) = BgaImportContext.Create(log);
        var state = result.State!;
        foreach (var seat in result.Meta!.Seats)
        {
            var opening = ImportValidator.RealOpening(context!, state.Setup!, seat.BgaPlayerId, seat.Seat)!;
            (state, var outcome) = GameEngine.Apply(state, opening);
            Assert.IsType<Success>(outcome);
        }

        // The first snapshot with pile counts is the one taken once everyone has chosen.
        Assert.Equal(log.PreludeOn == true ? GamePhase.PreludePlacement : GamePhase.Action, state.Phase);
        Assert.Equal(log.Moves.First(m => m.GameState?.DiscardPile is not null).GameState!.DiscardPile, state.DiscardPile.Count);
        foreach (var seat in result.Meta.Seats)
            Assert.Equal(CardNameMap.Resolve(log.Players[seat.BgaPlayerId].Corporation!), state.GetPlayer(seat.Seat).CorporationId);
    }

    [Fact]
    public void Import_AtTheStartOfTheGame_PutsTheCardsTheRealGameDrew_OnTopOfTheDeck()
    {
        var log = Fixtures.Load(Fixtures.Elysium2p);
        var (state, meta) = Import(Fixtures.Elysium2p, Fixtures.ForkPoints(Fixtures.Elysium2p).ForkPoints[0]);

        // Point Luna draws Tundra Farming for its own Earth tag as soon as it is chosen, then cdman234's
        // UNMI Contractor draws Mineral Deposit and StrandedKnight's Research Network draws three.
        Assert.Equal(
            Ids("Tundra Farming", "Mineral Deposit", "Nitrophilic Moss", "Great Escarpment Consortium", "Arctic Algae"),
            state.DrawPile.Take(5));

        var (context, _) = BgaImportContext.Create(log);
        foreach (var seat in meta.Seats)
            (state, _) = GameEngine.Apply(state, ImportValidator.RealOpening(context!, state.Setup!, seat.BgaPlayerId, seat.Seat)!);

        var stranded = state.GetPlayer(meta.Seats.Single(s => s.Name == "StrandedKnight").Seat);
        Assert.Contains(CardNameMap.Resolve("Tundra Farming")!, stranded.Hand);
        Assert.Equal(6, stranded.Hand.Count);
        Assert.Equal(38 - 5 * 3, stranded.Resources.MegaCredits);
        Assert.Equal(194, state.DrawPile.Count);
    }

    // ── Imported state: generation 1 ───────────────────────────

    [Fact]
    public void Import_AfterThePreludes_HasWhatTheOpeningAndThePreludesGaveEachPlayer()
    {
        var (state, meta) = Import(Fixtures.Elysium2p, GenerationStart(Fixtures.Elysium2p, generation: 1));
        var stranded = state.GetPlayer(meta.Seats.Single(s => s.Name == "StrandedKnight").Seat);
        var cdman = state.GetPlayer(meta.Seats.Single(s => s.Name == "cdman234").Seat);

        Assert.Equal(GamePhase.Action, state.Phase);
        Assert.Equal(1, state.Generation);
        Assert.Equal(cdman.PlayerId, state.ActivePlayerId);
        Assert.Null(state.Setup);
        Assert.Null(state.Prelude);

        Assert.Equal(Ids("Metals Company", "UNMI Contractor"), cdman.PlayedCards);
        Assert.Equal(Ids("Mohole Excavation", "Research Network"), stranded.PlayedCards);

        // CrediCor's 57 MC less seven cards; Point Luna's 38 less five. UNMI Contractor gave cdman234 3 TR and a
        // card; StrandedKnight drew one for Point Luna's Earth tag and three with Research Network.
        Assert.Equal(36, cdman.Resources.MegaCredits);
        Assert.Equal(23, stranded.Resources.MegaCredits);
        Assert.Equal(23, cdman.TerraformRating);
        Assert.True(cdman.IncreasedTRThisGeneration);
        Assert.False(stranded.IncreasedTRThisGeneration);
        Assert.Equal(8, cdman.Hand.Count);
        Assert.Equal(9, stranded.Hand.Count);
        Assert.Contains(CardNameMap.Resolve("Mineral Deposit")!, cdman.Hand);
        Assert.Contains(CardNameMap.Resolve("Tundra Farming")!, stranded.Hand);

        // The real game's next draw is the three cards for placing Nuclear Zone on Olympus Mons.
        Assert.Equal(Ids("Aerobraked Ammonia Asteroid", "Special Design", "Ecological Zone"), state.DrawPile.Take(3));
    }

    [Fact]
    public void Import_AfterThePreludes_StillOwesACorporationsFirstAction()
    {
        // Rutabaga00 plays Inventrix, whose first action draws three cards: move 12, his first of the game.
        var (state, meta) = Import(Fixtures.Hellas2p, GenerationStart(Fixtures.Hellas2p, generation: 1));
        var rutabaga = state.GetPlayer(meta.Seats.Single(s => s.Name == "Rutabaga00").Seat);
        var flamingPile = state.GetPlayer(meta.Seats.Single(s => s.Name == "Flaming Pile").Seat);

        Assert.False(rutabaga.PerformedFirstAction);
        Assert.True(flamingPile.PerformedFirstAction);
        Assert.Equal(4, rutabaga.Hand.Count);

        var moves = LegalMoveGenerator.GetLegalMoves(state, rutabaga.PlayerId).Actions!;
        Assert.True(moves.CanPerformFirstAction);

        // Taking it draws the three cards the real game drew.
        var (after, result) = GameEngine.Apply(state, new TmEngine.Domain.Moves.PerformFirstActionMove(rutabaga.PlayerId));
        Assert.IsType<Success>(result);
        Assert.Equal(
            Ids("House Printing", "Space Hotels", "Acquired Company").Order(),
            after.GetPlayer(rutabaga.PlayerId).Hand.Except(rutabaga.Hand).Order());
    }

    [Fact]
    public void Import_InGeneration1_CountsAFirstActionAsTheActionItIs()
    {
        // By move 17 Rutabaga00 has taken Inventrix's first action and used the Power Plant standard project.
        var (state, meta) = Import(Fixtures.Hellas2p, ForkAtMove(Fixtures.Hellas2p, 17));
        var rutabaga = state.GetPlayer(meta.Seats.Single(s => s.Name == "Rutabaga00").Seat);

        Assert.True(rutabaga.PerformedFirstAction);
        Assert.Equal(7, rutabaga.Hand.Count);
        Assert.Equal(1, state.Generation);
    }

    // ── Imported state: start of a generation ──────────────────

    [Fact]
    public void Import_BuildsTheStartOfTheActionPhase()
    {
        var (state, meta) = Import(Fixtures.Elysium2p, GenerationStart(Fixtures.Elysium2p, generation: 4));

        Assert.Equal(GamePhase.Action, state.Phase);
        Assert.Equal(4, state.Generation);
        Assert.Equal(MapName.Elysium, state.Map);
        Assert.Null(state.PendingAction);
        Assert.Null(state.Research);
        Assert.Null(state.Draft);

        // StrandedKnight starts generation 4 and therefore acts first.
        var first = meta.Seats.Single(s => s.Name == "StrandedKnight").Seat;
        Assert.Equal(first, state.FirstPlayerId);
        Assert.Equal(first, state.ActivePlayerId);

        Assert.All(state.Players, p =>
        {
            Assert.False(p.Passed);
            Assert.Equal(0, p.ActionsThisTurn);
            Assert.Empty(p.UsedCardActions);
            Assert.False(p.IncreasedTRThisGeneration);
            Assert.True(p.PerformedFirstAction);
        });
    }

    [Fact]
    public void Import_TakesCountersFromTheSnapshot_AndCardsFromTheMoves()
    {
        var (state, meta) = Import(Fixtures.Elysium2p, GenerationStart(Fixtures.Elysium2p, generation: 4));
        var stranded = state.GetPlayer(meta.Seats.Single(s => s.Name == "StrandedKnight").Seat);
        var cdman = state.GetPlayer(meta.Seats.Single(s => s.Name == "cdman234").Seat);

        // After buying 2 and 3 cards from 40 and 32 MC.
        Assert.Equal(34, stranded.Resources.MegaCredits);
        Assert.Equal(23, cdman.Resources.MegaCredits);
        Assert.Equal(CardNameMap.Resolve("Point Luna"), stranded.CorporationId);
        Assert.Equal(CardNameMap.Resolve("CrediCor"), cdman.CorporationId);

        // The log's own hand list for cdman234 has not caught up with his buys at this move; the accumulated hand has.
        Assert.Equal(11, stranded.Hand.Count);
        Assert.Equal(9, cdman.Hand.Count);
        Assert.Contains(CardNameMap.Resolve("AI Central"), stranded.Hand);
        Assert.Contains(CardNameMap.Resolve("Earth Office"), cdman.Hand);

        Assert.Contains(CardNameMap.Resolve("Mohole Excavation"), stranded.PlayedCards);
        Assert.Contains(CardNameMap.Resolve("Nuclear Zone"), cdman.PlayedCards);
        Assert.DoesNotContain(CardNameMap.Resolve("Point Luna"), stranded.PlayedCards);
    }

    [Fact]
    public void Import_PlacesTilesOnTheHexesTheLogNames()
    {
        var (state, meta) = Import(Fixtures.Elysium2p, GenerationStart(Fixtures.Elysium2p, generation: 4));
        var cdman = meta.Seats.Single(s => s.Name == "cdman234").Seat;

        Assert.Equal(new PlacedTile(TileType.NuclearZone, cdman, new HexCoord(8, 3)), state.PlacedTiles[new HexCoord(8, 3)]);
        Assert.Equal(new PlacedTile(TileType.MiningRights, cdman, new HexCoord(7, 8)), state.PlacedTiles[new HexCoord(7, 8)]);
        Assert.Equal(0, state.OceansPlaced);
    }

    [Fact]
    public void Import_CarriesMilestonesAndAwardsUnderEngineNames()
    {
        var (state, meta) = Import(Fixtures.Elysium2p, GenerationStart(Fixtures.Elysium2p, generation: 10));
        var seatOf = meta.Seats.ToDictionary(s => s.Name, s => s.Seat);

        Assert.Equal(
            [new("Generalist", seatOf["cdman234"]), new("Legend", seatOf["cdman234"]), new("Tycoon", seatOf["StrandedKnight"])],
            state.ClaimedMilestones.OrderBy(m => m.MilestoneName));
        Assert.Equal([new AwardFunding("Celebrity", seatOf["StrandedKnight"])], state.FundedAwards);
    }

    [Fact]
    public void Import_RecordsWhoSitsWhereAndWhereTheGameCameFrom()
    {
        var (_, meta) = Import(Fixtures.Elysium2p, GenerationStart(Fixtures.Elysium2p, generation: 4));

        // cdman234 started generation 1, so holds seat 0.
        Assert.Equal(["cdman234", "StrandedKnight"], meta.Seats.OrderBy(s => s.Seat).Select(s => s.Name));
        Assert.Equal("#ffa500", meta.Seats.Single(s => s.Name == "StrandedKnight").Color);
        Assert.Equal("824655675", meta.Source.TableId);
        Assert.Equal(106, meta.Source.MoveNumber);
    }

    // ── Imported state: inside a generation ────────────────────

    [Fact]
    public void Import_MidTurn_CountsTheActionAlreadyTaken()
    {
        // Abtsein has used Restricted Area (moves 138-139) and is about to claim Planner (140).
        var (state, meta) = Import(Fixtures.Tharsis2p, ForkAtMove(Fixtures.Tharsis2p, 139));
        var abtsein = state.GetPlayer(meta.Seats.Single(s => s.Name == "Abtsein").Seat);

        Assert.Equal(abtsein.PlayerId, state.ActivePlayerId);
        Assert.Equal(1, abtsein.ActionsThisTurn);
        Assert.Equal([CardNameMap.Resolve("Restricted Area")], abtsein.UsedCardActions);
        Assert.Empty(state.ClaimedMilestones);
    }

    [Fact]
    public void Import_AfterATurnEnds_HandsTheMoveToTheNextPlayer()
    {
        // Abtsein's two actions are done; jesse1288 is about to convert heat (142).
        var (state, meta) = Import(Fixtures.Tharsis2p, ForkAtMove(Fixtures.Tharsis2p, 141));
        var abtsein = state.GetPlayer(meta.Seats.Single(s => s.Name == "Abtsein").Seat);
        var jesse = state.GetPlayer(meta.Seats.Single(s => s.Name == "jesse1288").Seat);

        Assert.Equal(jesse.PlayerId, state.ActivePlayerId);
        Assert.Equal(abtsein.PlayerId, state.FirstPlayerId);
        Assert.Equal(0, jesse.ActionsThisTurn);
        Assert.Equal(0, abtsein.ActionsThisTurn);
        Assert.Equal([CardNameMap.Resolve("Restricted Area")], abtsein.UsedCardActions);
        Assert.Equal([new MilestoneClaim("Planner", abtsein.PlayerId)], state.ClaimedMilestones);
        Assert.False(jesse.IncreasedTRThisGeneration);
    }

    [Fact]
    public void Import_RemembersATerraformRatingGainThisGeneration()
    {
        // jesse1288 has converted heat (142) and is about to end the turn (143).
        var (state, meta) = Import(Fixtures.Tharsis2p, ForkAtMove(Fixtures.Tharsis2p, 142));
        var jesse = state.GetPlayer(meta.Seats.Single(s => s.Name == "jesse1288").Seat);

        Assert.Equal(1, jesse.ActionsThisTurn);
        Assert.True(jesse.IncreasedTRThisGeneration);
    }

    [Fact]
    public void Import_MarksPlayersWhoHavePassed()
    {
        // StrandedKnight passes at move 63; cdman234 plays on alone and is about to use Space Mirrors (67).
        var (state, meta) = Import(Fixtures.Elysium2p, ForkAtMove(Fixtures.Elysium2p, 66));
        var stranded = state.GetPlayer(meta.Seats.Single(s => s.Name == "StrandedKnight").Seat);
        var cdman = state.GetPlayer(meta.Seats.Single(s => s.Name == "cdman234").Seat);

        Assert.True(stranded.Passed);
        Assert.False(cdman.Passed);
        Assert.Equal(cdman.PlayerId, state.ActivePlayerId);
    }

    [Fact]
    public void Import_KeepsADiscountThatIsWaitingForTheNextCard()
    {
        // StrandedKnight has played Indentured Workers (158) and is about to play AI Central with it (160).
        var (state, meta) = Import(Fixtures.Elysium2p, ForkAtMove(Fixtures.Elysium2p, 159));
        var stranded = state.GetPlayer(meta.Seats.Single(s => s.Name == "StrandedKnight").Seat);

        Assert.Equal(8, stranded.NextCardDiscount);
        Assert.Equal(1, stranded.ActionsThisTurn);
    }

    [Fact]
    public void Import_WorksOutWhichCardAnUnnamedDrawingActionBelongsTo()
    {
        // This parse logs StrandedKnight's AI Central action at move 165 as "keeps 2 cards" without naming the card.
        var (state, meta) = Import(Fixtures.Elysium2p, ForkAtMove(Fixtures.Elysium2p, 165));
        var stranded = state.GetPlayer(meta.Seats.Single(s => s.Name == "StrandedKnight").Seat);

        Assert.Contains(CardNameMap.Resolve("AI Central"), stranded.UsedCardActions);
        Assert.Equal(1, stranded.ActionsThisTurn);
    }

    // ── Deck ───────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Importable))]
    public void Import_PilesAreTheSizeTheLogSays(string fixture)
    {
        var log = Fixtures.Load(fixture);

        // The log has no pile counts until the players have chosen their opening hands.
        foreach (var fork in Fixtures.ForkPoints(fixture).ForkPoints.Where(f => f.Kind != ForkKind.GameStart))
        {
            var state = BgaImporter.Import(log, new BgaImportOptions(fork.MoveIndex)).State!;
            var snapshot = log.Moves.Take(fork.MoveIndex + 1).Last(m => m.GameState is not null).GameState!;

            Assert.Equal(snapshot.DrawPile, state.DrawPile.Count);
            Assert.Equal(snapshot.DiscardPile, state.DiscardPile.Count);
        }
    }

    [Fact]
    public void Import_PutsTheCardsTheRealGameDrewNext_OnTopOfTheDeck()
    {
        var (state, _) = Import(Fixtures.Elysium2p, GenerationStart(Fixtures.Elysium2p, generation: 4));

        // After this fork the real game's first draw is Windmills (move 119), and generation 5's research
        // packs come off the deck in seat order: cdman234's four, then StrandedKnight's.
        Assert.Equal(CardNameMap.Resolve("Windmills"), state.DrawPile[0]);

        var log = Fixtures.Load(Fixtures.Elysium2p);
        var nextResearch = log.Moves.First(m => m.GameState?.Generation == 5 && m.CardOptions is not null).CardOptions!;
        var cdmanPack = nextResearch["98490496"].Select(CardNameMap.Resolve).ToList();
        var strandedPack = nextResearch["86296239"].Select(CardNameMap.Resolve).ToList();
        var top = state.DrawPile.ToList();

        Assert.Equal(top.IndexOf(cdmanPack[0]) + 4, top.IndexOf(strandedPack[0]));
        Assert.Equal(cdmanPack, top.GetRange(top.IndexOf(cdmanPack[0]), 4));
        Assert.Equal(strandedPack, top.GetRange(top.IndexOf(strandedPack[0]), 4));
    }

    [Fact]
    public void Import_ShufflesOnlyTheCardsNobodySaw_AndDoesItBySeed()
    {
        var log = Fixtures.Load(Fixtures.Elysium2p);
        var fork = GenerationStart(Fixtures.Elysium2p, generation: 4);
        List<string> Deck(int seed) => [.. BgaImporter.Import(log, new BgaImportOptions(fork.MoveIndex, ShuffleSeed: seed)).State!.DrawPile];

        var seed1 = Deck(1);
        var seed2 = Deck(2);

        Assert.Equal(seed1, Deck(1));
        Assert.NotEqual(seed1, seed2);
        Assert.Equal(seed1.Order(), seed2.Order());

        // The real game drew 87 more cards after this fork (153 in the pile then, 66 at the end); those stay put.
        Assert.Equal(seed1.Take(87), seed2.Take(87));
    }

    // ── Agreement with the real game ───────────────────────────

    [Theory]
    [MemberData(nameof(Importable))]
    public void EveryForkPoint_AgreesWithTheRealGame_OnScoresAndOnTheNextMoveBeingLegal(string fixture)
    {
        var log = Fixtures.Load(fixture);

        var disagreements = Fixtures.ForkPoints(fixture).ForkPoints
            .SelectMany(fork => BgaImporter.Import(log, new BgaImportOptions(fork.MoveIndex)).Issues
                .Where(i => i.Code is "SCORE_MISMATCH" or "NEXT_MOVE_NOT_LEGAL")
                .Select(i => $"move {fork.MoveNumber}: {i.Message}"))
            .Distinct()
            .ToList();

        Assert.True(disagreements.Count == 0, string.Join("\n", disagreements));
    }

    // ── Playability ────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Importable))]
    public void ForkPoints_CanBePlayedOnWithRandomMoves(string fixture)
    {
        var log = Fixtures.Load(fixture);
        var forks = Fixtures.ForkPoints(fixture).ForkPoints;

        // The start of the game, every generation start, and a spread of the positions inside generations.
        var sample = forks.Where((f, i) => f.Kind != ForkKind.ActionStart || i % 5 == 0);

        var failures = new List<string>();
        foreach (var fork in sample)
        {
            var state = BgaImporter.Import(log, new BgaImportOptions(fork.MoveIndex)).State!;
            for (int seed = 0; seed < 3; seed++)
            {
                var outcome = new RandomPlayer(seed).Play(state, maxMoves: 1500);
                if (outcome.Failure is not null)
                    failures.Add($"move {fork.MoveNumber} (generation {fork.Generation}), seed {seed}, after {outcome.MovesPlayed} moves: {outcome.Failure}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>The log as it would be from a table played with live scoring off: everyone stays at TR 20.</summary>
    private static BgaGameLog WithoutLiveScoring(BgaGameLog log) => WithSnapshots(log, s => s with
    {
        PlayerVp = s.PlayerVp?.ToDictionary(
            v => v.Key,
            v => v.Value with { TotalDetails = (v.Value.TotalDetails ?? new BgaVpTotals()) with { Tr = 20 } }),
    });

    /// <summary>The log as older scraper versions wrote it, without what each player was dealt.</summary>
    private static BgaGameLog WithoutStartingHands(BgaGameLog log) => log with
    {
        Players = log.Players.ToDictionary(p => p.Key, p => p.Value with { StartingHand = null }),
    };

    private static ImmutableList<string> Ids(params string[] names) => [.. names.Select(n => CardNameMap.Resolve(n)!)];

    private static BgaGameLog WithSnapshots(BgaGameLog log, Func<BgaSnapshot, BgaSnapshot> change) => log with
    {
        Moves = log.Moves.Select(m => m.GameState is null ? m : m with { GameState = change(m.GameState) }).ToList(),
    };

    [Fact]
    public void Import_TilesBelongToThePlayerNamedInTheText_NotTheMoveTheyWereLoggedUnder()
    {
        // Most of FennecFoxEnjoyer's greeneries were logged under moves carrying StrandedKnight's id. Read by the
        // text instead, the greenery points agree with BGA's at every fork point.
        foreach (var fork in Fixtures.ForkPoints(Fixtures.ElysiumMisfiledTiles).ForkPoints)
        {
            var result = BgaImporter.Import(Fixtures.Load(Fixtures.ElysiumMisfiledTiles), new BgaImportOptions(fork.MoveIndex));
            Assert.True(result.Success, Describe(result.BlockingIssues));
            Assert.DoesNotContain(result.Issues, i => i.Code == "SCORE_MISMATCH" && i.Message.Contains("greeneries"));
        }
    }

    [Fact]
    public void Import_WarnsWhenACardsTileNeverAppearsInTheLog()
    {
        var fork = GenerationStart(Fixtures.ElysiumMisfiledTiles, generation: 9);
        var result = BgaImporter.Import(Fixtures.Load(Fixtures.ElysiumMisfiledTiles), new BgaImportOptions(fork.MoveIndex));

        Assert.True(result.Success, Describe(result.BlockingIssues));
        var missing = Assert.Single(result.Issues, i => i.Code == "TILE_NOT_IN_LOG");
        Assert.Contains("StrandedKnight's Capital tile", missing.Message);
        Assert.DoesNotContain(result.State!.PlacedTiles.Values, t => t.Type == TileType.Capital);
    }

    private static ForkPoint GenerationStart(string fixture, int generation) =>
        Fixtures.ForkPoints(fixture).ForkPoints
            .Single(f => f.Generation == generation && f.Kind == ForkKind.GenerationStart);

    /// <summary>The fork point whose position is the one after the given BGA move number.</summary>
    private static ForkPoint ForkAtMove(string fixture, int moveNumber) =>
        Fixtures.ForkPoints(fixture).ForkPoints.Single(f => f.MoveNumber == moveNumber);

    private static (GameState State, GameMeta Meta) Import(string fixture, ForkPoint fork)
    {
        var result = BgaImporter.Import(Fixtures.Load(fixture), new BgaImportOptions(fork.MoveIndex));
        Assert.True(result.Success, Describe(result.BlockingIssues));
        return (result.State!, result.Meta!);
    }

    private static string Describe(IEnumerable<ImportIssue> issues) =>
        string.Join("; ", issues.Select(i => $"{i.Code}: {i.Message}"));
}
