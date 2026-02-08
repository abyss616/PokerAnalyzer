namespace PokerAnalyzer.Infrastructure.Persistence;

internal static class GridColumnSeedData
{
    internal static readonly GridColumnDefinition[] All =
    [
        new GridColumnDefinition { Id = Guid.Parse("9a4c3d1e-8ad8-4d1c-87b8-95d2ea1b4b0a"), StatName = "VPIP", DisplayName = "VPIP", SortOrder = 1 },
        new GridColumnDefinition { Id = Guid.Parse("0c6d3f5f-0b3d-4c54-a233-2ed6fda70d7b"), StatName = "PFR", DisplayName = "PFR", SortOrder = 2 },
        new GridColumnDefinition { Id = Guid.Parse("ae1a3a78-5181-4c51-8f69-7b7d2e1d7f71"), StatName = "3Bet", DisplayName = "3Bet", SortOrder = 3 },
        new GridColumnDefinition { Id = Guid.Parse("b0fd737e-0ab8-4d7f-8c43-faa0388a4d1c"), StatName = "Faced 3Bet", DisplayName = "Faced 3Bet", SortOrder = 4 },
        new GridColumnDefinition { Id = Guid.Parse("9e546b86-9c0d-4e20-8a37-3b4d5c98a2f3"), StatName = "Fold to 3Bet", DisplayName = "Fold to 3Bet", SortOrder = 5 },
        new GridColumnDefinition { Id = Guid.Parse("6a2b7a1e-f9e1-4c01-a91b-2f0d6e5a4d93"), StatName = "Saw Flop", DisplayName = "Saw Flop", SortOrder = 6 },
        new GridColumnDefinition { Id = Guid.Parse("8d8d2a21-2d3c-4f28-9f58-b8d0f2410ce0"), StatName = "Flop WTSD", DisplayName = "Flop WTSD", SortOrder = 7 },
        new GridColumnDefinition { Id = Guid.Parse("5e3b1f85-42e7-4b43-8d3b-9f44de2aab1d"), StatName = "Flop W$SD", DisplayName = "Flop W$SD", SortOrder = 8 },
        new GridColumnDefinition { Id = Guid.Parse("a5e13472-3cc9-46a9-8f2b-8e9b955f0d7a"), StatName = "Flop CBet Opp", DisplayName = "Flop CBet Opp", SortOrder = 9 },
        new GridColumnDefinition { Id = Guid.Parse("3b2c6c8c-3b55-48d8-bc6a-d9a0f1fd6e18"), StatName = "Flop CBet", DisplayName = "Flop CBet", SortOrder = 10 },
        new GridColumnDefinition { Id = Guid.Parse("78f6d56d-1e29-4d1f-88d1-4b2df7b1b58f"), StatName = "Fold to Flop CBet Opp", DisplayName = "Fold to Flop CBet Opp", SortOrder = 11 },
        new GridColumnDefinition { Id = Guid.Parse("5b2d1c6e-7f0e-4bd0-8d2d-1e68c2f3b7b2"), StatName = "Fold to Flop CBet", DisplayName = "Fold to Flop CBet", SortOrder = 12 },
        new GridColumnDefinition { Id = Guid.Parse("6f2c4e6f-3db2-4c98-8d8d-11b5e1a1de16"), StatName = "Donk Bets", DisplayName = "Donk Bets", SortOrder = 13 },
        new GridColumnDefinition { Id = Guid.Parse("1f1d3a4b-9c2e-4b0b-9f1a-7f1d4c7e1b9d"), StatName = "First Fold to CBet", DisplayName = "First Fold to CBet", SortOrder = 14 },
        new GridColumnDefinition { Id = Guid.Parse("b2b94d4e-2a8b-4b7a-a1b4-64c74b7b7e31"), StatName = "Call vs CBet", DisplayName = "Call vs CBet", SortOrder = 15 },
        new GridColumnDefinition { Id = Guid.Parse("d2f865b1-8b3a-46cd-9a20-5c0c3f0f7148"), StatName = "Raise vs CBet", DisplayName = "Raise vs CBet", SortOrder = 16 },
        new GridColumnDefinition { Id = Guid.Parse("a1e42c0c-2f8a-4d3b-b8b1-3c6b1f9e6f0b"), StatName = "Multiway CBet", DisplayName = "Multiway CBet", SortOrder = 17 },
        new GridColumnDefinition { Id = Guid.Parse("f3a9d1c7-92c3-44a4-8b7b-5f1d2f8c4b8e"), StatName = "Probe Bets", DisplayName = "Probe Bets", SortOrder = 18 },
        new GridColumnDefinition { Id = Guid.Parse("c2a34d8c-4c2c-4c1d-8b9d-5c3b9d7f6b2a"), StatName = "Saw Turn", DisplayName = "Saw Turn", SortOrder = 19 },
        new GridColumnDefinition { Id = Guid.Parse("4c8d1b1e-7f3c-46c0-9b8a-2c6f1b7d4a9f"), StatName = "Turn WTSD", DisplayName = "Turn WTSD", SortOrder = 20 },
        new GridColumnDefinition { Id = Guid.Parse("7d9c4b2a-1c5b-4c5e-8e2a-3c7b5d1f2a6e"), StatName = "Turn W$SD", DisplayName = "Turn W$SD", SortOrder = 21 },
        new GridColumnDefinition { Id = Guid.Parse("9d1c2b3a-4e5f-4c6d-8b7c-1a2b3c4d5e6f"), StatName = "Turn CBet", DisplayName = "Turn CBet", SortOrder = 22 },
        new GridColumnDefinition { Id = Guid.Parse("2a3b4c5d-6e7f-4a8b-9c0d-1e2f3a4b5c6d"), StatName = "Turn Check", DisplayName = "Turn Check", SortOrder = 23 },
        new GridColumnDefinition { Id = Guid.Parse("3b4c5d6e-7f8a-4b9c-0d1e-2f3a4b5c6d7e"), StatName = "Turn Fold to Bet", DisplayName = "Turn Fold to Bet", SortOrder = 24 },
        new GridColumnDefinition { Id = Guid.Parse("4c5d6e7f-8a9b-4c0d-1e2f-3a4b5c6d7e8f"), StatName = "Turn Aggression", DisplayName = "Turn Aggression", SortOrder = 25 },
        new GridColumnDefinition { Id = Guid.Parse("5d6e7f8a-9b0c-4d1e-2f3a-4b5c6d7e8f9a"), StatName = "Turn Bet Size % Pot", DisplayName = "Turn Bet Size % Pot", SortOrder = 26 },
        new GridColumnDefinition { Id = Guid.Parse("6e7f8a9b-0c1d-4e2f-3a4b-5c6d7e8f9a0b"), StatName = "Turn Raise vs Bet", DisplayName = "Turn Raise vs Bet", SortOrder = 27 },
        new GridColumnDefinition { Id = Guid.Parse("7f8a9b0c-1d2e-4f3a-4b5c-6d7e8f9a0b1c"), StatName = "Turn WTSD Carryover", DisplayName = "Turn WTSD Carryover", SortOrder = 28 },
        new GridColumnDefinition { Id = Guid.Parse("8a9b0c1d-2e3f-403a-4b5c-7d8e9f0a1b2c"), StatName = "Saw River", DisplayName = "Saw River", SortOrder = 29 },
        new GridColumnDefinition { Id = Guid.Parse("9b0c1d2e-3f4a-4b5c-8d9e-0f1a2b3c4d5e"), StatName = "River WTSD", DisplayName = "River WTSD", SortOrder = 30 },
        new GridColumnDefinition { Id = Guid.Parse("0c1d2e3f-4a5b-4c6d-9e0f-1a2b3c4d5e6f"), StatName = "River W$SD", DisplayName = "River W$SD", SortOrder = 31 },
        new GridColumnDefinition { Id = Guid.Parse("1d2e3f4a-5b6c-4d7e-0f1a-2b3c4d5e6f7a"), StatName = "River Bet Opp", DisplayName = "River Bet Opp", SortOrder = 32 },
        new GridColumnDefinition { Id = Guid.Parse("2e3f4a5b-6c7d-4e8f-1a2b-3c4d5e6f7a8b"), StatName = "River Bets When Checked To", DisplayName = "River Bets When Checked To", SortOrder = 33 },
        new GridColumnDefinition { Id = Guid.Parse("3f4a5b6c-7d8e-4f0a-2b3c-4d5e6f7a8b9c"), StatName = "River Faced Bet", DisplayName = "River Faced Bet", SortOrder = 34 },
        new GridColumnDefinition { Id = Guid.Parse("4a5b6c7d-8e9f-401b-3c4d-5e6f7a8b9c0d"), StatName = "River Calls vs Bet", DisplayName = "River Calls vs Bet", SortOrder = 35 },
        new GridColumnDefinition { Id = Guid.Parse("5b6c7d8e-9f0a-412c-4d5e-6f7a8b9c0d1e"), StatName = "River Fold to Bet", DisplayName = "River Fold to Bet", SortOrder = 36 },
        new GridColumnDefinition { Id = Guid.Parse("6c7d8e9f-0a1b-423d-5e6f-7a8b9c0d1e2f"), StatName = "River Raise vs Bet", DisplayName = "River Raise vs Bet", SortOrder = 37 },
        new GridColumnDefinition { Id = Guid.Parse("7d8e9f0a-1b2c-434e-6f7a-8b9c0d1e2f3a"), StatName = "River Aggression", DisplayName = "River Aggression", SortOrder = 38 },
        new GridColumnDefinition { Id = Guid.Parse("8e9f0a1b-2c3d-445f-7a8b-9c0d1e2f3a4b"), StatName = "River Bet Size % Pot", DisplayName = "River Bet Size % Pot", SortOrder = 39 }
    ];
}
