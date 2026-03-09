using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class SolverLegalActionGeneratorTests
{
    [Fact]
    public void GenerateLegalActions_CheckedToSpot_ReturnsCheckThenBetWithExplicitAmount()
    {
        var acting = Player(seat: 0, stack: 90, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 90, streetContribution: 10, totalContribution: 10);

        var state = CreateState(
            acting.PlayerId,
            [acting, villain],
            pot: 20,
            currentBetSize: 10,
            lastRaiseSize: 10);

        var actions = state.GenerateLegalActions();

        Assert.Equal(
            [
                new LegalAction(ActionType.Check),
                new LegalAction(ActionType.Bet, new ChipAmount(20))
            ],
            actions);

        Assert.DoesNotContain(actions, action => action.ActionType == ActionType.Bet && action.Amount is null);
    }

    [Fact]
 
    public void GenerateLegalActions_FacingBetWithoutFullRaise_ReturnsFoldAndCallOnly()
    {
        var acting = Player(seat: 0, stack: 30, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 80, streetContribution: 20, totalContribution: 20);
        var state = CreateState(acting.PlayerId, [acting, villain], pot: 30, currentBetSize: 20, lastRaiseSize: 25);

        var actions = state.GenerateLegalActions();

        Assert.Equal(
            [
                new LegalAction(ActionType.Fold),
            new LegalAction(ActionType.Call, new ChipAmount(10))
            ],
            actions);
    }

    [Fact]
    public void GenerateLegalActions_UnopenedPreflop_ContainsOnlyFoldLimpAndRaiseToTwoPointFiveBb()
    {
        var sb = Player(seat: 0, stack: 99, streetContribution: 1, totalContribution: 1);
        var bb = Player(seat: 1, stack: 98, streetContribution: 2, totalContribution: 2);

        var state = CreateState(
            actingPlayerId: sb.PlayerId,
            players: [sb, bb],
            pot: 3,
            currentBetSize: 2,
            lastRaiseSize: 2,
            raisesThisStreet: 0,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2))
            ]);

        var actions = state.GenerateLegalActions();

        Assert.Equal(
            [
                new LegalAction(ActionType.Fold),
                new LegalAction(ActionType.Call, new ChipAmount(1)),
                new LegalAction(ActionType.Raise, new ChipAmount(5))
            ],
            actions);

        Assert.DoesNotContain(actions, action => action.ActionType == ActionType.Raise && action.Amount != new ChipAmount(5));
    }

    [Fact]
    public void GenerateLegalActions_AfterLimp_DoesNotUseUnopenedActionSet()
    {
        var sb = Player(seat: 0, stack: 98, streetContribution: 2, totalContribution: 2);
        var bb = Player(seat: 1, stack: 98, streetContribution: 2, totalContribution: 2);

        var state = CreateState(
            actingPlayerId: bb.PlayerId,
            players: [sb, bb],
            pot: 4,
            currentBetSize: 2,
            lastRaiseSize: 2,
            raisesThisStreet: 0,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2)),
                new SolverActionEntry(sb.PlayerId, ActionType.Call, new ChipAmount(2))
            ]);

        var actions = state.GenerateLegalActions();

        Assert.Equal(
            [
                new LegalAction(ActionType.Check),
                new LegalAction(ActionType.Bet, new ChipAmount(4))
            ],
            actions);

        Assert.DoesNotContain(actions, action => action.ActionType == ActionType.Bet && action.Amount is null);
    }
    [Fact]
    public void GenerateLegalActions_FacingBetWithFullRaise_ReturnsFoldCallRaiseCategory()
    {
        var acting = Player(seat: 0, stack: 90, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 80, streetContribution: 20, totalContribution: 20);

        var state = CreateState(acting.PlayerId, [acting, villain], pot: 30, currentBetSize: 20, lastRaiseSize: 10);

        var actions = state.GenerateLegalActions();

        Assert.Equal(
            [
                new LegalAction(ActionType.Fold),
            new LegalAction(ActionType.Call, new ChipAmount(10)),
            new LegalAction(ActionType.Raise, new ChipAmount(30))
            ],
            actions);
    }

    [Fact]
    public void GenerateLegalActions_FacingBet_RaiseActionsAlwaysIncludeAmount()
    {
        var acting = Player(seat: 0, stack: 90, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 80, streetContribution: 20, totalContribution: 20);
        var state = CreateState(acting.PlayerId, [acting, villain], pot: 30, currentBetSize: 20, lastRaiseSize: 10);

        var actions = state.GenerateLegalActions();

        Assert.NotEmpty(actions.Where(action => action.ActionType == ActionType.Raise));
        Assert.DoesNotContain(actions, action => action.ActionType == ActionType.Raise && action.Amount is null);
    }

    [Fact]
    public void GenerateLegalActions_ShortStackFacingBet_UsesCallShortAmount()
    {
        var acting = Player(seat: 0, stack: 7, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 80, streetContribution: 20, totalContribution: 20);
        var state = CreateState(acting.PlayerId, [acting, villain], pot: 30, currentBetSize: 20, lastRaiseSize: 10);

        var actions = state.GenerateLegalActions();

        Assert.Equal(
            [
                new LegalAction(ActionType.Fold),
            new LegalAction(ActionType.Call, new ChipAmount(7))
            ],
            actions);
    }

    [Fact]
    public void GenerateLegalActions_WithSizeProvider_ExpandsAndFiltersSizedActionsDeterministically()
    {
        var acting = Player(seat: 0, stack: 90, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 80, streetContribution: 20, totalContribution: 20);
        var state = CreateState(acting.PlayerId, [acting, villain], pot: 30, currentBetSize: 20, lastRaiseSize: 10);
        var provider = new FixedSizeProvider(
            betSizes: [new ChipAmount(40)],
            raiseSizes: [new ChipAmount(25), new ChipAmount(30), new ChipAmount(30), new ChipAmount(130)]);

        var actions = state.GenerateLegalActions(provider);

        Assert.Equal(
            [
                new LegalAction(ActionType.Fold),
            new LegalAction(ActionType.Call, new ChipAmount(10)),
            new LegalAction(ActionType.Raise, new ChipAmount(35)),
            new LegalAction(ActionType.Raise, new ChipAmount(40)),
            new LegalAction(ActionType.Raise, new ChipAmount(100))
            ],
            actions);
    }

    [Fact]
    public void GenerateLegalActions_WithSizeProvider_BetTargetsAreRelativeToPriorContribution()
    {
        var acting = Player(seat: 0, stack: 85, streetContribution: 15, totalContribution: 15);
        var villain = Player(seat: 1, stack: 85, streetContribution: 15, totalContribution: 15);
        var state = CreateState(acting.PlayerId, [acting, villain], pot: 30, currentBetSize: 15, lastRaiseSize: 10, raisesThisStreet: 1);
        var provider = new FixedSizeProvider(
            betSizes: [new ChipAmount(1), new ChipAmount(10), new ChipAmount(20)],
            raiseSizes: Array.Empty<ChipAmount>());

        var actions = state.GenerateLegalActions(provider);
        var bets = actions.Where(a => a.ActionType == ActionType.Bet).ToArray();

        Assert.Equal(
            [
                new LegalAction(ActionType.Bet, new ChipAmount(25)),
                new LegalAction(ActionType.Bet, new ChipAmount(35)),
                new LegalAction(ActionType.Bet, new ChipAmount(100))
            ],
            bets);
        Assert.All(bets, bet => Assert.True(bet.Amount > acting.CurrentStreetContribution));
    }

    [Fact]
    public void GenerateLegalActions_WithSizeProvider_RaiseTargetsAreRelativeToPriorContribution()
    {
        var acting = Player(seat: 0, stack: 90, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 80, streetContribution: 20, totalContribution: 20);
        var state = CreateState(acting.PlayerId, [acting, villain], pot: 30, currentBetSize: 20, lastRaiseSize: 10);
        var provider = new FixedSizeProvider(
            betSizes: Array.Empty<ChipAmount>(),
            raiseSizes: [new ChipAmount(1), new ChipAmount(20)]);

        var actions = state.GenerateLegalActions(provider);
        var raises = actions.Where(a => a.ActionType == ActionType.Raise).ToArray();

        Assert.Equal(
            [
                new LegalAction(ActionType.Raise, new ChipAmount(30)),
                new LegalAction(ActionType.Raise, new ChipAmount(100))
            ],
            raises);
        Assert.All(raises, raise => Assert.True(raise.Amount > acting.CurrentStreetContribution));
    }

    [Fact]
    public void CreateState_ActingPlayerIsAllIn_Throws()
    {
        var acting = Player(
            seat: 0,
            stack: 0,
            streetContribution: 10,
            totalContribution: 10,
            isAllIn: true);

        var villain = Player(
            seat: 1,
            stack: 90,
            streetContribution: 10,
            totalContribution: 10);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                acting.PlayerId,
                [acting, villain],
                pot: 20,
                currentBetSize: 10,
                lastRaiseSize: 10));

        Assert.Contains("is all-in and cannot act", ex.Message);
    }

    private static SolverPlayerState Player(
     int seat,
     long stack,
     long streetContribution = 0,
     long totalContribution = 0,
     bool isFolded = false,
     bool? isAllIn = null)
    {
        var resolvedIsAllIn = isAllIn ?? (stack == 0 && !isFolded);

        return new SolverPlayerState(
            PlayerId: PlayerId.New(),
            SeatIndex: seat,
            Position: seat == 0 ? Position.SB : Position.BB,
            Stack: new ChipAmount(stack),
            CurrentStreetContribution: new ChipAmount(streetContribution),
            TotalContribution: new ChipAmount(totalContribution),
            IsFolded: isFolded,
            IsAllIn: resolvedIsAllIn);
    }
    private static SolverHandState CreateState(
        PlayerId actingPlayerId,
        IReadOnlyList<SolverPlayerState> players,
        long pot,
        long currentBetSize,
        long lastRaiseSize,
        int raisesThisStreet = 1,
        IReadOnlyList<SolverActionEntry>? actionHistory = null)
    {
        return new SolverHandState(
            config: new GameConfig(6, new ChipAmount(5), new ChipAmount(10), ChipAmount.Zero, new ChipAmount(100)),
            street: Street.Preflop,
            buttonSeatIndex: 1,
            actingPlayerId: actingPlayerId,
            pot: new ChipAmount(pot),
            currentBetSize: new ChipAmount(currentBetSize),
            lastRaiseSize: new ChipAmount(lastRaiseSize),
            raisesThisStreet: raisesThisStreet,
            players: players,
            actionHistory: actionHistory,
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: null);
    }

    private sealed class FixedSizeProvider(IReadOnlyList<ChipAmount> betSizes, IReadOnlyList<ChipAmount> raiseSizes) : IBetSizeSetProvider
    {
        public IReadOnlyList<ChipAmount> GetBetSizes(SolverHandState state) => betSizes;

        public IReadOnlyList<ChipAmount> GetRaiseSizes(SolverHandState state) => raiseSizes;
    }
}
