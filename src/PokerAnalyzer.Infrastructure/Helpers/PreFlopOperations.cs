using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Helpers
{
    public static class PreFlopOperations
    {
        public static bool IsPreflopAggressive(ActionType t) =>
t is ActionType.Raise or ActionType.AllIn;
    }
}
