using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class EquityBasedPreflopLeafEvaluatorTests
{

    [Fact]
    public void Evaluate_MultiwayFacingOpen_RootRoutingStaysDeferredWhileUsingGeneralizedApproximation()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var context = CreateFacingRaiseMultiwayContext(Position.BTN, Position.CO, ActionType.Call);

        var result = evaluator.Evaluate(context);

        Assert.NotNull(result.Details);
        Assert.Equal("GeneralizedFacingRaise", result.Details!.EvaluatorType);
        Assert.Equal("Multiway", result.Details.RootEvaluatorMode);
        Assert.Equal("FacingRaise", result.Details.NodeFamily);
        Assert.False(result.Details.IsHeadsUp);
        Assert.True(result.Details.UsedDirectAbstractionShortcut);
        Assert.False(result.Details.UsedFallbackEvaluator);
    }



    [Fact]
    public void Evaluate_AppliesBlockerFiltering_BeforeEquity()
    {
        var provider = new StaticOpponentRangeProvider(
            new WeightedHoleCards(HoleCards.Parse("AsAd"), 1d),
            new WeightedHoleCards(HoleCards.Parse("KdQd"), 1d));

        var evaluator = new EquityBasedPreflopLeafEvaluator(provider, new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var context = CreateHeadsUpContext(HoleCards.Parse("AsKh"), HoleCards.Parse("QcJc"), ActionType.Raise, "v2/UNOPENED/BTN/eff=100");

        var result = evaluator.Evaluate(context);

        Assert.Contains("filteredCombos=1", result.Reason);
        Assert.DoesNotContain("fallback", result.Reason);
        Assert.NotNull(result.Details);
        Assert.Equal(1, result.Details!.FilteredCombos);
        Assert.Equal("static-test", result.Details.RangeDescription);
        Assert.Equal("static-test", result.Details.RangeDetail);
    }










    [Fact]
    public void Evaluate_UnopenedBtnRaise_IncludesFoldEquityComponents()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var result = evaluator.Evaluate(CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("Jc9d"), ActionType.Raise));

        Assert.NotNull(result.Details);
        Assert.Equal("Raise", result.Details!.RootActionType);
        Assert.NotNull(result.Details.FoldProbability);
        Assert.NotNull(result.Details.ContinueProbability);
        Assert.True(result.Details.ImmediateWinComponent > 0d);
        Assert.NotNull(result.Details.ContinueBranchUtility);
        Assert.NotNull(result.Details.ContinueComponent);
        Assert.Contains("Action=Raise", result.Details.DisplaySummary);
    }



    [Fact]
    public void Evaluate_UnopenedBtn_FoldProbabilityChangesByProfile()
    {
        var gtoEvaluator = new EquityBasedPreflopLeafEvaluator(
            new TableDrivenOpponentRangeProvider(),
            new HeuristicPreflopLeafEvaluator(),
            samplesPerMatchup: 120,
            populationProfileProvider: new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName));

        var microEvaluator = new EquityBasedPreflopLeafEvaluator(
            new TableDrivenOpponentRangeProvider(),
            new HeuristicPreflopLeafEvaluator(),
            samplesPerMatchup: 120,
            populationProfileProvider: new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.MicroStakesLoosePassiveName));

        var context = CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("KcTd"), ActionType.Raise);
        var gto = gtoEvaluator.Evaluate(context);
        var micro = microEvaluator.Evaluate(context);

        Assert.NotNull(gto.Details?.FoldProbability);
        Assert.NotNull(micro.Details?.FoldProbability);
        Assert.NotEqual(gto.Details!.FoldProbability, micro.Details!.FoldProbability);
        Assert.True(micro.Details.FoldProbability < gto.Details.FoldProbability);
        Assert.Contains("sbPct=0.45", gto.Details.RangeDetail);
        Assert.Contains("bbPct=0.45", gto.Details.RangeDetail);
        Assert.Contains("sbPct=0.52", micro.Details.RangeDetail);
        Assert.Contains("bbPct=0.62", micro.Details.RangeDetail);
    }



































    [Fact]
    public void Evaluate_Facing3Bet_ProducesDistinctUtilitiesForCallFourBetAndJam()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(
            new TableDrivenOpponentRangeProvider(),
            new HeuristicPreflopLeafEvaluator(),
            samplesPerMatchup: 120);

        var call = evaluator.Evaluate(CreateFacing3BetProfileContext(Position.CO, Position.BTN, HoleCards.Parse("JsTs"), ActionType.Call));
        var fourBet = evaluator.Evaluate(CreateFacing3BetProfileContext(Position.CO, Position.BTN, HoleCards.Parse("JsTs"), ActionType.Raise, new ChipAmount(2200)));
        var jam = evaluator.Evaluate(CreateFacing3BetProfileContext(Position.CO, Position.BTN, HoleCards.Parse("JsTs"), ActionType.Raise, new ChipAmount(10000)));

        Assert.NotNull(call.Details);
        Assert.NotNull(fourBet.Details);
        Assert.NotNull(jam.Details);
        Assert.Equal("Facing3Bet", call.Details!.NodeFamily);
        Assert.Equal("Facing3Bet", fourBet.Details!.NodeFamily);
        Assert.Equal("Facing3Bet", jam.Details!.NodeFamily);
        Assert.NotEqual(call.Details.HeroUtility, fourBet.Details.HeroUtility);
        Assert.NotEqual(fourBet.Details.HeroUtility, jam.Details.HeroUtility);
        Assert.NotEqual(call.Details.HeroUtility, jam.Details.HeroUtility);
        Assert.Equal(0d, call.Details.FoldProbability);
        Assert.True(fourBet.Details.FoldProbability > 0d);
        Assert.True(jam.Details.FoldProbability > fourBet.Details.FoldProbability);
        Assert.Equal("Jam", jam.Details.RootActionType);
    }






    private static PreflopLeafEvaluationContext CreateHeadsUpContext(HoleCards heroCards, HoleCards villainCards, ActionType rootAction, string solverKey, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var villainId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var config = new GameConfig(2, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(heroId, 0, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(villainId, 1, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var root = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(200),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 1,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(heroId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(villainId, ActionType.PostBigBlind, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards,
                [villainId] = villainCards
            });

        return new PreflopLeafEvaluationContext(
            root,
            root,
            heroId,
            Position.BTN,
            heroCards,
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(250) : ChipAmount.Zero),
            solverKey);
    }

    private static PreflopLeafEvaluationContext CreateLimpOptionBbContext(HoleCards heroCards, HoleCards villainCards, ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var villainId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var config = new GameConfig(2, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(villainId, 0, Position.SB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(heroId, 1, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var root = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(200),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(villainId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(heroId, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(villainId, ActionType.Call, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards,
                [villainId] = villainCards
            });

        return new PreflopLeafEvaluationContext(
            root,
            root,
            heroId,
            Position.BB,
            heroCards,
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(550) : ChipAmount.Zero),
            "v2/LIMP_OPTION/BB/eff=118.5/jam=18");
    }

    private static PreflopLeafEvaluationContext CreateFacingRaiseMultiwayContext(Position heroPosition, Position openerPosition, ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.NewGuid());
        var openerId = new PlayerId(Guid.NewGuid());
        var thirdId = new PlayerId(Guid.NewGuid());
        var fourthId = new PlayerId(Guid.NewGuid());

        var config = new GameConfig(6, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(openerId, 0, openerPosition, new ChipAmount(9700), new ChipAmount(300), new ChipAmount(300), false, false),
            new SolverPlayerState(heroId, 1, heroPosition, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(thirdId, 2, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(fourthId, 3, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        }
        .Where(p => p.Position != heroPosition && p.Position != openerPosition
            || p.PlayerId == openerId || p.PlayerId == heroId)
        .ToArray();

        var sb = players.FirstOrDefault(p => p.Position == Position.SB);
        var bb = players.FirstOrDefault(p => p.Position == Position.BB);
        var history = new List<SolverActionEntry>();
        if (sb is not null)
            history.Add(new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(50)));
        if (bb is not null)
            history.Add(new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(100)));
        history.Add(new SolverActionEntry(openerId, ActionType.Raise, new ChipAmount(300)));

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(450),
            currentBetSize: new ChipAmount(300),
            lastRaiseSize: new ChipAmount(200),
            raisesThisStreet: 1,
            players,
            actionHistory: history,
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [openerId] = HoleCards.Parse("QdJd")
            });

        var solverKey = $"v2/VS_OPEN/{heroPosition}/eff=100/open=3";
        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            heroPosition,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(900) : new ChipAmount(300)),
            solverKey);
    }

    private static PreflopLeafEvaluationContext CreateThreeWayContext(string solverKey = "v2/VS_OPEN/BTN/eff=100", HoleCards? heroCards = null, ActionType rootAction = ActionType.Raise)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var v1 = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var v2 = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var config = new GameConfig(3, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(heroId, 0, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(v1, 1, Position.SB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(v2, 2, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(300),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 1,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(heroId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(v1, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(v2, ActionType.Call, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards ?? HoleCards.Parse("AsKh"),
                [v1] = HoleCards.Parse("QdJd"),
                [v2] = HoleCards.Parse("9c9d")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.BTN,
            heroCards ?? HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? new ChipAmount(250) : ChipAmount.Zero),
            solverKey);
    }

    private static PreflopLeafEvaluationContext CreateFacingRaiseProfileContext(Position heroPosition, Position openerPosition, HoleCards heroCards, ActionType rootAction, ChipAmount? raiseAmount = null, decimal effectiveStackBb = 100m)
    {
        var heroId = new PlayerId(Guid.NewGuid());
        var openerId = new PlayerId(Guid.NewGuid());
        var sbId = new PlayerId(Guid.NewGuid());
        var bbId = new PlayerId(Guid.NewGuid());

        var stackChips = new ChipAmount((long)(effectiveStackBb * 100m));
        var heroPostedBlind = 100L;
        var heroStack = new ChipAmount(Math.Max(0L, stackChips.Value - heroPostedBlind));
        var openerStack = new ChipAmount(Math.Max(0L, stackChips.Value - 300L));
        var config = new GameConfig(6, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, stackChips);
        var allPlayers = new[]
        {
            new SolverPlayerState(openerId, 0, openerPosition, openerStack, new ChipAmount(300), new ChipAmount(300), false, false),
            new SolverPlayerState(heroId, 1, heroPosition, heroStack, new ChipAmount(heroPostedBlind), new ChipAmount(heroPostedBlind), false, false),
            new SolverPlayerState(sbId, 2, Position.SB, new ChipAmount(Math.Max(0L, stackChips.Value - 50L)), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 3, Position.BB, new ChipAmount(Math.Max(0L, stackChips.Value - 100L)), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var players = allPlayers
            .Where(p => p.Position != heroPosition && p.Position != openerPosition
                || p.PlayerId == openerId || p.PlayerId == heroId)
            .ToArray();

        var history = new List<SolverActionEntry>();
        var sb = players.FirstOrDefault(p => p.Position == Position.SB);
        var bb = players.FirstOrDefault(p => p.Position == Position.BB);
        if (sb is not null)
            history.Add(new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(50)));
        if (bb is not null)
            history.Add(new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(100)));
        history.Add(new SolverActionEntry(openerId, ActionType.Raise, new ChipAmount(300)));

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(450),
            currentBetSize: new ChipAmount(300),
            lastRaiseSize: new ChipAmount(200),
            raisesThisStreet: 1,
            players,
            actionHistory: history,
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards,
                [openerId] = HoleCards.Parse("QdJd")
            });

        var amount = rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(900) : new ChipAmount(300);
        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            heroPosition,
            heroCards,
            (double)effectiveStackBb,
            new LegalAction(rootAction, amount),
            $"v2/VS_OPEN/{heroPosition}/eff={effectiveStackBb:0.##}/open=3");
    }

    private static PreflopLeafEvaluationContext CreateFacing3BetProfileContext(Position heroPosition, Position threeBettorPosition, HoleCards heroCards, ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.NewGuid());
        var threeBettorId = new PlayerId(Guid.NewGuid());
        var sbId = new PlayerId(Guid.NewGuid());
        var bbId = new PlayerId(Guid.NewGuid());

        var config = new GameConfig(6, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var allPlayers = new[]
        {
            new SolverPlayerState(threeBettorId, 0, threeBettorPosition, new ChipAmount(9000), new ChipAmount(1000), new ChipAmount(1000), false, false),
            new SolverPlayerState(heroId, 1, heroPosition, new ChipAmount(9750), new ChipAmount(250), new ChipAmount(250), false, false),
            new SolverPlayerState(sbId, 2, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 3, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var players = allPlayers
            .Where(p => p.Position != heroPosition && p.Position != threeBettorPosition
                || p.PlayerId == threeBettorId || p.PlayerId == heroId)
            .ToArray();

        var history = new List<SolverActionEntry>();
        var sb = players.FirstOrDefault(p => p.Position == Position.SB);
        var bb = players.FirstOrDefault(p => p.Position == Position.BB);
        if (sb is not null)
            history.Add(new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(50)));
        if (bb is not null)
            history.Add(new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(100)));
        history.Add(new SolverActionEntry(heroId, ActionType.Raise, new ChipAmount(250)));
        history.Add(new SolverActionEntry(threeBettorId, ActionType.Raise, new ChipAmount(1000)));

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(1450),
            currentBetSize: new ChipAmount(1000),
            lastRaiseSize: new ChipAmount(750),
            raisesThisStreet: 2,
            players,
            actionHistory: history,
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards,
                [threeBettorId] = HoleCards.Parse("AhKh")
            });

        var amount = rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(2200) : new ChipAmount(1000);
        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            heroPosition,
            heroCards,
            100,
            new LegalAction(rootAction, amount),
            $"v2/VS_3BET/{heroPosition}/eff=100/open=2.5/3bet=10");
    }

    private static PreflopLeafEvaluationContext CreateFacing3BetObservedSpotContext(Position heroPosition, Position threeBettorPosition, HoleCards heroCards, ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.NewGuid());
        var threeBettorId = new PlayerId(Guid.NewGuid());
        var sbId = new PlayerId(Guid.NewGuid());
        var bbId = new PlayerId(Guid.NewGuid());

        var config = new GameConfig(6, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(threeBettorId, 0, threeBettorPosition, new ChipAmount(9250), new ChipAmount(600), new ChipAmount(600), false, false),
            new SolverPlayerState(heroId, 1, heroPosition, new ChipAmount(8900), new ChipAmount(350), new ChipAmount(350), false, false),
            new SolverPlayerState(sbId, 2, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 3, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var history = new List<SolverActionEntry>
        {
            new(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
            new(bbId, ActionType.PostBigBlind, new ChipAmount(100)),
            new(heroId, ActionType.Raise, new ChipAmount(350)),
            new(threeBettorId, ActionType.Raise, new ChipAmount(600))
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(1100),
            currentBetSize: new ChipAmount(600),
            lastRaiseSize: new ChipAmount(250),
            raisesThisStreet: 2,
            players,
            actionHistory: history,
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards,
                [threeBettorId] = HoleCards.Parse("AhKh")
            });

        var amount = rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(1800) : new ChipAmount(250);
        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            heroPosition,
            heroCards,
            92.5,
            new LegalAction(rootAction, amount),
            $"v2/VS_3BET/{heroPosition}/eff=92.5/open=3.5/3bet=6/jam=18");
    }

    private static PreflopLeafEvaluationContext CreateFacing3BetMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.NewGuid());
        var threeBettorId = new PlayerId(Guid.NewGuid());
        var callerId = new PlayerId(Guid.NewGuid());
        var bbId = new PlayerId(Guid.NewGuid());

        var config = new GameConfig(5, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(callerId, 0, Position.CO, new ChipAmount(9750), new ChipAmount(250), new ChipAmount(250), false, false),
            new SolverPlayerState(threeBettorId, 1, Position.BTN, new ChipAmount(9000), new ChipAmount(1000), new ChipAmount(1000), false, false),
            new SolverPlayerState(heroId, 2, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 3, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 1,
            actingPlayerId: heroId,
            pot: new ChipAmount(1400),
            currentBetSize: new ChipAmount(1000),
            lastRaiseSize: new ChipAmount(750),
            raisesThisStreet: 2,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(heroId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(callerId, ActionType.Raise, new ChipAmount(250)),
                new SolverActionEntry(threeBettorId, ActionType.Raise, new ChipAmount(1000))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [callerId] = HoleCards.Parse("QdJd"),
                [threeBettorId] = HoleCards.Parse("9c9d")
            });

        var amount = rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(2200) : new ChipAmount(950);
        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.SB,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, amount),
            "v2/VS_3BET/SB/eff=100/open=2.5/3bet=10");
    }



    private static PreflopLeafEvaluationContext CreateBtnFacingLimpMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var limperId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var sbId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var bbId = new PlayerId(Guid.Parse("44444444-4444-4444-4444-444444444444"));

        var config = new GameConfig(4, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(limperId, 0, Position.CO, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(heroId, 1, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(sbId, 2, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(100), false, false),
            new SolverPlayerState(bbId, 3, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 1,
            actingPlayerId: heroId,
            pot: new ChipAmount(350),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(limperId, ActionType.Call, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [limperId] = HoleCards.Parse("QdJd"),
                [sbId] = HoleCards.Parse("9c9d"),
                [bbId] = HoleCards.Parse("8c7c")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.BTN,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(550) : ChipAmount.Zero),
            "v2/LIMP/BTN/eff=100");
    }

    private static PreflopLeafEvaluationContext CreateCoFacingLimpMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var limperId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var btnId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var sbId = new PlayerId(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var bbId = new PlayerId(Guid.Parse("55555555-5555-5555-5555-555555555555"));

        var config = new GameConfig(5, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(limperId, 0, Position.HJ, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(heroId, 1, Position.CO, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(btnId, 2, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(sbId, 3, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 4, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 2,
            actingPlayerId: heroId,
            pot: new ChipAmount(250),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(limperId, ActionType.Call, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [limperId] = HoleCards.Parse("QdJd"),
                [btnId] = HoleCards.Parse("9h8h"),
                [sbId] = HoleCards.Parse("7c7d"),
                [bbId] = HoleCards.Parse("8c6c")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.CO,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(550) : ChipAmount.Zero),
            "v2/LIMP/CO/eff=100");
    }

    private static PreflopLeafEvaluationContext CreateHjUnopenedMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var coId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var btnId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var sbId = new PlayerId(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var bbId = new PlayerId(Guid.Parse("55555555-5555-5555-5555-555555555555"));

        var config = new GameConfig(5, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(heroId, 0, Position.HJ, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(coId, 1, Position.CO, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(btnId, 2, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(sbId, 3, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 4, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 2,
            actingPlayerId: heroId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [coId] = HoleCards.Parse("QdJd"),
                [btnId] = HoleCards.Parse("9h8h"),
                [sbId] = HoleCards.Parse("7c7d"),
                [bbId] = HoleCards.Parse("8c6c")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.HJ,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(250) : ChipAmount.Zero),
            "v2/UNOPENED/HJ/eff=100");
    }

    private static PreflopLeafEvaluationContext CreateCoUnopenedMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var btnId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var sbId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var bbId = new PlayerId(Guid.Parse("44444444-4444-4444-4444-444444444444"));

        var config = new GameConfig(4, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(heroId, 0, Position.CO, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(btnId, 1, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(sbId, 2, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 3, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 1,
            actingPlayerId: heroId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [btnId] = HoleCards.Parse("QdJd"),
                [sbId] = HoleCards.Parse("7c7d"),
                [bbId] = HoleCards.Parse("8c6c")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.CO,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(250) : ChipAmount.Zero),
            "v2/UNOPENED/CO/eff=100");
    }

    private static PreflopLeafEvaluationContext CreateSbUnopenedMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var btnId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var bbId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var config = new GameConfig(3, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(btnId, 0, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(heroId, 1, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 2, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(heroId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [btnId] = HoleCards.Parse("QdJd"),
                [bbId] = HoleCards.Parse("9c9d")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.SB,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(550) : ChipAmount.Zero),
            "v2/UNOPENED_SB/SB/eff=100");
    }

    private static PreflopLeafEvaluationContext CreateThreeWayContextWithLeafActiveOpponents(string solverKey, int leafActiveOpponents)
    {
        var baseline = CreateThreeWayContext(solverKey, HoleCards.Parse("AsKh"));
        var heroId = baseline.HeroPlayerId;

        var updatedPlayers = baseline.LeafState.Players
            .Select(player => player.PlayerId == heroId
                ? player
                : player with { IsFolded = leafActiveOpponents == 0 })
            .ToArray();

        var leaf = baseline.LeafState.With(players: updatedPlayers);
        return baseline with { LeafState = leaf };
    }

    private sealed class StaticOpponentRangeProvider : IOpponentRangeProvider
    {
        private readonly WeightedHoleCards[] _combos;

        public StaticOpponentRangeProvider(params WeightedHoleCards[] combos)
        {
            _combos = combos;
        }

        public bool TryGetRange(OpponentRangeRequest request, out OpponentWeightedRange range, out string reason)
        {
            range = new OpponentWeightedRange(_combos, "static-test");
            reason = "static-test";
            return true;
        }
    }
}
