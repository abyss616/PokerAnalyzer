using PokerAnalyzer.Domain.Game;

internal static class PositionAssigner
{
    public static PositionResolution Assign(
        IReadOnlyList<HandPlayer> players,
        IReadOnlyList<HandAction> actions,
        int? tableSize)
    {
        var playersForResolver = players
            .Select(player => new PositionResolverPlayer(player.Seat, player.Dealer))
            .ToList();

        var seatsByPlayerName = players
            .Where(player => player.Seat > 0)
            .ToDictionary(player => player.Name, player => player.Seat, StringComparer.OrdinalIgnoreCase);

        var blindSeats = actions
            .Where(action => action.Street == Street.Preflop && (action.Type == ActionType.PostSmallBlind || action.Type == ActionType.PostBigBlind))
            .OrderBy(action => action.ActionIndex)
            .Select(action => seatsByPlayerName.TryGetValue(action.Player, out var seat) ? seat : 0)
            .Where(seat => seat > 0)
            .Take(2)
            .ToList();

        return PositionResolver.Resolve(playersForResolver, blindSeats, tableSize);
    }
}
