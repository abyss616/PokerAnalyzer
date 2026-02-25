using System.Collections.Generic;
using System.Linq;

namespace PokerAnalyzer.Domain.Game;

public readonly record struct PositionResolverPlayer(int Seat, bool IsDealer);

public readonly record struct PositionResolution(
    IReadOnlyDictionary<int, Position> PositionsBySeat,
    int? DealerSeat,
    int? SbSeat,
    int? BbSeat);

public static class PositionResolver
{
    public static PositionResolution Resolve(
        IReadOnlyList<PositionResolverPlayer> players,
        IReadOnlyList<int> round0BlindSeats,
        int? tableSize)
    {
        var activeSeats = players
            .Select(p => p.Seat)
            .Where(seat => seat > 0)
            .Distinct()
            .OrderBy(seat => seat)
            .ToList();

        if (activeSeats.Count == 0)
            return new PositionResolution(new Dictionary<int, Position>(), null, null, null);

        var activeSet = activeSeats.ToHashSet();
        var resolvedTableSize = Math.Max(tableSize ?? activeSeats.Max(), activeSeats.Max());

        var dealerSeat = players
            .FirstOrDefault(p => p.IsDealer && activeSet.Contains(p.Seat))
            .Seat;
        if (!activeSet.Contains(dealerSeat))
            dealerSeat = 0;

        var blindSeats = round0BlindSeats
            .Where(activeSet.Contains)
            .Distinct()
            .Take(2)
            .ToList();

        int? sbSeat = blindSeats.Count >= 1 ? blindSeats[0] : null;
        int? bbSeat = blindSeats.Count >= 2 ? blindSeats[1] : null;

        if (activeSeats.Count == 2)
        {
            dealerSeat = sbSeat ?? (dealerSeat > 0 ? dealerSeat : activeSeats[0]);
            sbSeat ??= dealerSeat;
            bbSeat ??= activeSeats.First(seat => seat != sbSeat.Value);

            var huPositions = new Dictionary<int, Position>
            {
                [sbSeat.Value] = Position.SB,
                [bbSeat.Value] = Position.BB
            };

            return new PositionResolution(huPositions, dealerSeat, sbSeat, bbSeat);
        }

        if (dealerSeat == 0)
            dealerSeat = sbSeat.HasValue ? PreviousActiveSeat(sbSeat.Value, activeSet, resolvedTableSize) : activeSeats[0];

        if (!sbSeat.HasValue)
            sbSeat = NextActiveSeat(dealerSeat.Value, activeSet, resolvedTableSize);

        if (!bbSeat.HasValue)
            bbSeat = NextActiveSeat(sbSeat.Value, activeSet, resolvedTableSize);

        var positions = activeSeats.ToDictionary(seat => seat, _ => Position.Unknown);
        positions[dealerSeat.Value] = Position.BTN;
        positions[sbSeat.Value] = Position.SB;
        positions[bbSeat.Value] = Position.BB;

        var remainingSeats = new List<int>();
        var cursor = bbSeat.Value;
        while (true)
        {
            cursor = NextActiveSeat(cursor, activeSet, resolvedTableSize);
            if (cursor == dealerSeat.Value)
                break;

            if (cursor != sbSeat.Value && cursor != bbSeat.Value)
                remainingSeats.Add(cursor);
        }

        var remainingLabels = GetRemainingLabels(remainingSeats.Count);
        for (var i = 0; i < remainingSeats.Count && i < remainingLabels.Length; i++)
            positions[remainingSeats[i]] = remainingLabels[i];

        return new PositionResolution(positions, dealerSeat, sbSeat, bbSeat);
    }

    private static int NextActiveSeat(int seat, HashSet<int> activeSeats, int tableSize)
    {
        for (var offset = 1; offset <= tableSize; offset++)
        {
            var candidate = ((seat - 1 + offset) % tableSize) + 1;
            if (activeSeats.Contains(candidate))
                return candidate;
        }

        return seat;
    }

    private static int PreviousActiveSeat(int seat, HashSet<int> activeSeats, int tableSize)
    {
        for (var offset = 1; offset <= tableSize; offset++)
        {
            var candidate = ((seat - 1 - offset + tableSize * 10) % tableSize) + 1;
            if (activeSeats.Contains(candidate))
                return candidate;
        }

        return seat;
    }

    private static Position[] GetRemainingLabels(int count) => count switch
    {
        <= 0 => [],
        1 => [Position.UTG],
        2 => [Position.UTG, Position.CO],
        3 => [Position.UTG, Position.HJ, Position.CO],
        4 => [Position.UTG, Position.UTG1, Position.HJ, Position.CO],
        5 => [Position.UTG, Position.UTG1, Position.UTG2, Position.HJ, Position.CO],
        _ => [Position.UTG, Position.UTG1, Position.UTG2, Position.LJ, Position.HJ, Position.CO]
    };
}
