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
    public void GenerateLegalActions_FacingLimpPreflop_ContainsFoldCallRaiseFivePointFiveBbAndRaiseNineBb()
    {
        var sb = Player(seat: 0, stack: 98, streetContribution: 2, totalContribution: 2);
        var bb = Player(seat: 1, stack: 98, streetContribution: 2, totalContribution: 2);

        var state = CreateState(
            actingPlayerId: sb.PlayerId,
            players: [sb, bb],
            pot: 4,
            currentBetSize: 2,
            lastRaiseSize: 2,
            raisesThisStreet: 0,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2)),
                new SolverActionEntry(bb.PlayerId, ActionType.Call, new ChipAmount(2))
            ]);

        var actions = state.GenerateLegalActions();

        Assert.Equal(
            [
                new LegalAction(ActionType.Fold),
                new LegalAction(ActionType.Call, new ChipAmount(2)),
                new LegalAction(ActionType.Raise, new ChipAmount(11)),
                new LegalAction(ActionType.Raise, new ChipAmount(18))
            ],
            actions);
    }















    [Fact]
    public void GenerateLegalActions_FacingRaisePreflop_SbVsCo_UsesNineElevenAndJam()
    {
        var co = new SolverPlayerState(PlayerId.New(), 0, Position.CO, new ChipAmount(9700), new ChipAmount(300), new ChipAmount(300), false, false);
        var btn = new SolverPlayerState(PlayerId.New(), 1, Position.BTN, new ChipAmount(10000), ChipAmount.Zero, ChipAmount.Zero, true, false);
        var heroSb = new SolverPlayerState(PlayerId.New(), 2, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false);
        var bb = new SolverPlayerState(PlayerId.New(), 3, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false);

        var state = new SolverHandState(
            config: new GameConfig(4, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000)),
            street: Street.Preflop,
            buttonSeatIndex: 1,
            actingPlayerId: heroSb.PlayerId,
            pot: new ChipAmount(450),
            currentBetSize: new ChipAmount(300),
            lastRaiseSize: new ChipAmount(200),
            raisesThisStreet: 1,
            players: [co, btn, heroSb, bb],
            actionHistory:
            [
                new SolverActionEntry(heroSb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(co.PlayerId, ActionType.Raise, new ChipAmount(300)),
                new SolverActionEntry(btn.PlayerId, ActionType.Fold, ChipAmount.Zero)
            ],
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: null);

        var actions = state.GenerateLegalActions();

        Assert.Equal(
            [
                new LegalAction(ActionType.Fold),
                new LegalAction(ActionType.Call, new ChipAmount(300)),
                new LegalAction(ActionType.Raise, new ChipAmount(900)),
                new LegalAction(ActionType.Raise, new ChipAmount(1100)),
                new LegalAction(ActionType.Raise, new ChipAmount(10000))
            ],
            actions);
    }





    [Fact]
    public void GenerateLegalActions_CompletedPreflopState_ReturnsEmpty()
    {
        var sb = Player(seat: 0, stack: 97, streetContribution: 0, totalContribution: 3);
        var bb = Player(seat: 1, stack: 97, streetContribution: 0, totalContribution: 3);

        var state = CreateState(
            actingPlayerId: sb.PlayerId,
            players: [sb, bb],
            pot: 6,
            currentBetSize: 0,
            lastRaiseSize: 2,
            raisesThisStreet: 0,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2)),
                new SolverActionEntry(sb.PlayerId, ActionType.Call, new ChipAmount(2)),
                new SolverActionEntry(bb.PlayerId, ActionType.Check, new ChipAmount(2))
            ]);

        var actions = state.GenerateLegalActions();

        Assert.Empty(actions);
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
    private static SolverHandState CreateFacingRaiseScenario(Position heroPosition, Position openerPosition)
    {
        var hero = new SolverPlayerState(PlayerId.New(), 1, heroPosition, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false);
        var opener = new SolverPlayerState(PlayerId.New(), 0, openerPosition, new ChipAmount(9700), new ChipAmount(300), new ChipAmount(300), false, false);

        var fillerPositions = new[] { Position.UTG, Position.HJ, Position.CO, Position.BTN, Position.SB, Position.BB }
            .Where(p => p != heroPosition && p != openerPosition)
            .Take(3)
            .ToArray();

        var fillers = fillerPositions
            .Select((position, index) =>
            {
                var contribution = position switch
                {
                    Position.SB => 50L,
                    Position.BB => 100L,
                    _ => 0L
                };

                return new SolverPlayerState(
                    PlayerId.New(),
                    2 + index,
                    position,
                    new ChipAmount(10000 - contribution),
                    new ChipAmount(contribution),
                    new ChipAmount(contribution),
                    false,
                    false);
            })
            .ToArray();

        var players = new[] { opener, hero }.Concat(fillers).ToArray();
        var sb = players.FirstOrDefault(p => p.Position == Position.SB);
        var bb = players.FirstOrDefault(p => p.Position == Position.BB);

        var actionHistory = new List<SolverActionEntry>();
        if (sb is not null)
            actionHistory.Add(new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(50)));
        if (bb is not null)
            actionHistory.Add(new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(100)));
        actionHistory.Add(new SolverActionEntry(opener.PlayerId, ActionType.Raise, new ChipAmount(300)));

        return new SolverHandState(
            config: new GameConfig(6, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000)),
            street: Street.Preflop,
            buttonSeatIndex: 2,
            actingPlayerId: hero.PlayerId,
            pot: new ChipAmount(550),
            currentBetSize: new ChipAmount(300),
            lastRaiseSize: new ChipAmount(200),
            raisesThisStreet: 1,
            players: players,
            actionHistory: actionHistory,
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: null);
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
