using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class HeuristicPreflopLeafEvaluatorTests
{
    [Fact]
    public void Evaluate_UnopenedButton_J9oRaise_IsBetterThanFold()
    {
        var context = CreateContext(HoleCards.Parse("Jc9h"), Position.BTN, ActionType.Raise, 250);
        var evaluator = new HeuristicPreflopLeafEvaluator();

        var evaluation = evaluator.Evaluate(context);

        Assert.True(evaluation.UtilityByPlayer[context.HeroPlayerId] > 0d);
        Assert.Contains("evRaise=", evaluation.Reason);
    }

    [Fact]
    public void Evaluate_UnopenedButton_AARaise_IsHigherThan_SevenTwoOffsuitRaise()
    {
        var evaluator = new HeuristicPreflopLeafEvaluator();
        var aaContext = CreateContext(HoleCards.Parse("AsAh"), Position.BTN, ActionType.Raise, 250);
        var seventyTwoContext = CreateContext(HoleCards.Parse("7c2d"), Position.BTN, ActionType.Raise, 250);

        var aaEval = evaluator.Evaluate(aaContext);
        var seventyTwoEval = evaluator.Evaluate(seventyTwoContext);

        Assert.True(aaEval.UtilityByPlayer[aaContext.HeroPlayerId] > seventyTwoEval.UtilityByPlayer[seventyTwoContext.HeroPlayerId]);
    }

    private static PreflopLeafEvaluationContext CreateContext(HoleCards heroCards, Position heroPosition, ActionType firstAction, long raiseToAmount)
    {
        var state = CreateUnopenedFirstInState(heroCards, heroPosition, firstAction, raiseToAmount);
        var heroId = state.ActionHistory.Last().PlayerId;
        var hero = state.Players.First(player => player.PlayerId == heroId);

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            hero.Position,
            heroCards,
            100,
            new LegalAction(firstAction, firstAction == ActionType.Raise ? new ChipAmount(raiseToAmount) : ChipAmount.Zero),
            "v2/UNOPENED/BTN/eff=100");
    }

    private static SolverHandState CreateUnopenedFirstInState(HoleCards heroCards, Position heroPosition, ActionType firstAction, long raiseToAmount)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var bbId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var sbId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var config = new GameConfig(6, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));

        var players = new[]
        {
            new SolverPlayerState(heroId, 5, heroPosition, new ChipAmount(10000), ChipAmount.Zero, ChipAmount.Zero, false, false),
            new SolverPlayerState(sbId, 0, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 1, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var actionAmount = firstAction switch
        {
            ActionType.Fold => ChipAmount.Zero,
            ActionType.Call => new ChipAmount(100),
            ActionType.Raise => new ChipAmount(raiseToAmount),
            _ => throw new ArgumentOutOfRangeException(nameof(firstAction))
        };

        return new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 5,
            actingPlayerId: bbId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: firstAction == ActionType.Raise ? 1 : 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(heroId, firstAction, actionAmount)
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards,
                [sbId] = HoleCards.Parse("QcJh"),
                [bbId] = HoleCards.Parse("9s8s")
            });
    }
}
