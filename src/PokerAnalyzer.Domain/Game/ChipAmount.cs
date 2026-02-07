namespace PokerAnalyzer.Domain.Game;

/// <summary>
/// Integer chip amount. All chip accounting is done in whole chips to avoid floating point or rounding issues.
/// </summary>
public readonly record struct ChipAmount(long Value)
{
    public static ChipAmount Zero => new(0);

    public static ChipAmount operator +(ChipAmount a, ChipAmount b) => new(a.Value + b.Value);
    public static ChipAmount operator -(ChipAmount a, ChipAmount b) => new(a.Value - b.Value);

    public static bool operator >(ChipAmount a, ChipAmount b) => a.Value > b.Value;
    public static bool operator <(ChipAmount a, ChipAmount b) => a.Value < b.Value;
    public static bool operator >=(ChipAmount a, ChipAmount b) => a.Value >= b.Value;
    public static bool operator <=(ChipAmount a, ChipAmount b) => a.Value <= b.Value;

    public override string ToString() => Value.ToString();
}
