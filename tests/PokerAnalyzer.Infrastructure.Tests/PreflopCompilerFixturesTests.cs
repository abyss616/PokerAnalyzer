using System.Text;
using System.Text.Json;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using Xunit;
using Xunit.Abstractions;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class PreflopCompilerFixturesTests
{
    private readonly ITestOutputHelper _output;

    public PreflopCompilerFixturesTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Runs_All_PreflopCompiler_Fixtures()
    {
        var fixtureRoot = ResolveFixtureRoot();
        var fixtures = PreflopFixtureLoader.LoadAll(fixtureRoot);
        Assert.Equal(20, fixtures.Count);

        var extractor = new PreflopStateExtractor();

        foreach (var fixture in fixtures)
        {
            var idMap = fixture.Players.Seats.ToDictionary(x => x.PlayerId, _ => PlayerId.New());
            var seats = fixture.Players.Seats.Select(s => new PlayerSeat(
                idMap[s.PlayerId],
                s.PlayerId,
                s.Seat,
                Enum.Parse<Position>(s.Position),
                new ChipAmount(s.StackChips))).ToList();

            var hero = fixture.Players.Seats.Single(s => s.IsHero);
            var actions = fixture.Actions.Actions.Select(a => new PreflopInputAction(idMap[a.Actor], a.Type, a.AmountBb)).ToList();
            var result = extractor.TryExtract(seats, actions, idMap[hero.PlayerId], fixture.Players.Table.SmallBlind, fixture.Players.Table.BigBlind);

            if (ShouldUpdateFixtures())
            {
                UpdateFixtureExpected(fixtureRoot, fixture.Name, result);
                continue;
            }

            var e = fixture.Expected.ExpectedExtraction;
            if (!result.IsSupported)
            {
                _output.WriteLine($"Fixture '{fixture.Name}' unsupported: {result.UnsupportedReason}");
                _output.WriteLine(FormatTrace(result.Trace));
            }

            Assert.True(result.IsSupported, $"Fixture '{fixture.Name}' unsupported: {result.UnsupportedReason}\n{FormatTrace(result.Trace)}");
            Assert.NotNull(result.Key);
            var stringResult = JsonSerializer.Serialize<PreflopInfoSetKey>(result.Key);
            AssertFixtureMatchesExpected(fixture.Name, e, result.Key!, result.Trace);

        }
    }

    [Fact]
    public void Fixtures_Must_Include_Complete_Expected_Extraction_Schema()
    {
        var fixtureRoot = ResolveFixtureRoot();
        var fixtures = PreflopFixtureLoader.LoadAll(fixtureRoot);

        foreach (var fixture in fixtures)
        {
            var e = fixture.Expected.ExpectedExtraction;
            Assert.False(string.IsNullOrWhiteSpace(e.SolverKey), $"Fixture '{fixture.Name}' is missing expectedExtraction.solverKey");

                        Assert.NotNull(e.Buckets.JamThresholdBucketBb);
            Assert.True(e.Buckets.JamThresholdBucketBb > 0, $"Fixture '{fixture.Name}' must include a positive expectedExtraction.buckets.jamThresholdBucketBb");

            if (e.HistorySignature == "VS_OPEN")
                Assert.NotNull(e.Buckets.OpenSizeBucketBb);

            if (e.HistorySignature == "VS_3BET")
                Assert.NotNull(e.Buckets.ThreeBetSizeBucketBb);

            if (e.HistorySignature == "VS_SQUEEZE")
                Assert.NotNull(e.Buckets.SqueezeSizeBucketBb);

            if (e.HistorySignature is "VS_4BET" or "VS_5BET")
                Assert.NotNull(e.Buckets.FourBetSizeBucketBb);
        }
    }



    [Fact]
    public void Extraction_Unopened_Btn_Facing_Blinds_Derives_ToCall_From_Bet_State()
    {
        var extractor = new PreflopStateExtractor();
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(btnId, "BTN", 1, Position.BTN, new ChipAmount(100m)),
            new(sbId, "SB", 2, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 3, Position.BB, new ChipAmount(100m))
        };

        var result = extractor.TryExtract(seats, [], btnId, smallBlind: 0.5m, bigBlind: 1m);

        Assert.True(result.IsSupported, result.UnsupportedReason);
        Assert.NotNull(result.Key);
        Assert.Equal("UNOPENED", result.Key!.HistorySignature);
        Assert.Equal(1m, result.Key.ToCallBb);
        Assert.Equal(result.Key.ToCallBb, result.Trace.ToCallBb);
        Assert.Equal(1m, result.Trace.CurrentBetBb);
        Assert.Equal(0m, result.Trace.ActingContribBb);
        Assert.Equal(1.5m, result.Trace.PotBb);
    }

    [Fact]
    public void Extraction_Unopened_Sb_Facing_Bb_Derives_ToCall_From_Bet_State()
    {
        var extractor = new PreflopStateExtractor();
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(btnId, "BTN", 1, Position.BTN, new ChipAmount(100m)),
            new(sbId, "SB", 2, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 3, Position.BB, new ChipAmount(100m))
        };

        var result = extractor.TryExtract(seats, [], sbId, smallBlind: 0.5m, bigBlind: 1m);

        Assert.True(result.IsSupported, result.UnsupportedReason);
        Assert.NotNull(result.Key);
        Assert.Equal("UNOPENED_SB", result.Key!.HistorySignature);
        Assert.Equal(0.5m, result.Key.ToCallBb);
        Assert.Equal(result.Key.ToCallBb, result.Trace.ToCallBb);
        Assert.Equal(1m, result.Trace.CurrentBetBb);
        Assert.Equal(0.5m, result.Trace.ActingContribBb);
        Assert.Equal(1.5m, result.Trace.PotBb);
    }

    [Fact]
    public void Extraction_Bb_Option_Vs_Sb_Complete_Maps_To_LimpOption_With_Zero_ToCall()
    {
        var extractor = new PreflopStateExtractor();
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(btnId, "BTN", 1, Position.BTN, new ChipAmount(100m)),
            new(sbId, "SB", 2, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 3, Position.BB, new ChipAmount(100m))
        };

        var actions = new List<PreflopInputAction>
        {
            new(sbId, "CALL", 0.5m)
        };

        var result = extractor.TryExtract(seats, actions, bbId, smallBlind: 0.5m, bigBlind: 1m);

        Assert.True(result.IsSupported, result.UnsupportedReason);
        Assert.NotNull(result.Key);
        Assert.Equal("LIMP_OPTION", result.Key!.HistorySignature);
        Assert.Equal(0m, result.Key.ToCallBb);
        Assert.Equal(result.Key.ToCallBb, result.Trace.ToCallBb);
        Assert.Equal(1m, result.Trace.CurrentBetBb);
        Assert.Equal(1m, result.Trace.ActingContribBb);
        Assert.Equal(2m, result.Trace.PotBb);
        Assert.True(result.Trace.HadPriorCallOrCompletion);
        Assert.Equal(Position.BB, result.Trace.ActingPosition);
        Assert.Single(result.Trace.PriorActionsBeforeActing);
        Assert.Equal("CALL", result.Trace.PriorActionsBeforeActing[0].ActionType);
    }

    [Fact]
    public void Extraction_Multiway_Unopened_Preserves_Opponents_And_Players_Behind_Context()
    {
        var extractor = new PreflopStateExtractor();
        var utgId = PlayerId.New();
        var hjId = PlayerId.New();
        var coId = PlayerId.New();
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(utgId, "UTG", 0, Position.UTG, new ChipAmount(100m)),
            new(hjId, "HJ", 1, Position.HJ, new ChipAmount(100m)),
            new(coId, "CO", 2, Position.CO, new ChipAmount(100m)),
            new(btnId, "BTN", 3, Position.BTN, new ChipAmount(100m)),
            new(sbId, "SB", 4, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 5, Position.BB, new ChipAmount(100m))
        };

        var result = extractor.TryExtract(seats, [], hjId, smallBlind: 0.5m, bigBlind: 1m);

        Assert.True(result.IsSupported, result.UnsupportedReason);
        Assert.NotNull(result.Key);
        Assert.Equal("UNOPENED", result.Key!.HistorySignature);
        Assert.True(result.Key.IsMultiway);
        Assert.Equal(5, result.Key.ActiveOpponentCount);
        Assert.Equal(4, result.Key.PlayersBehindCount);
        Assert.Equal(0, result.Key.CallerCount);
        Assert.False(result.Key.HasCallers);
        Assert.True(result.Trace.IsMultiway);
        Assert.Equal(result.Key.ActiveOpponentCount, result.Trace.ActiveOpponentCount);
        Assert.Equal(result.Key.PlayersBehindCount, result.Trace.PlayersBehindCount);
    }

    [Fact]
    public void Extraction_Multiway_Limp_Preserves_Caller_Context()
    {
        var extractor = new PreflopStateExtractor();
        var utgId = PlayerId.New();
        var hjId = PlayerId.New();
        var coId = PlayerId.New();
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(utgId, "UTG", 0, Position.UTG, new ChipAmount(100m)),
            new(hjId, "HJ", 1, Position.HJ, new ChipAmount(100m)),
            new(coId, "CO", 2, Position.CO, new ChipAmount(100m)),
            new(btnId, "BTN", 3, Position.BTN, new ChipAmount(100m)),
            new(sbId, "SB", 4, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 5, Position.BB, new ChipAmount(100m))
        };

        var actions = new List<PreflopInputAction>
        {
            new(utgId, "CALL", 1m)
        };

        var result = extractor.TryExtract(seats, actions, hjId, smallBlind: 0.5m, bigBlind: 1m);

        Assert.True(result.IsSupported, result.UnsupportedReason);
        Assert.NotNull(result.Key);
        Assert.Equal("LIMP", result.Key!.HistorySignature);
        Assert.True(result.Key.IsMultiway);
        Assert.True(result.Key.HasCallers);
        Assert.Equal(1, result.Key.CallerCount);
        Assert.Equal(5, result.Key.ActiveOpponentCount);
        Assert.Equal(4, result.Key.PlayersBehindCount);
        Assert.True(result.Trace.HadPriorCallOrCompletion);
        Assert.True(result.Trace.HasCallers);
    }

    [Fact]
    public void Extraction_Multiway_FacingOpen_Preserves_Callers_And_Players_Behind_Context()
    {
        var extractor = new PreflopStateExtractor();
        var utgId = PlayerId.New();
        var hjId = PlayerId.New();
        var coId = PlayerId.New();
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(utgId, "UTG", 0, Position.UTG, new ChipAmount(100m)),
            new(hjId, "HJ", 1, Position.HJ, new ChipAmount(100m)),
            new(coId, "CO", 2, Position.CO, new ChipAmount(100m)),
            new(btnId, "BTN", 3, Position.BTN, new ChipAmount(100m)),
            new(sbId, "SB", 4, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 5, Position.BB, new ChipAmount(100m))
        };

        var actions = new List<PreflopInputAction>
        {
            new(utgId, "RAISE_TO", 2.5m),
            new(hjId, "CALL", 2.5m)
        };

        var result = extractor.TryExtract(seats, actions, coId, smallBlind: 0.5m, bigBlind: 1m);

        Assert.True(result.IsSupported, result.UnsupportedReason);
        Assert.NotNull(result.Key);
        Assert.Equal("VS_OPEN", result.Key!.HistorySignature);
        Assert.Equal(Position.UTG, result.Key.FacingPosition);
        Assert.True(result.Key.IsMultiway);
        Assert.True(result.Key.HasCallers);
        Assert.Equal(1, result.Key.CallerCount);
        Assert.Equal(5, result.Key.ActiveOpponentCount);
        Assert.Equal(3, result.Key.PlayersBehindCount);
        Assert.Equal(2.5m, result.Key.OpenSizeBucketBb);
        Assert.True(result.Key.ToCallBb > 0m);
        Assert.True(result.Trace.HasCallers);
        Assert.Equal(result.Key.PlayersBehindCount, result.Trace.PlayersBehindCount);
    }

    [Fact]
    public void Validation_MultiwayVsOpen_WithCallerAndPlayersBehind_IsSupported()
    {
        var key = new PreflopInfoSetKey(Position.CO, Position.UTG, "VS_OPEN", 1, 2.5m, 100m, 2.5m, null, null, null, null, 18m, "k", ActiveOpponentCount: 5, CallerCount: 1, PlayersBehindCount: 3);
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.CO, PlayerId.New(), Position.UTG, 1, 2.5m, 2.5m, 0m, 6.5m, 100m, ActiveOpponentCount: 5, CallerCount: 1, PlayersBehindCount: 3);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validation_MismatchedMultiwayTopology_IsUnsupported()
    {
        var key = new PreflopInfoSetKey(Position.CO, Position.UTG, "VS_OPEN", 1, 2.5m, 100m, 2.5m, null, null, null, null, 18m, "k", ActiveOpponentCount: 5, CallerCount: 1, PlayersBehindCount: 3);
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.CO, PlayerId.New(), Position.UTG, 1, 2.5m, 2.5m, 0m, 6.5m, 100m, ActiveOpponentCount: 4, CallerCount: 1, PlayersBehindCount: 2);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.False(result.IsValid);
        Assert.Contains("active opponent count mismatch", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validation_LimpOption_With_Zero_ToCall_IsSupported()
    {
        var key = new PreflopInfoSetKey(Position.BB, Position.BB, "LIMP_OPTION", 0, 0m, 100m, null, null, null, null, null, 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.BB, null, Position.BB, 0, 0m, 1m, 1m, 2m, 100m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validation_Invalid_LimpOption_With_NonZero_ToCall_IsUnsupported()
    {
        var key = new PreflopInfoSetKey(Position.BB, Position.BB, "LIMP_OPTION", 0, 0.5m, 100m, null, null, null, null, null, 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.BB, null, Position.BB, 0, 0.5m, 1m, 0.5m, 2m, 100m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.False(result.IsValid);
        Assert.Contains("LIMP_OPTION requires ToCall == 0", result.Reason);
    }
    [Fact]
    public void Extraction_Uses_Literal_ToCall_For_Open_And_VsOpen()
    {
        var fixtureRoot = ResolveFixtureRoot();
        var fixtures = PreflopFixtureLoader.LoadAll(fixtureRoot);
        var extractor = new PreflopStateExtractor();

        foreach (var fixture in fixtures)
        {
            var idMap = fixture.Players.Seats.ToDictionary(x => x.PlayerId, _ => PlayerId.New());
            var seats = fixture.Players.Seats.Select(s => new PlayerSeat(
                idMap[s.PlayerId],
                s.PlayerId,
                s.Seat,
                Enum.Parse<Position>(s.Position),
                new ChipAmount(s.StackChips))).ToList();

            var hero = fixture.Players.Seats.Single(s => s.IsHero);
            var actions = fixture.Actions.Actions.Select(a => new PreflopInputAction(idMap[a.Actor], a.Type, a.AmountBb)).ToList();
            var result = extractor.TryExtract(seats, actions, idMap[hero.PlayerId], fixture.Players.Table.SmallBlind, fixture.Players.Table.BigBlind);
            Assert.True(result.IsSupported, $"Fixture '{fixture.Name}' unsupported: {result.UnsupportedReason}");

            if (result.Key!.HistorySignature == "OPEN")
                Assert.Equal(0m, result.Key.ToCallBb);

            if (result.Key.HistorySignature == "VS_OPEN")
                Assert.True(result.Key.ToCallBb > 0m, $"Fixture '{fixture.Name}' expected VS_OPEN to have ToCallBb > 0 but got {result.Key.ToCallBb}");
        }
    }

    [Fact]
    public void Validation_Invalid_Open_With_NonZero_ToCall_IsUnsupported()
    {
        var key = new PreflopInfoSetKey(Position.CO, null, "OPEN", 0, 1m, 100m, null, null, null, null, null, 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.CO, null, null, 0, 0m, 2m, 1m, 3m, 100m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.False(result.IsValid);
        Assert.Contains("OPEN with non-zero ToCall", result.Reason);
    }

    [Fact]
    public void Validation_Open_With_Zero_ToCall_In_BigBlind_IsSupported()
    {
        var key = new PreflopInfoSetKey(Position.BB, Position.BB, "OPEN", 0, 0m, 100m, null, null, null, null, null, 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.BB, null, Position.BB, 0, 0m, 1m, 1m, 1.5m, 100m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validation_Invalid_VsOpen_With_Zero_ToCall_IsUnsupported()
    {
        var key = new PreflopInfoSetKey(Position.BB, Position.BTN, "VS_OPEN", 1, 0m, 100m, 2.5m, null, null, null, null, 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.BB, PlayerId.New(), Position.BTN, 1, 0m, 2.5m, 2.5m, 4m, 100m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.False(result.IsValid);
        Assert.Contains("ToCall <= 0", result.Reason);
    }

    [Fact]
    public void Extraction_FacingSqueeze_Uses_VsSqueeze_Signature_And_Squeeze_Bucket()
    {
        var extractor = new PreflopStateExtractor();
        var utgId = PlayerId.New();
        var hjId = PlayerId.New();
        var coId = PlayerId.New();
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(utgId, "UTG", 0, Position.UTG, new ChipAmount(100m)),
            new(hjId, "HJ", 1, Position.HJ, new ChipAmount(100m)),
            new(coId, "CO", 2, Position.CO, new ChipAmount(100m)),
            new(btnId, "BTN", 3, Position.BTN, new ChipAmount(100m)),
            new(sbId, "SB", 4, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 5, Position.BB, new ChipAmount(100m))
        };

        var actions = new List<PreflopInputAction>
        {
            new(utgId, "RAISE_TO", 2.5m),
            new(hjId, "CALL", 2.5m),
            new(coId, "RAISE_TO", 11m)
        };

        var result = extractor.TryExtract(seats, actions, utgId, smallBlind: 0.5m, bigBlind: 1m);

        Assert.True(result.IsSupported, result.UnsupportedReason);
        Assert.NotNull(result.Key);
        Assert.Equal("VS_SQUEEZE", result.Key!.HistorySignature);
        Assert.Equal(2.5m, result.Key.OpenSizeBucketBb);
        Assert.Null(result.Key.ThreeBetSizeBucketBb);
        Assert.Equal(11m, result.Key.SqueezeSizeBucketBb);
        Assert.Contains("/squeeze=11", result.Key.SolverKey);
    }

    [Fact]
    public void Extraction_NormalThreeBet_Control_DoesNot_Populate_Squeeze_Bucket()
    {
        var extractor = new PreflopStateExtractor();
        var utgId = PlayerId.New();
        var coId = PlayerId.New();
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(utgId, "UTG", 0, Position.UTG, new ChipAmount(100m)),
            new(coId, "CO", 1, Position.CO, new ChipAmount(100m)),
            new(btnId, "BTN", 2, Position.BTN, new ChipAmount(100m)),
            new(sbId, "SB", 3, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 4, Position.BB, new ChipAmount(100m))
        };

        var actions = new List<PreflopInputAction>
        {
            new(utgId, "RAISE_TO", 2.5m),
            new(coId, "RAISE_TO", 8m)
        };

        var result = extractor.TryExtract(seats, actions, utgId, smallBlind: 0.5m, bigBlind: 1m);

        Assert.True(result.IsSupported, result.UnsupportedReason);
        Assert.NotNull(result.Key);
        Assert.Equal("VS_3BET", result.Key!.HistorySignature);
        Assert.Equal(2.5m, result.Key.OpenSizeBucketBb);
        Assert.Equal(8m, result.Key.ThreeBetSizeBucketBb);
        Assert.Null(result.Key.SqueezeSizeBucketBb);
    }

    [Fact]
    public void Extraction_Squeeze_Then_FourBet_Preserves_Squeeze_And_FourBet_Buckets()
    {
        var extractor = new PreflopStateExtractor();
        var utgId = PlayerId.New();
        var hjId = PlayerId.New();
        var coId = PlayerId.New();
        var btnId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(utgId, "UTG", 0, Position.UTG, new ChipAmount(100m)),
            new(hjId, "HJ", 1, Position.HJ, new ChipAmount(100m)),
            new(coId, "CO", 2, Position.CO, new ChipAmount(100m)),
            new(btnId, "BTN", 3, Position.BTN, new ChipAmount(100m)),
            new(sbId, "SB", 4, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 5, Position.BB, new ChipAmount(100m))
        };

        var actions = new List<PreflopInputAction>
        {
            new(utgId, "RAISE_TO", 2.5m),
            new(hjId, "CALL", 2.5m),
            new(coId, "RAISE_TO", 11m)
        };

        var result = extractor.TryExtract(
            seats,
            [.. actions, new PreflopInputAction(utgId, "RAISE_TO", 25m)],
            hjId,
            smallBlind: 0.5m,
            bigBlind: 1m);

        Assert.True(result.IsSupported, result.UnsupportedReason);
        Assert.NotNull(result.Key);
        Assert.Equal("VS_4BET", result.Key!.HistorySignature);
        Assert.Equal(2.5m, result.Key.OpenSizeBucketBb);
        Assert.Equal(11m, result.Key.SqueezeSizeBucketBb);
        Assert.Equal(25m, result.Key.FourBetSizeBucketBb);
        Assert.Null(result.Key.ThreeBetSizeBucketBb);
    }

    [Fact]
    public void Validation_VsSqueeze_Requires_Squeeze_Bucket()
    {
        var key = new PreflopInfoSetKey(Position.UTG, Position.CO, "VS_SQUEEZE", 2, 8.5m, 89m, 2.5m, null, null, null, null, 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.UTG, PlayerId.New(), Position.CO, 2, 8.5m, 11m, 2.5m, 17m, 89m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.False(result.IsValid);
        Assert.Contains("VS_SQUEEZE requires squeeze size bucket", result.Reason);
    }

    [Fact]
    public void Extraction_MultiDecision_Slices_Classify_Unopened_Then_Vs3Bet_For_Same_Hero()
    {
        var extractor = new PreflopStateExtractor();
        var coId = PlayerId.New();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(coId, "CO", 1, Position.CO, new ChipAmount(100m)),
            new(sbId, "SB", 2, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 3, Position.BB, new ChipAmount(100m))
        };

        // Action 1 (hero first decision): unopened CO facing blinds.
        var action1 = extractor.TryExtract(seats, [], coId, smallBlind: 0.5m, bigBlind: 1m);
        Assert.True(action1.IsSupported, action1.UnsupportedReason);
        Assert.NotNull(action1.Key);
        Assert.Equal("UNOPENED", action1.Key!.HistorySignature);

        // Action 2 (hero second decision): hero opened and now faces BB 3bet.
        var actionsBeforeAction2 = new List<PreflopInputAction>
        {
            new(coId, "RAISE_TO", 2.5m),
            new(sbId, "FOLD", 0m),
            new(bbId, "RAISE_TO", 9m)
        };

        var action2 = extractor.TryExtract(seats, actionsBeforeAction2, coId, smallBlind: 0.5m, bigBlind: 1m);
        Assert.True(action2.IsSupported, action2.UnsupportedReason);
        Assert.NotNull(action2.Key);
        Assert.Equal("VS_3BET", action2.Key!.HistorySignature);
        Assert.Equal(2, action2.Key.RaiseDepth);
        Assert.True(action2.Key.ToCallBb > 0m);
    }

    [Fact]
    public void Extraction_TrueHu_VsOpen_Keeps_Current_Key_Shape()
    {
        var extractor = new PreflopStateExtractor();
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var seats = new List<PlayerSeat>
        {
            new(sbId, "SB", 0, Position.SB, new ChipAmount(100m)),
            new(bbId, "BB", 1, Position.BB, new ChipAmount(100m))
        };

        var actions = new List<PreflopInputAction>
        {
            new(sbId, "RAISE_TO", 2.5m)
        };

        var result = extractor.TryExtract(seats, actions, bbId, smallBlind: 0.5m, bigBlind: 1m);

        Assert.True(result.IsSupported, result.UnsupportedReason);
        Assert.NotNull(result.Key);
        Assert.Equal("VS_OPEN", result.Key!.HistorySignature);
        Assert.False(result.Key.IsMultiway);
        Assert.False(result.Key.HasCallers);
        Assert.Equal(1, result.Key.ActiveOpponentCount);
        Assert.Equal(0, result.Key.CallerCount);
        Assert.Equal(0, result.Key.PlayersBehindCount);
        Assert.Equal("v2/VS_OPEN/BB/eff=97.5/open=2.5/jam=18", result.Key.SolverKey);
    }

    [Fact]
    public void Validation_Valid_Unopened_Sb_Spot_Passes()
    {
        var key = new PreflopInfoSetKey(Position.SB, null, "UNOPENED_SB", 0, 0.5m, 100m, null, null, null, null, null, 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.SB, null, null, 0, 0m, 0.5m, 0.5m, 1.5m, 100m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validation_Failure_DoesNot_Invoke_Solver_Query()
    {
        var key = new PreflopInfoSetKey(Position.CO, null, "OPEN", 0, 1m, 100m, null, null, null, null, null, 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.CO, null, null, 0, 0m, 2m, 1m, 3m, 100m);
        var solver = new RecordingSolverClient();

        var validation = await ValidateAndQuerySolverAsync(key, ctx, solver, CancellationToken.None);

        Assert.False(validation.IsValid);
        Assert.Equal(0, solver.QueryCount);
    }

    private static string FormatTrace(PreflopQueryTrace trace)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Trace: Sig={trace.HistorySignature} Depth={trace.RaiseDepth} ToCallBb={trace.ToCallBb} Facing={trace.FacingPosition} SolverKey={trace.SolverKey}");
        sb.AppendLine($"  Buckets: O={trace.OpenSizeBucket} ISO={trace.IsoSizeBucket} 3B={trace.ThreeBetBucket} SQZ={trace.SqueezeBucket} 4B={trace.FourBetBucket} JAM={trace.JamThreshold}");
        foreach (var action in trace.RawActionHistory)
            sb.AppendLine($"  [{action.Street}] {action.PlayerId} {action.Position} {action.ActionType} chips={action.AmountChips} bb={action.AmountBb}");

        return sb.ToString();
    }


    private static string ResolveFixtureRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..",
            "Fixtures",
            "PreflopCompiler"));
    }

    private static bool ShouldUpdateFixtures()
        => string.Equals(Environment.GetEnvironmentVariable("UPDATE_FIXTURES"), "1", StringComparison.Ordinal);

    private void AssertFixtureMatchesExpected(string fixtureName, ExpectedExtraction expected, PreflopInfoSetKey actual, PreflopQueryTrace trace)
    {
        var mismatches = new List<string>();

        Compare("actingPosition", expected.ActingPosition, actual.ActingPosition.ToString(), mismatches);
        Compare("facingPosition", expected.FacingPosition, actual.FacingPosition?.ToString(), mismatches);
        Compare("historySignature", expected.HistorySignature, actual.HistorySignature, mismatches);
        Compare("raiseDepth", expected.RaiseDepth, actual.RaiseDepth, mismatches);
        Compare("toCallBb", expected.ToCallBb, actual.ToCallBb, mismatches);
        Compare("effectiveStackBb", expected.EffectiveStackBb, actual.EffectiveStackBb, mismatches);
        Compare("buckets.openSizeBucketBb", expected.Buckets.OpenSizeBucketBb, actual.OpenSizeBucketBb, mismatches);
        Compare("buckets.isoSizeBucketBb", expected.Buckets.IsoSizeBucketBb, actual.IsoSizeBucketBb, mismatches);
        Compare("buckets.threeBetSizeBucketBb", expected.Buckets.ThreeBetSizeBucketBb, actual.ThreeBetSizeBucketBb, mismatches);
        Compare("buckets.squeezeSizeBucketBb", expected.Buckets.SqueezeSizeBucketBb, actual.SqueezeSizeBucketBb, mismatches);
        Compare("buckets.fourBetSizeBucketBb", expected.Buckets.FourBetSizeBucketBb, actual.FourBetSizeBucketBb, mismatches);
        Compare("buckets.jamThresholdBucketBb", expected.Buckets.JamThresholdBucketBb, actual.JamThresholdBucketBb, mismatches);
        Compare("solverKey", expected.SolverKey, actual.SolverKey, mismatches);
        Compare("trace.solverKey", expected.SolverKey, trace.SolverKey, mismatches);

        if (mismatches.Count == 0)
            return;

        var message = $"Fixture '{fixtureName}' mismatches:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", mismatches)}{Environment.NewLine}{FormatTrace(trace)}";
        Assert.Fail(message);
    }

    private static void Compare<T>(string field, T expected, T actual, List<string> mismatches)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            mismatches.Add($"{field}: expected '{expected}', actual '{actual}'");
    }

    private static void UpdateFixtureExpected(string fixtureRoot, string fixtureName, PreflopExtractionResult result)
    {
        if (!result.IsSupported || result.Key is null)
            return;

        var key = result.Key;
        var expected = new
        {
            expectedExtraction = new
            {
                actingPosition = key.ActingPosition.ToString(),
                facingPosition = key.FacingPosition?.ToString(),
                historySignature = key.HistorySignature,
                raiseDepth = key.RaiseDepth,
                toCallBb = key.ToCallBb,
                effectiveStackBb = key.EffectiveStackBb,
                buckets = new
                {
                    openSizeBucketBb = key.OpenSizeBucketBb,
                    isoSizeBucketBb = key.IsoSizeBucketBb,
                    threeBetSizeBucketBb = key.ThreeBetSizeBucketBb,
                    squeezeSizeBucketBb = key.SqueezeSizeBucketBb,
                    fourBetSizeBucketBb = key.FourBetSizeBucketBb,
                    jamThresholdBucketBb = key.JamThresholdBucketBb
                },
                solverKey = key.SolverKey
            }
        };

        var path = Path.Combine(fixtureRoot, fixtureName, "expected.json");
        var json = System.Text.Json.JsonSerializer.Serialize(expected, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json + Environment.NewLine);
    }

    private static async Task<PreflopValidationResult> ValidateAndQuerySolverAsync(
        PreflopInfoSetKey key,
        PreflopSpotContext ctx,
        RecordingSolverClient solver,
        CancellationToken ct)
    {
        var validation = PreflopKeyValidator.Validate(key, ctx);
        if (!validation.IsValid)
            return validation;

        await solver.QueryStrategyAsync(key.SolverKey, ct);
        return validation;
    }

    private sealed class RecordingSolverClient
    {
        public int QueryCount { get; private set; }

        public Task QueryStrategyAsync(string _, CancellationToken __)
        {
            QueryCount++;
            return Task.CompletedTask;
        }
    }
}
