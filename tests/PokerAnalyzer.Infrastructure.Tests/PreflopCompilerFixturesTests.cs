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
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PreflopCompiler");
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

            var e = fixture.Expected.ExpectedExtraction;
            if (!result.IsSupported)
            {
                _output.WriteLine($"Fixture '{fixture.Name}' unsupported: {result.UnsupportedReason}");
                _output.WriteLine(FormatTrace(result.Trace));
            }

            Assert.True(result.IsSupported, $"Fixture '{fixture.Name}' unsupported: {result.UnsupportedReason}\n{FormatTrace(result.Trace)}");
            Assert.NotNull(result.Key);
            Assert.Equal(e.ActingPosition, result.Key!.ActingPosition.ToString());
            Assert.Equal(e.FacingPosition, result.Key.FacingPosition?.ToString());
            Assert.Equal(e.HistorySignature, result.Key.HistorySignature);
            Assert.Equal(e.RaiseDepth, result.Key.RaiseDepth);
            Assert.Equal(e.ToCallBb, result.Key.ToCallBb);
            Assert.Equal(e.EffectiveStackBb, result.Key.EffectiveStackBb);
            Assert.Equal(e.Buckets.Open, result.Key.OpenSizeBucket);
            Assert.Equal(e.Buckets.Iso, result.Key.IsoSizeBucket);
            Assert.Equal(e.Buckets.ThreeBet, result.Key.ThreeBetBucket);
            Assert.Equal(e.Buckets.Squeeze, result.Key.SqueezeBucket);
            Assert.Equal(e.Buckets.FourBet, result.Key.FourBetBucket);
            Assert.Equal(e.SolverKey, result.Key.SolverKey);
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
        foreach (var action in trace.RawActionHistory)
            sb.AppendLine($"  [{action.Street}] {action.PlayerId} {action.Position} {action.ActionType} chips={action.AmountChips} bb={action.AmountBb}");

        return sb.ToString();
    }
}
