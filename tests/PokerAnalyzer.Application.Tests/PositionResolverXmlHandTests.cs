using PokerAnalyzer.Domain.Game;
using System.Xml.Linq;

namespace PokerAnalyzer.Application.Tests;

public sealed class PositionResolverXmlHandTests
{
    [Fact]
    public void Resolves_Blinds_And_Hero_Position_From_Xml_Game_11996443765()
    {
        const string xml = """
            <session>
              <game gamecode="11996443765">
                <general>
                  <tablesize>6</tablesize>
                  <players>
                    <player name="xlnt" seat="1" dealer="1" chips="€2.00" />
                    <player name="tigerfluffy676" seat="3" chips="€2.00" />
                    <player name="RATEG" seat="5" chips="€2.00" />
                  </players>
                </general>
                <round no="0">
                  <cards type="Pocket" player="tigerfluffy676">HK DK</cards>
                  <action no="1" player="tigerfluffy676" type="1" sum="€0.01" />
                  <action no="2" player="RATEG" type="2" sum="€0.02" />
                </round>
              </game>
            </session>
            """;

        var doc = XDocument.Parse(xml);
        var game = doc.Root!.Element("game")!;
        var players = game.Element("general")!.Element("players")!.Elements("player")
            .Select(player => new
            {
                Name = player.Attribute("name")!.Value,
                Seat = int.Parse(player.Attribute("seat")!.Value),
                Dealer = player.Attribute("dealer")?.Value == "1"
            })
            .ToList();

        var seatsByName = players.ToDictionary(player => player.Name, player => player.Seat, StringComparer.OrdinalIgnoreCase);
        var round0BlindSeats = game.Elements("round")
            .First(round => round.Attribute("no")?.Value == "0")
            .Elements("action")
            .Take(2)
            .Select(action => seatsByName[action.Attribute("player")!.Value])
            .ToList();

        var resolution = PositionResolver.Resolve(
            players.Select(player => new PositionResolverPlayer(player.Seat, player.Dealer)).ToList(),
            round0BlindSeats,
            tableSize: 6);

        Assert.Equal(seatsByName["xlnt"], resolution.DealerSeat);
        Assert.Equal(seatsByName["tigerfluffy676"], resolution.SbSeat);
        Assert.Equal(seatsByName["RATEG"], resolution.BbSeat);
        Assert.Equal(Position.SB, resolution.PositionsBySeat[seatsByName["tigerfluffy676"]]);
    }
}
