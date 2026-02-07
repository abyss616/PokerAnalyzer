public interface IHandHistoryParser
{
    PokerAnalyzer.Domain.HandHistory.Hand ParseHand(string rawXml, Guid handId);
}
