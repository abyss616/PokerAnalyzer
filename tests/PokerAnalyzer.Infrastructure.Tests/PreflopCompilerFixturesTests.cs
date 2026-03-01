using System.Text;
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

            AssertBucketPresent(fixture.Name, "openSizeBucket", e.Buckets.OpenSizeBucket);
            AssertBucketPresent(fixture.Name, "isoSizeBucket", e.Buckets.IsoSizeBucket);
            AssertBucketPresent(fixture.Name, "threeBetSizeBucket", e.Buckets.ThreeBetSizeBucket);
            AssertBucketPresent(fixture.Name, "squeezeSizeBucket", e.Buckets.SqueezeSizeBucket);
            AssertBucketPresent(fixture.Name, "fourBetSizeBucket", e.Buckets.FourBetSizeBucket);

            Assert.True(e.Buckets.JamThreshold > 0, $"Fixture '{fixture.Name}' must include a positive expectedExtraction.buckets.jamThreshold");

            if (e.HistorySignature == "VS_3BET")
                Assert.NotEqual("NA", e.Buckets.ThreeBetSizeBucket);

            if (e.HistorySignature is "VS_4BET" or "VS_5BET")
                Assert.NotEqual("NA", e.Buckets.FourBetSizeBucket);
        }
    }

    [Fact]
    public void Validation_Invalid_Open_With_Zero_ToCall_IsUnsupported()
    {
        var key = new PreflopInfoSetKey(Position.CO, null, "OPEN", 0, 0m, 100m, "MEDIUM", "NA", "NA", "NA", "NA", 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.CO, null, null, 0, 0m, 2m, 1m, 3m, 100m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.False(result.IsValid);
        Assert.Contains("OPEN with ToCall == 0", result.Reason);
    }

    [Fact]
    public void Validation_Open_With_Zero_ToCall_In_BigBlind_IsSupported()
    {
        var key = new PreflopInfoSetKey(Position.BB, Position.BB, "OPEN", 0, 0m, 100m, "SMALL", "NA", "NA", "NA", "NA", 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.BB, null, Position.BB, 0, 0m, 1m, 1m, 1.5m, 100m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validation_Invalid_VsOpen_With_Zero_ToCall_IsUnsupported()
    {
        var key = new PreflopInfoSetKey(Position.BB, Position.BTN, "VS_OPEN", 1, 0m, 100m, "MEDIUM", "NA", "NA", "NA", "NA", 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.BB, PlayerId.New(), Position.BTN, 1, 0m, 2.5m, 2.5m, 4m, 100m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.False(result.IsValid);
        Assert.Contains("VS_*", result.Reason);
    }

    [Fact]
    public void Validation_Valid_Unopened_Sb_Spot_Passes()
    {
        var key = new PreflopInfoSetKey(Position.SB, null, "UNOPENED_SB", 0, 0m, 100m, "SMALL", "NA", "NA", "NA", "NA", 18m, "k");
        var ctx = new PreflopSpotContext(PlayerId.New(), Position.SB, null, null, 0, 0m, 0.5m, 0.5m, 1.5m, 100m);

        var result = PreflopKeyValidator.Validate(key, ctx);

        Assert.True(result.IsValid);
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
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures"));
        var preflopCompilerRoot = Path.Combine(fixturesRoot, "PreflopCompiler");

        return Directory.Exists(preflopCompilerRoot) ? preflopCompilerRoot : fixturesRoot;
    }

    private static bool ShouldUpdateFixtures()
        => string.Equals(Environment.GetEnvironmentVariable("UPDATE_FIXTURES"), "1", StringComparison.Ordinal);

    private static void AssertBucketPresent(string fixtureName, string bucketName, string value)
        => Assert.False(string.IsNullOrWhiteSpace(value), $"Fixture '{fixtureName}' is missing expectedExtraction.buckets.{bucketName}");

    private void AssertFixtureMatchesExpected(string fixtureName, ExpectedExtraction expected, PreflopInfoSetKey actual, PreflopQueryTrace trace)
    {
        var mismatches = new List<string>();

        Compare("actingPosition", expected.ActingPosition, actual.ActingPosition.ToString(), mismatches);
        Compare("facingPosition", expected.FacingPosition, actual.FacingPosition?.ToString(), mismatches);
        Compare("historySignature", expected.HistorySignature, actual.HistorySignature, mismatches);
        Compare("raiseDepth", expected.RaiseDepth, actual.RaiseDepth, mismatches);
        Compare("toCallBb", expected.ToCallBb, actual.ToCallBb, mismatches);
        Compare("effectiveStackBb", expected.EffectiveStackBb, actual.EffectiveStackBb, mismatches);
        Compare("buckets.openSizeBucket", expected.Buckets.OpenSizeBucket, actual.OpenSizeBucket, mismatches);
        Compare("buckets.isoSizeBucket", expected.Buckets.IsoSizeBucket, actual.IsoSizeBucket, mismatches);
        Compare("buckets.threeBetSizeBucket", expected.Buckets.ThreeBetSizeBucket, actual.ThreeBetBucket, mismatches);
        Compare("buckets.squeezeSizeBucket", expected.Buckets.SqueezeSizeBucket, actual.SqueezeBucket, mismatches);
        Compare("buckets.fourBetSizeBucket", expected.Buckets.FourBetSizeBucket, actual.FourBetBucket, mismatches);
        Compare("buckets.jamThreshold", expected.Buckets.JamThreshold, actual.JamThreshold, mismatches);
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
                    openSizeBucket = key.OpenSizeBucket,
                    isoSizeBucket = key.IsoSizeBucket,
                    threeBetSizeBucket = key.ThreeBetBucket,
                    squeezeSizeBucket = key.SqueezeBucket,
                    fourBetSizeBucket = key.FourBetBucket,
                    jamThreshold = key.JamThreshold
                },
                solverKey = key.SolverKey
            }
        };

        var path = Path.Combine(fixtureRoot, fixtureName, "expected.json");
        var json = System.Text.Json.JsonSerializer.Serialize(expected, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json + Environment.NewLine);
    }
}
