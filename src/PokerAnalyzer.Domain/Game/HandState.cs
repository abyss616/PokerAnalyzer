namespace PokerAnalyzer.Domain.Game;

/// <summary>
/// Runtime state as actions are applied. This is intentionally minimal; it is sufficient to support
/// (a) action legality checks, (b) pot/stack accounting, and (c) decision-point extraction.
/// </summary>
public sealed class HandState
{
    public Street Street { get; private set; }
    public Board Board { get; set; }
    public ChipAmount Pot { get; private set; }

    /// <summary>Current bet size to call on this street (i.e. the highest street contribution among active players).</summary>
    public ChipAmount BetToCall { get; private set; }

    /// <summary>Total chips contributed by each player on the current street.</summary>
    public IReadOnlyDictionary<PlayerId, ChipAmount> StreetContrib => _streetContrib;

    /// <summary>Current stacks (remaining chips) for each player.</summary>
    public IReadOnlyDictionary<PlayerId, ChipAmount> Stacks => _stacks;

    public IReadOnlySet<PlayerId> ActivePlayers => _activePlayers;

    /// <summary>
    /// Last voluntary aggressor on the current street (bet/raise). IMPORTANT: posting blinds does NOT set this.
    /// </summary>
    public PlayerId? LastAggressor { get; private set; }

    private readonly Dictionary<PlayerId, ChipAmount> _streetContrib;
    private readonly Dictionary<PlayerId, ChipAmount> _stacks;
    private readonly HashSet<PlayerId> _activePlayers;

    private HandState(
        Street street,
        Board board,
        ChipAmount pot,
        ChipAmount betToCall,
        Dictionary<PlayerId, ChipAmount> streetContrib,
        Dictionary<PlayerId, ChipAmount> stacks,
        HashSet<PlayerId> activePlayers,
        PlayerId? lastAggressor)
    {
        Street = street;
        Board = board;
        Pot = pot;
        BetToCall = betToCall;
        _streetContrib = streetContrib;
        _stacks = stacks;
        _activePlayers = activePlayers;
        LastAggressor = lastAggressor;
    }

    /// <summary>
    /// Creates a new hand state and applies forced blinds as setup (NOT as actions).
    /// </summary>
    public static HandState CreateNewHand(
        IEnumerable<PlayerSeat> seats,
        ChipAmount smallBlind,
        ChipAmount bigBlind,
        Street street = Street.Preflop,
        Board? board = null)
    {
        var seatList = seats.ToList();

        var stacks = seatList.ToDictionary(s => s.Id, s => s.StartingStack);
        var contrib = seatList.ToDictionary(s => s.Id, _ => ChipAmount.Zero);
        var active = seatList.Select(s => s.Id).ToHashSet();

        var pot = ChipAmount.Zero;

        // Forced blinds are posted here. They do NOT imply aggression.
        if (smallBlind.Value > 0)
        {
            var sbSeat = seatList.FirstOrDefault(s => s.Position == Position.SB);
            if (sbSeat is not null)
            {
                PostBlind(sbSeat.Id, smallBlind, stacks, contrib, ref pot);
            }
        }

        ChipAmount bbPosted = ChipAmount.Zero;
        if (bigBlind.Value > 0)
        {
            var bbSeat = seatList.FirstOrDefault(s => s.Position == Position.BB);
            if (bbSeat is not null)
            {
                bbPosted = PostBlind(bbSeat.Id, bigBlind, stacks, contrib, ref pot);
            }
        }

        // BetToCall at start of preflop is the effective big blind (can be short/all-in).
        var betToCall = bbPosted.Value > 0 ? bbPosted : ChipAmount.Zero;

        // IMPORTANT: posting blinds does NOT set LastAggressor.
        PlayerId? lastAggressor = null;

        return new HandState(street, board ?? new Board(), pot, betToCall, contrib, stacks, active, lastAggressor);
    }

    private static ChipAmount PostBlind(
        PlayerId playerId,
        ChipAmount blind,
        IDictionary<PlayerId, ChipAmount> stacks,
        IDictionary<PlayerId, ChipAmount> contrib,
        ref ChipAmount pot)
    {
        var stack = stacks[playerId];
        var posted = blind.Value > stack.Value ? stack : blind; // allow short blind
        stacks[playerId] = new ChipAmount(stack.Value - posted.Value);
        contrib[playerId] = new ChipAmount(contrib[playerId].Value + posted.Value);
        pot = new ChipAmount(pot.Value + posted.Value);
        return posted;
    }

    public HandState Clone()
        => new HandState(
            Street,
            Board,
            Pot,
            BetToCall,
            new Dictionary<PlayerId, ChipAmount>(_streetContrib),
            new Dictionary<PlayerId, ChipAmount>(_stacks),
            new HashSet<PlayerId>(_activePlayers),
            LastAggressor);

    public ChipAmount GetToCall(PlayerId playerId)
    {
        _streetContrib.TryGetValue(playerId, out var already);
        var toCall = BetToCall - already;
        return toCall.Value < 0 ? ChipAmount.Zero : toCall;
    }

    public IReadOnlyList<ActionType> GetLegalActions(PlayerId playerId)
    {
        if (!_activePlayers.Contains(playerId))
            return Array.Empty<ActionType>();

        var stack = _stacks[playerId];
        var toCall = GetToCall(playerId);

        var actions = new List<ActionType>(6);

        if (toCall.Value == 0)
        {
            actions.Add(ActionType.Check);
            if (stack.Value > 0)
            {
                actions.Add(ActionType.Bet);
                actions.Add(ActionType.AllIn);
            }
        }
        else
        {
            actions.Add(ActionType.Fold);
            if (stack.Value > 0)
            {
                actions.Add(ActionType.Call);
                actions.Add(ActionType.Raise);
                actions.Add(ActionType.AllIn);
            }
        }

        return actions;
    }

    /// <summary>
    /// Applies a betting decision. Forced blind posts may be routed here when actions are recorded explicitly.
    /// </summary>
    public HandState Apply(BettingAction action)
    {
        if (!_activePlayers.Contains(action.ActorId))
            throw new InvalidOperationException("Actor is not active in this hand (folded or unknown).");

        if (!_stacks.ContainsKey(action.ActorId))
            throw new InvalidOperationException("Unknown actor stack.");

        return action.Type switch
        {
            ActionType.Fold => ApplyFold(action.ActorId),
            ActionType.Check => ApplyCheck(action.ActorId),
            ActionType.Call => ApplyCall(action.ActorId),
            ActionType.Bet => ApplyBetOrRaise(action.ActorId, action.Amount, isRaise: false),
            ActionType.Raise => ApplyBetOrRaise(action.ActorId, action.Amount, isRaise: true),
            ActionType.AllIn => ApplyAllIn(action.ActorId, action.Amount),
            ActionType.PostSmallBlind => ApplyPostBlind(action.ActorId, action.Amount, isBigBlind: false),
            ActionType.PostBigBlind => ApplyPostBlind(action.ActorId, action.Amount, isBigBlind: true),
            _ => throw new ArgumentOutOfRangeException(nameof(action.Type),
                $"Unsupported action type in HandState.Apply: {action.Type}.")
        };
    }

    private HandState ApplyFold(PlayerId actor)
    {
        var st = Clone();
        st._activePlayers.Remove(actor);
        return st;
    }

    private HandState ApplyCheck(PlayerId actor)
    {
        var toCall = GetToCall(actor);
        if (toCall.Value != 0)
            throw new InvalidOperationException("Cannot check when facing a bet.");

        return Clone(); // no chip movement
    }

    private HandState ApplyCall(PlayerId actor)
    {
        var st = Clone();
        var toCall = st.GetToCall(actor);

        // If nothing to call, calling is the same as checking.
        if (toCall.Value == 0)
            return st; // or: return st.ApplyCheck(actor);

        // (optional) defensive: negative should never happen
        if (toCall.Value < 0)
            throw new InvalidOperationException("Invalid to-call amount.");

        var stack = st._stacks[actor];
        var pay = toCall.Value > stack.Value ? stack : toCall; // all-in call allowed

        st._stacks[actor] = new ChipAmount(stack.Value - pay.Value);
        st._streetContrib[actor] = new ChipAmount(st._streetContrib[actor].Value + pay.Value);
        st.Pot = new ChipAmount(st.Pot.Value + pay.Value);

        return st;
    }


    /// <summary>
    /// Bet/Raise uses "toAmount" semantics: total contribution on this street AFTER the action.
    /// </summary>
    private HandState ApplyBetOrRaise(PlayerId actor, ChipAmount toAmount, bool isRaise)
    {
        if (toAmount.Value <= 0)
            throw new InvalidOperationException("Bet/Raise amount must be > 0 and represent total street contribution after the action.");

        var st = Clone();
        var already = st._streetContrib[actor];

        if (isRaise && toAmount.Value <= st.BetToCall.Value)
            throw new InvalidOperationException("Raise must increase the bet-to-call.");

        if (!isRaise && st.BetToCall.Value != 0)
            throw new InvalidOperationException("Cannot bet when a bet already exists; use Raise.");

        var delta = new ChipAmount(toAmount.Value - already.Value);
        if (delta.Value <= 0)
            throw new InvalidOperationException("Bet/Raise must increase actor contribution.");

        var stack = st._stacks[actor];
        if (delta.Value > stack.Value)
            throw new InvalidOperationException("Insufficient stack for requested bet/raise amount.");

        st._stacks[actor] = new ChipAmount(stack.Value - delta.Value);
        st._streetContrib[actor] = new ChipAmount(already.Value + delta.Value);
        st.Pot = new ChipAmount(st.Pot.Value + delta.Value);

        st.BetToCall = toAmount;
        st.LastAggressor = actor;

        return st;
    }

    private HandState ApplyAllIn(PlayerId actor, ChipAmount toAmount)
    {
        var st = Clone();
        var already = st._streetContrib[actor];
        var stack = st._stacks[actor];

        // If toAmount is provided, treat it as "to contribution".
        // If it is zero, push the entire remaining stack.
        var target = toAmount.Value > 0 ? toAmount : new ChipAmount(already.Value + stack.Value);
        var delta = new ChipAmount(target.Value - already.Value);

        if (delta.Value <= 0)
            throw new InvalidOperationException("All-in must increase actor contribution.");

        if (delta.Value > stack.Value)
            throw new InvalidOperationException("All-in exceeds available stack (inconsistent state).");

        st._stacks[actor] = new ChipAmount(stack.Value - delta.Value);
        st._streetContrib[actor] = new ChipAmount(already.Value + delta.Value);
        st.Pot = new ChipAmount(st.Pot.Value + delta.Value);

        // Only counts as aggression if it increases BetToCall.
        if (target.Value > st.BetToCall.Value)
        {
            st.BetToCall = target;
            st.LastAggressor = actor;
        }

        return st;
    }

    private HandState ApplyPostBlind(PlayerId actor, ChipAmount blind, bool isBigBlind)
    {
        if (blind.Value <= 0)
            throw new InvalidOperationException("Blind amount must be > 0.");

        var st = Clone();
        var already = st._streetContrib[actor];
        if (already.Value > 0)
            throw new InvalidOperationException("Blind already posted for this player.");

        var stack = st._stacks[actor];
        var posted = blind.Value > stack.Value ? stack : blind; // allow short blind

        st._stacks[actor] = new ChipAmount(stack.Value - posted.Value);
        st._streetContrib[actor] = new ChipAmount(already.Value + posted.Value);
        st.Pot = new ChipAmount(st.Pot.Value + posted.Value);

        if (isBigBlind && posted.Value > st.BetToCall.Value)
        {
            st.BetToCall = posted;
        }

        return st;
    }
}
