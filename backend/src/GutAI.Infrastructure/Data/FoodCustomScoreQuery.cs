using Lucene.Net.Index;
using Lucene.Net.Queries;
using Lucene.Net.Queries.Function;
using Lucene.Net.Search;

namespace GutAI.Infrastructure.Data;

/// <summary>
/// CustomScoreQuery that blends Lucene text relevance with a pre-computed static quality signal.
/// </summary>
internal sealed class FoodCustomScoreQuery : CustomScoreQuery
{
    public FoodCustomScoreQuery(Query subQuery, FunctionQuery qualityQuery)
        : base(subQuery, qualityQuery) { }

    protected override CustomScoreProvider GetCustomScoreProvider(AtomicReaderContext context)
        => new FoodScoreProvider(context);
}

internal sealed class FoodScoreProvider : CustomScoreProvider
{
    public FoodScoreProvider(AtomicReaderContext context) : base(context) { }

    public override float CustomScore(int doc, float subQueryScore, float valSrcScore)
    {
        // subQueryScore = Lucene BooleanQuery relevance
        // valSrcScore   = pre-computed static quality (from SingleDocValuesField)
        // Quality acts as a moderate booster — reduced from 15f to 8f to prevent
        // metadata-rich OpenFoodFacts products from overwhelming USDA whole foods
        return subQueryScore + valSrcScore * 8f;
    }
}
