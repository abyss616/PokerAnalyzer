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
