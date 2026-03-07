using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class SolverInfoSetKeyMapperTests
{
    private static readonly GameConfig Config = new(
        MaxPlayers: 6,
        SmallBlind: new ChipAmount(5),
        BigBlind: new ChipAmount(10),
        Ante: ChipAmount.Zero,
        StartingStack: new ChipAmount(100));

    [Fact]
    public void Same_Observable_Preflop_State_Maps_To_Same_Key()
    {
        var mapper = new SolverInfoSetKeyMapper();
        var stateA = CreateFacingOpenState(HoleCards.Parse("AsKh"), HoleCards.Parse("7c7d"));
        var stateB = CreateFacingOpenState(HoleCards.Parse("AsKh"), HoleCards.Parse("7c7d"));

        var keyA = mapper.Map(stateA);
        var keyB = mapper.Map(stateB);

        Assert.True(keyA.IsSupported);
        Assert.True(keyB.IsSupported);
        Assert.Equal(keyA.Key, keyB.Key);
        Assert.Equal(keyA.PreflopKey, keyB.PreflopKey);
    }

    [Fact]
    public void Opponent_Hole_Cards_Are_Excluded_From_InfoSet_Key()
    {
        var mapper = new SolverInfoSetKeyMapper();
        var stateA = CreateFacingOpenState(HoleCards.Parse("AsKh"), HoleCards.Parse("7c7d"));
        var stateB = CreateFacingOpenState(HoleCards.Parse("AsKh"), HoleCards.Parse("QcQd"));

        var keyA = mapper.Map(stateA);
        var keyB = mapper.Map(stateB);

        Assert.True(keyA.IsSupported);
        Assert.True(keyB.IsSupported);
        Assert.Equal(keyA.Key, keyB.Key);
    }

    [Fact]
    public void Different_Actor_Hole_Cards_Map_To_Different_Key()
    {
        var mapper = new SolverInfoSetKeyMapper();
        var stateA = CreateFacingOpenState(HoleCards.Parse("AsKh"), HoleCards.Parse("7c7d"));
        var stateB = CreateFacingOpenState(HoleCards.Parse("AdKd"), HoleCards.Parse("7c7d"));

        var keyA = mapper.Map(stateA);
        var keyB = mapper.Map(stateB);

        Assert.True(keyA.IsSupported);
        Assert.True(keyB.IsSupported);
        Assert.NotEqual(keyA.Key, keyB.Key);
    }

    [Fact]
    public void Different_Public_Action_History_Maps_To_Different_Key()
    {
        var mapper = new SolverInfoSetKeyMapper();
        var facingOpen = CreateFacingOpenState(HoleCards.Parse("AsKh"), HoleCards.Parse("7c7d"));
        var limped = CreateLimpedToBigBlindState(HoleCards.Parse("AsKh"), HoleCards.Parse("7c7d"));

        var keyA = mapper.Map(facingOpen);
        var keyB = mapper.Map(limped);

        Assert.True(keyA.IsSupported);
        Assert.True(keyB.IsSupported);
        Assert.NotEqual(keyA.Key, keyB.Key);
        Assert.NotEqual(keyA.PreflopKey, keyB.PreflopKey);
    }

    [Fact]
    public void Mapping_Is_Deterministic_For_Equality_Hash_And_Ordering()
    {
        var mapper = new SolverInfoSetKeyMapper();
        var state = CreateFacingOpenState(HoleCards.Parse("AsKh"), HoleCards.Parse("7c7d"));

        var first = mapper.Map(state).Key!;
        var second = mapper.Map(state).Key!;

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(0, first.CompareTo(second));

        var other = mapper.Map(CreateFacingOpenState(HoleCards.Parse("2c2d"), HoleCards.Parse("7c7d"))).Key!;
        var ordered = new[] { first, other }.OrderBy(x => x).ToArray();
        Assert.Equal(2, ordered.Length);
    }

    [Fact]
    public void Postflop_Board_Cards_Affect_Key()
    {
        var mapper = new SolverInfoSetKeyMapper();
        var flopA = CreatePostflopState(Card.Parse("Ah"), Card.Parse("Kd"), Card.Parse("7c"));
        var flopB = CreatePostflopState(Card.Parse("Ah"), Card.Parse("Qd"), Card.Parse("7c"));

        var keyA = mapper.Map(flopA);
        var keyB = mapper.Map(flopB);

        Assert.True(keyA.IsSupported);
        Assert.True(keyB.IsSupported);
        Assert.NotEqual(keyA.Key, keyB.Key);
    }

    private static SolverHandState CreateFacingOpenState(HoleCards actorCards, HoleCards buttonCards)
    {
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var btnId = PlayerId.New();

        return new SolverHandState(
            Config,
            Street.Preflop,
            buttonSeatIndex: 2,
            actingPlayerId: bbId,
            pot: new ChipAmount(40),
            currentBetSize: new ChipAmount(25),
            lastRaiseSize: new ChipAmount(15),
            raisesThisStreet: 1,
            players:
            [
                new SolverPlayerState(sbId, 0, Position.SB, new ChipAmount(95), new ChipAmount(5), new ChipAmount(5), false, false),
                new SolverPlayerState(bbId, 1, Position.BB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false),
                new SolverPlayerState(btnId, 2, Position.BTN, new ChipAmount(75), new ChipAmount(25), new ChipAmount(25), false, false)
            ],
            actionHistory:
            [
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(5)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(10)),
                new SolverActionEntry(btnId, ActionType.Raise, new ChipAmount(25))
            ],
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [sbId] = HoleCards.Parse("2s3s"),
                [bbId] = actorCards,
                [btnId] = buttonCards
            });
    }

    private static SolverHandState CreateLimpedToBigBlindState(HoleCards actorCards, HoleCards buttonCards)
    {
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();
        var btnId = PlayerId.New();

        return new SolverHandState(
            Config,
            Street.Preflop,
            buttonSeatIndex: 2,
            actingPlayerId: bbId,
            pot: new ChipAmount(25),
            currentBetSize: new ChipAmount(10),
            lastRaiseSize: ChipAmount.Zero,
            raisesThisStreet: 0,
            players:
            [
                new SolverPlayerState(sbId, 0, Position.SB, new ChipAmount(95), new ChipAmount(5), new ChipAmount(5), false, false),
                new SolverPlayerState(bbId, 1, Position.BB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false),
                new SolverPlayerState(btnId, 2, Position.BTN, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false)
            ],
            actionHistory:
            [
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(5)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(10)),
                new SolverActionEntry(btnId, ActionType.Call, new ChipAmount(10))
            ],
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [sbId] = HoleCards.Parse("2s3s"),
                [bbId] = actorCards,
                [btnId] = buttonCards
            });
    }

    private static SolverHandState CreatePostflopState(Card c1, Card c2, Card c3)
    {
        var sbId = PlayerId.New();
        var bbId = PlayerId.New();

        return new SolverHandState(
            Config,
            Street.Flop,
            buttonSeatIndex: 1,
            actingPlayerId: bbId,
            pot: new ChipAmount(20),
            currentBetSize: ChipAmount.Zero,
            lastRaiseSize: ChipAmount.Zero,
            raisesThisStreet: 0,
            players:
            [
                new SolverPlayerState(sbId, 0, Position.SB, new ChipAmount(90), ChipAmount.Zero, new ChipAmount(10), false, false),
                new SolverPlayerState(bbId, 1, Position.BB, new ChipAmount(90), ChipAmount.Zero, new ChipAmount(10), false, false)
            ],
            boardCards: [c1, c2, c3],
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [sbId] = HoleCards.Parse("2s3s"),
                [bbId] = HoleCards.Parse("AsKh")
            });
    }
}
