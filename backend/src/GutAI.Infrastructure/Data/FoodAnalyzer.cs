using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.Synonym;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Util;

namespace GutAI.Infrastructure.Data;

/// <summary>
/// Lucene analyzer pipeline: StandardTokenizer → LowerCase → Stop → Synonym → PorterStem.
/// </summary>
internal sealed class FoodAnalyzer : Analyzer
{
    private static readonly CharArraySet StopWords;
    private static readonly SynonymMap Synonyms;

    static FoodAnalyzer()
    {
        StopWords = BuildStopWords();
        Synonyms = BuildSynonymMap();
    }

    private static CharArraySet BuildStopWords()
    {
        var words = new CharArraySet(LuceneVersion.LUCENE_48, 40, ignoreCase: true);
        foreach (var w in new[]
        {
            "with", "and", "in", "of", "style", "flavored", "flavoured",
            "ns", "as", "to", "the", "for", "a", "an",
            "nfs", "not", "further", "specified", "type", "all", "purpose",
            "usda", "commodity", "purchased", "commercially", "prepared",
            "ready", "eat"
        })
        {
            words.Add(w);
        }
        return words.AsReadOnly();
    }

    private static SynonymMap BuildSynonymMap()
    {
        var builder = new SynonymMap.Builder(dedup: true);

        AddSynonym(builder, "toast", "bread", "toasted");
        AddSynonym(builder, "steak", "beef", "loin");
        AddSynonym(builder, "oatmeal", "oats", "cereal");
        AddSynonym(builder, "fries", "potatoes", "french", "fried");
        AddSynonym(builder, "chips", "potato", "chips");
        AddSynonym(builder, "soda", "carbonated", "beverage");
        AddSynonym(builder, "pop", "carbonated", "beverage");

        return builder.Build();
    }

    private static void AddSynonym(SynonymMap.Builder builder, string input, params string[] outputs)
    {
        var inputCs = new CharsRef(input);
        foreach (var output in outputs)
            builder.Add(inputCs, new CharsRef(output), true);
    }

    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var tokenizer = new StandardTokenizer(LuceneVersion.LUCENE_48, reader);
        TokenStream stream = new LowerCaseFilter(LuceneVersion.LUCENE_48, tokenizer);
        stream = new StopFilter(LuceneVersion.LUCENE_48, stream, StopWords);
        stream = new SynonymFilter(stream, Synonyms, ignoreCase: true);
        stream = new PorterStemFilter(stream);
        return new TokenStreamComponents(tokenizer, stream);
    }
}
