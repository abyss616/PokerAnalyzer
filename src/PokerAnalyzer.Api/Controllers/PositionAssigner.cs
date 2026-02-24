using PokerAnalyzer.Domain.Game;

internal static class PositionAssigner
{
    public static IReadOnlyDictionary<int, Position> Assign(
        IReadOnlyList<HandPlayer> players,
        IReadOnlyList<HandAction> actions,
        int? dealerSeatNumber)
    {
        var orderedSeats = players
            .Where(p => p.Seat > 0)
            .OrderBy(p => p.Seat)
            .ToList();

        if (orderedSeats.Count == 0)
            return new Dictionary<int, Position>();

        var seats = orderedSeats.Select(p => p.Seat).ToList();
        var buttonSeat = ResolveButtonSeat(players, orderedSeats, actions, dealerSeatNumber);
        var buttonIndex = seats.IndexOf(buttonSeat);
        if (buttonIndex < 0)
            throw new InvalidOperationException($"Unable to map positions: computed button seat {buttonSeat} is not an active seat.");

        var clockwiseFromButton = orderedSeats
            .Skip(buttonIndex)
            .Concat(orderedSeats.Take(buttonIndex))
            .ToList();

        var labels = GetLabels(clockwiseFromButton.Count);
        var mapped = new Dictionary<int, Position>(clockwiseFromButton.Count);
        for (var i = 0; i < clockwiseFromButton.Count; i++)
            mapped[clockwiseFromButton[i].Seat] = labels[i];

        return mapped;
    }

    private static int ResolveButtonSeat(
        IReadOnlyList<HandPlayer> allPlayers,
        IReadOnlyList<HandPlayer> activeSeats,
        IReadOnlyList<HandAction> actions,
        int? dealerSeatNumber)
    {
        if (dealerSeatNumber.HasValue && activeSeats.Any(p => p.Seat == dealerSeatNumber.Value))
            return dealerSeatNumber.Value;

        var blinds = actions
            .Where(a => a.Street == Street.Preflop && (a.Type == ActionType.PostSmallBlind || a.Type == ActionType.PostBigBlind))
            .Take(2)
            .ToList();

        if (blinds.Count >= 2)
        {
            var sb = allPlayers.FirstOrDefault(p => string.Equals(p.Name, blinds[0].Player, StringComparison.OrdinalIgnoreCase));
            if (sb is not null)
            {
                var orderedSeatNumbers = activeSeats.Select(p => p.Seat).OrderBy(s => s).ToList();
                var sbIndex = orderedSeatNumbers.IndexOf(sb.Seat);
                if (sbIndex >= 0)
                {
                    var buttonIndex = (sbIndex - 1 + orderedSeatNumbers.Count) % orderedSeatNumbers.Count;
                    return orderedSeatNumbers[buttonIndex];
                }
            }
        }

        return activeSeats[0].Seat;
    }

    private static Position[] GetLabels(int playerCount) => playerCount switch
    {
        2 => [Position.SB, Position.BB],
        3 => [Position.BTN, Position.SB, Position.BB],
        4 => [Position.BTN, Position.SB, Position.BB, Position.CO],
        5 => [Position.BTN, Position.SB, Position.BB, Position.HJ, Position.CO],
        6 => [Position.BTN, Position.SB, Position.BB, Position.UTG, Position.HJ, Position.CO],
        7 => [Position.BTN, Position.SB, Position.BB, Position.UTG, Position.UTG1, Position.HJ, Position.CO],
        8 => [Position.BTN, Position.SB, Position.BB, Position.UTG, Position.UTG1, Position.UTG2, Position.HJ, Position.CO],
        _ => [Position.BTN, Position.SB, Position.BB, Position.UTG, Position.UTG1, Position.UTG2, Position.LJ, Position.HJ, Position.CO]
    };
}
