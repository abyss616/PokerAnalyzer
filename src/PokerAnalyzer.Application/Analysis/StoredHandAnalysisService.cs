using System;
using System.Collections.Generic;
using System.Text;

namespace PokerAnalyzer.Application.Analysis
{
    public class StoredHandAnalysisService : IStoredHandAnalysisService
    {
        public Task<HandAnalysisResult> AnalyzeHandAsync(Guid handId, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
