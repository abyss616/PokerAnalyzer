using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class SolverLegalActionGeneratorTests
{
    [Fact]
    public void GenerateLegalActions_CheckedToSpot_ReturnsCheckThenBetCategory()
    {
        var acting = Player(seat: 0, stack: 100, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 100, streetContribution: 10, totalContribution: 10);
        var state = CreateState(acting.PlayerId, [acting, villain], pot: 20, currentBetSize: 10, lastRaiseSize: 10);

        var actions = state.GenerateLegalActions();

        Assert.Equal(
            [
                new LegalAction(ActionType.Check),
                new LegalAction(ActionType.Bet)
            ],
            actions);
    }

    [Fact]
    public void GenerateLegalActions_FacingBetWithoutFullRaise_ReturnsFoldAndCallOnly()
    {
        var acting = Player(seat: 0, stack: 30, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 100, streetContribution: 20, totalContribution: 20);
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
    public void GenerateLegalActions_FacingBetWithFullRaise_ReturnsFoldCallRaiseCategory()
    {
        var acting = Player(seat: 0, stack: 90, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 100, streetContribution: 20, totalContribution: 20);
        var state = CreateState(acting.PlayerId, [acting, villain], pot: 30, currentBetSize: 20, lastRaiseSize: 10);

        var actions = state.GenerateLegalActions();

        Assert.Equal(
            [
                new LegalAction(ActionType.Fold),
                new LegalAction(ActionType.Call, new ChipAmount(10)),
                new LegalAction(ActionType.Raise)
            ],
            actions);
    }

    [Fact]
    public void GenerateLegalActions_ShortStackFacingBet_UsesCallShortAmount()
    {
        var acting = Player(seat: 0, stack: 7, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 100, streetContribution: 20, totalContribution: 20);
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
        var villain = Player(seat: 1, stack: 100, streetContribution: 20, totalContribution: 20);
        var state = CreateState(acting.PlayerId, [acting, villain], pot: 30, currentBetSize: 20, lastRaiseSize: 10);
        var provider = new FixedSizeProvider(
            betSizes: [new ChipAmount(40)],
            raiseSizes: [new ChipAmount(25), new ChipAmount(30), new ChipAmount(30), new ChipAmount(130)]);

        var actions = state.GenerateLegalActions(provider);

        Assert.Equal(
            [
                new LegalAction(ActionType.Fold),
                new LegalAction(ActionType.Call, new ChipAmount(10)),
                new LegalAction(ActionType.Raise, new ChipAmount(30)),
                new LegalAction(ActionType.Raise, new ChipAmount(100))
            ],
            actions);
    }

    [Fact]
    public void GenerateLegalActions_ActingPlayerHasNoChips_ReturnsNoActions()
    {
        var acting = Player(seat: 0, stack: 0, streetContribution: 10, totalContribution: 10);
        var villain = Player(seat: 1, stack: 100, streetContribution: 10, totalContribution: 10);
        var state = CreateState(acting.PlayerId, [acting, villain], pot: 20, currentBetSize: 10, lastRaiseSize: 10);

        var actions = SolverLegalActionGenerator.GenerateLegalActions(state);

        Assert.Empty(actions);
    }

    private static SolverPlayerState Player(int seat, long stack, long streetContribution, long totalContribution)
        => new(PlayerId.New(), seat, seat == 0 ? Position.SB : Position.BB, new ChipAmount(stack), new ChipAmount(streetContribution), new ChipAmount(totalContribution), false, false);

    private static SolverHandState CreateState(
        PlayerId actingPlayerId,
        IReadOnlyList<SolverPlayerState> players,
        long pot,
        long currentBetSize,
        long lastRaiseSize)
    {
        return new SolverHandState(
            config: new GameConfig(6, new ChipAmount(5), new ChipAmount(10), ChipAmount.Zero, new ChipAmount(100)),
            street: Street.Preflop,
            buttonSeatIndex: 1,
            actingPlayerId: actingPlayerId,
            pot: new ChipAmount(pot),
            currentBetSize: new ChipAmount(currentBetSize),
            lastRaiseSize: new ChipAmount(lastRaiseSize),
            raisesThisStreet: 1,
            players: players,
            actionHistory: null,
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
