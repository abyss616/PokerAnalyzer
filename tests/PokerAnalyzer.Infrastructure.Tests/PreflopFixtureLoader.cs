using System.Text.Json;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed record PreflopFixtureCase(string Name, PreflopPlayersFixture Players, PreflopActionsFixture Actions, PreflopExpectedFixture Expected);

public static class PreflopFixtureLoader
{
    public static IReadOnlyList<PreflopFixtureCase> LoadAll(string root)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var fixtures = new List<PreflopFixtureCase>();

        foreach (var dir in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.Ordinal))
        {
            var players = JsonSerializer.Deserialize<PreflopPlayersFixture>(File.ReadAllText(Path.Combine(dir, "players.json")), options)!;
            var actions = JsonSerializer.Deserialize<PreflopActionsFixture>(File.ReadAllText(Path.Combine(dir, "actions.json")), options)!;
            var expected = JsonSerializer.Deserialize<PreflopExpectedFixture>(File.ReadAllText(Path.Combine(dir, "expected.json")), options)!;
            fixtures.Add(new PreflopFixtureCase(Path.GetFileName(dir), players, actions, expected));
        }

        return fixtures;
    }
}
