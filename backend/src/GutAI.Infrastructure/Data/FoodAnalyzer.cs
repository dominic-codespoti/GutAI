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

        // Cooking forms
        AddSynonym(builder, "toast", "bread", "toasted");
        AddSynonym(builder, "steak", "beef", "loin");
        AddSynonym(builder, "oatmeal", "oats", "cereal");
        AddSynonym(builder, "porridge", "oats", "cereal");
        AddSynonym(builder, "fries", "potatoes", "french", "fried");
        AddSynonym(builder, "chips", "potato", "chips");
        AddSynonym(builder, "soda", "carbonated", "beverage");
        AddSynonym(builder, "pop", "carbonated", "beverage");

        // Regional AU/UK → US equivalents
        AddSynonym(builder, "capsicum", "peppers", "sweet");
        AddSynonym(builder, "prawns", "shrimp");
        AddSynonym(builder, "prawn", "shrimp");
        AddSynonym(builder, "mince", "ground", "beef");
        AddSynonym(builder, "rocket", "arugula");
        AddSynonym(builder, "coriander", "cilantro");
        AddSynonym(builder, "aubergine", "eggplant");
        AddSynonym(builder, "courgette", "zucchini");
        AddSynonym(builder, "beetroot", "beets");
        AddSynonym(builder, "sultana", "raisins", "golden");
        AddSynonym(builder, "sultanas", "raisins", "golden");
        AddSynonym(builder, "crisps", "potato", "chips");
        AddSynonym(builder, "biscuit", "cookie");
        AddSynonym(builder, "biscuits", "cookies");
        AddSynonym(builder, "lolly", "candy");
        AddSynonym(builder, "lollies", "candy");
        AddSynonym(builder, "muesli", "granola", "cereal");
        AddSynonym(builder, "skim", "nonfat");
        AddSynonym(builder, "skimmed", "nonfat");
        AddSynonym(builder, "wholemeal", "whole", "wheat");
        AddSynonym(builder, "minced", "ground");
        AddSynonym(builder, "tinned", "canned");

        // Common colloquial terms
        AddSynonym(builder, "hotdog", "frankfurter", "sausage");
        AddSynonym(builder, "jam", "preserves", "jelly");
        AddSynonym(builder, "ketchup", "catsup", "tomato", "sauce");
        AddSynonym(builder, "mayo", "mayonnaise");
        AddSynonym(builder, "vegemite", "yeast", "extract", "spread");
        AddSynonym(builder, "marmite", "yeast", "extract", "spread");
        AddSynonym(builder, "yoghurt", "yogurt");

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
