using PokerAnalyzer.Api.Logging;
using PokerAnalyzer.Application.Analysis;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<UiLogStore>();
builder.Services.AddSingleton<UiLogLoggerProvider>();
builder.Logging.Services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<UiLogLoggerProvider>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IHandHistoryIngestService, HandHistoryIngestService>();

builder.Services.AddPokerAnalyzer();
builder.Services.AddControllers();
var app = builder.Build();

app.MapControllers();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PokerAnalyzer.Infrastructure.Persistence.PokerDbContext>();
    if (app.Environment.IsDevelopment())
    {
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }
}
app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/analyze", async (AnalyzeHandRequest request, HandAnalyzer analyzer, CancellationToken ct) =>
{
    var hand = request.ToDomain();
    var result = await analyzer.AnalyzeAsync(hand, ct);
    return Results.Ok(result);
})
.WithName("AnalyzeHand")
.Produces<HandAnalysisResult>(StatusCodes.Status200OK);

app.Run();

public sealed record AnalyzeHandRequest(
    Guid HandId,
    long SmallBlind,
    long BigBlind,
    Guid HeroId,
    string? HeroHoleCards, // e.g. "AsKh"
    IReadOnlyList<SeatDto> Seats,
    string Board, // e.g. "AhKd2c" or ""
    IReadOnlyList<ActionDto> Actions
)
{
    public PokerAnalyzer.Domain.HandHistory.Hand ToDomain()
    {
        var seats = Seats.Select(s => new PlayerSeat(
            new PlayerId(s.Id),
            s.Name,
            s.SeatNumber,
            s.Position,
            new ChipAmount(s.StartingStack)
        )).ToList();

        var heroId = new PlayerId(HeroId);

        HoleCards? hc = null;
        if (!string.IsNullOrWhiteSpace(HeroHoleCards))
            hc = HoleCards.Parse(HeroHoleCards!);

        var board = new Board();

        var actions = Actions.Select(a => new BettingAction(
            a.Street,
            new PlayerId(a.ActorId),
            a.Type,
            new ChipAmount(a.Amount)
        )).ToList();

        return new PokerAnalyzer.Domain.HandHistory.Hand(
            HandId,
            new ChipAmount(SmallBlind),
            new ChipAmount(BigBlind),
            seats,
            heroId,
            hc,
            board,
            actions
        );
    }
}

public sealed record SeatDto(
    Guid Id,
    string Name,
    int SeatNumber,
    Position Position,
    long StartingStack
);

public sealed record ActionDto(
    Street Street,
    Guid ActorId,
    ActionType Type,
    long Amount
);
