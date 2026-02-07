using PokerAnalyzer.Domain.HandHistory;

namespace PokerAnalyzer.Infrastructure.HandHistories
{
    public class XmlHandHistoryParser : IHandHistoryParser
    {
        public Domain.HandHistory.Hand ParseHand(string rawXml, Guid handId)
        {
            throw new NotImplementedException();
        }
    }
}
