using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using GutAI.Domain.Enums;

namespace GutAI.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(ITableStore store)
    {
        await SeedSymptomTypesAsync(store);
        await SeedFoodAdditivesAsync(store);
    }

    private static async Task SeedSymptomTypesAsync(ITableStore store)
    {
        var existing = await store.GetAllSymptomTypesAsync();
        if (existing.Count > 0) return;

        var types = new List<SymptomType>
        {
            new() { Id = 1, Name = "Bloating", Category = "Digestive", Icon = "🫧" },
            new() { Id = 2, Name = "Gas", Category = "Digestive", Icon = "💨" },
            new() { Id = 3, Name = "Cramping", Category = "Digestive", Icon = "😖" },
            new() { Id = 4, Name = "Diarrhea", Category = "Digestive", Icon = "🚽" },
            new() { Id = 5, Name = "Constipation", Category = "Digestive", Icon = "🧱" },
            new() { Id = 6, Name = "Heartburn / Acid Reflux", Category = "Digestive", Icon = "🔥" },
            new() { Id = 7, Name = "Nausea", Category = "Digestive", Icon = "🤢" },
            new() { Id = 8, Name = "Stomach Pain", Category = "Digestive", Icon = "😫" },
            new() { Id = 9, Name = "Indigestion", Category = "Digestive", Icon = "😣" },
            new() { Id = 10, Name = "Brain Fog", Category = "Neurological", Icon = "🧠" },
            new() { Id = 11, Name = "Headache", Category = "Neurological", Icon = "🤕" },
            new() { Id = 12, Name = "Migraine", Category = "Neurological", Icon = "⚡" },
            new() { Id = 13, Name = "Dizziness", Category = "Neurological", Icon = "😵" },
            new() { Id = 14, Name = "Skin Rash", Category = "Skin", Icon = "🌡️" },
            new() { Id = 15, Name = "Hives", Category = "Skin", Icon = "🔴" },
            new() { Id = 16, Name = "Acne Flare-up", Category = "Skin", Icon = "😤" },
            new() { Id = 17, Name = "Eczema Flare-up", Category = "Skin", Icon = "🩹" },
            new() { Id = 18, Name = "Fatigue", Category = "Energy", Icon = "😴" },
            new() { Id = 19, Name = "Energy Crash", Category = "Energy", Icon = "📉" },
            new() { Id = 20, Name = "Insomnia", Category = "Energy", Icon = "🌙" },
            new() { Id = 21, Name = "Joint Pain", Category = "Other", Icon = "🦴" },
            new() { Id = 22, Name = "Mood Changes", Category = "Other", Icon = "😶" },
            new() { Id = 23, Name = "Anxiety", Category = "Other", Icon = "😰" },
            new() { Id = 24, Name = "Inflammation", Category = "Other", Icon = "🔥" },
        };

        foreach (var t in types)
            await store.UpsertSymptomTypeAsync(t);
    }

    private static async Task SeedFoodAdditivesAsync(ITableStore store)
    {
        var existing = await store.GetAllFoodAdditivesAsync();
        var existingEnumbers = existing.Select(a => a.ENumber).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var additives = new List<FoodAdditive>
        {
            new()
            {
                Id = 1, ENumber = "E129", Name = "Red 40 (Allura Red AC)",
                AlternateNames = ["FD&C Red No. 40", "CI 16035", "Allura Red"],
                Category = "Color", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Linked to hyperactivity in children. EU requires warning label. Contains benzidine, a known carcinogen.",
                Description = "Most widely used food dye in the US. Found in candy, beverages, cereals, and snack foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 2, ENumber = "E110", Name = "Yellow 6 (Sunset Yellow)",
                AlternateNames = ["FD&C Yellow No. 6", "CI 15985", "Sunset Yellow FCF"],
                Category = "Color", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Linked to hyperactivity in children, allergic reactions, and may contain carcinogenic contaminants.",
                Description = "Used in candy, baked goods, cereals, and beverages.",
                BannedInCountries = ["Norway", "Finland"]
            },
            new()
            {
                Id = 3, ENumber = "E102", Name = "Yellow 5 (Tartrazine)",
                AlternateNames = ["FD&C Yellow No. 5", "CI 19140", "Tartrazine"],
                Category = "Color", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Linked to hyperactivity, hives, asthma, and allergic reactions, especially in aspirin-sensitive individuals.",
                Description = "Second most common food dye. Found in candy, soft drinks, chips, and pickles.",
                BannedInCountries = ["Norway", "Austria"]
            },
            new()
            {
                Id = 4, ENumber = "E133", Name = "Blue 1 (Brilliant Blue)",
                AlternateNames = ["FD&C Blue No. 1", "CI 42090"],
                Category = "Color", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Some evidence of chromosomal damage. Not adequately tested.",
                Description = "Used in beverages, candy, baked goods, and ice cream.",
                BannedInCountries = []
            },
            new()
            {
                Id = 5, ENumber = "E127", Name = "Red 3 (Erythrosine)",
                AlternateNames = ["FD&C Red No. 3", "CI 45430"],
                Category = "Color", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Restricted,
                HealthConcerns = "Recognized as a thyroid carcinogen by the FDA. Ban on cosmetics/external drugs but still allowed in food.",
                Description = "Found in candy, popsicles, cake decorating gels. FDA acknowledged carcinogenicity in 1990.",
                BannedInCountries = ["EU (cosmetics)"]
            },
            new()
            {
                Id = 6, ENumber = "E143", Name = "Green 3 (Fast Green)",
                AlternateNames = ["FD&C Green No. 3", "CI 42053"],
                Category = "Color", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.NotAuthorized,
                HealthConcerns = "Poorly tested. Some evidence of bladder tumors in animal studies.",
                Description = "Used in candy, beverages, and desserts. Banned in EU.",
                BannedInCountries = ["EU"]
            },
            new()
            {
                Id = 7, ENumber = "E320", Name = "BHA (Butylated Hydroxyanisole)",
                AlternateNames = ["Butylated Hydroxyanisole"],
                Category = "Preservative", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Reasonably anticipated to be a human carcinogen (National Toxicology Program). Causes cancer in rat forestomachs.",
                Description = "Antioxidant preservative used in fats, oils, cereals, chewing gum, and snack foods.",
                BannedInCountries = ["Japan (some uses)"]
            },
            new()
            {
                Id = 8, ENumber = "E321", Name = "BHT (Butylated Hydroxytoluene)",
                AlternateNames = ["Butylated Hydroxytoluene"],
                Category = "Preservative", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Some animal studies show increased cancer risk. Others show protective effects. Uncertain.",
                Description = "Antioxidant preservative similar to BHA. Used in cereals, fats, and oils.",
                BannedInCountries = []
            },
            new()
            {
                Id = 9, ENumber = "E319", Name = "TBHQ (tert-Butylhydroquinone)",
                AlternateNames = ["tert-Butylhydroquinone", "Tertiary Butylhydroquinone"],
                Category = "Preservative", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "High doses caused stomach tumors in rats. May affect immune system function.",
                Description = "Antioxidant preservative in oils, crackers, microwave popcorn, and fast food.",
                BannedInCountries = []
            },
            new()
            {
                Id = 10, ENumber = "E250", Name = "Sodium Nitrite",
                AlternateNames = ["Sodium Nitrite"],
                Category = "Preservative", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Forms nitrosamines (potent carcinogens) in the body. Linked to colorectal cancer. WHO classifies processed meat as Group 1 carcinogen partly due to nitrites.",
                Description = "Preservative and color fixative in processed meats: bacon, hot dogs, deli meats, sausages.",
                BannedInCountries = []
            },
            new()
            {
                Id = 11, ENumber = "E251", Name = "Sodium Nitrate",
                AlternateNames = ["Sodium Nitrate", "Chile Saltpeter"],
                Category = "Preservative", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Converts to sodium nitrite in the body, forming carcinogenic nitrosamines.",
                Description = "Used in cured meats and some cheeses.",
                BannedInCountries = []
            },
            new()
            {
                Id = 12, ENumber = "E924", Name = "Potassium Bromate",
                AlternateNames = ["Potassium Bromate"],
                Category = "Flour Treatment", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                HealthConcerns = "Causes cancer in animals. IARC classifies as possibly carcinogenic to humans (Group 2B).",
                Description = "Flour improver that strengthens dough. Banned in EU, UK, Canada, Brazil. Still used in some US breads.",
                BannedInCountries = ["EU", "UK", "Canada", "Brazil", "China", "India"]
            },
            new()
            {
                Id = 13, ENumber = "E407", Name = "Carrageenan",
                AlternateNames = ["Carrageenan", "Irish Moss Extract"],
                Category = "Emulsifier", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Linked to inflammation, gut irritation, and gastrointestinal problems. Degraded carrageenan is a known carcinogen.",
                Description = "Thickener/stabilizer derived from seaweed. Found in dairy alternatives, ice cream, deli meats.",
                BannedInCountries = []
            },
            new()
            {
                Id = 14, ENumber = "E171", Name = "Titanium Dioxide",
                AlternateNames = ["Titanium Dioxide", "CI 77891", "TiO2"],
                Category = "Color", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                HealthConcerns = "Possible genotoxicity — may damage DNA. EFSA concluded it can no longer be considered safe as a food additive.",
                Description = "White pigment used in candy, frosting, chewing gum, coffee creamer. Banned in EU since August 2022.",
                BannedInCountries = ["EU"]
            },
            new()
            {
                Id = 15, ENumber = "E951", Name = "Aspartame",
                AlternateNames = ["Aspartame", "NutraSweet", "Equal"],
                Category = "Sweetener", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                EfsaAdiMgPerKgBw = 40m,
                HealthConcerns = "IARC classified as 'possibly carcinogenic to humans' (Group 2B) in 2023. Some studies link to headaches, seizures in sensitive individuals.",
                Description = "Artificial sweetener 200x sweeter than sugar. Found in diet sodas, sugar-free gum, tabletop sweeteners.",
                BannedInCountries = []
            },
            new()
            {
                Id = 16, ENumber = "E955", Name = "Sucralose",
                AlternateNames = ["Sucralose", "Splenda"],
                Category = "Sweetener", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                EfsaAdiMgPerKgBw = 15m,
                HealthConcerns = "Some studies suggest gut microbiome disruption and possible DNA damage at high doses. Ongoing research.",
                Description = "Artificial sweetener 600x sweeter than sugar. Found in diet drinks, baked goods, condiments.",
                BannedInCountries = []
            },
            new()
            {
                Id = 17, ENumber = "E954", Name = "Saccharin",
                AlternateNames = ["Saccharin", "Sweet'N Low"],
                Category = "Sweetener", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                EfsaAdiMgPerKgBw = 5m,
                HealthConcerns = "Caused bladder cancer in male rats. Delisted from carcinogen list in 2000 but concerns remain.",
                Description = "Oldest artificial sweetener. Found in diet drinks, tabletop sweeteners.",
                BannedInCountries = []
            },
            new()
            {
                Id = 18, ENumber = "E950", Name = "Acesulfame Potassium (Ace-K)",
                AlternateNames = ["Acesulfame K", "Acesulfame Potassium", "Ace-K"],
                Category = "Sweetener", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                EfsaAdiMgPerKgBw = 9m,
                HealthConcerns = "Poorly tested. Contains methylene chloride, a potential carcinogen. May disrupt metabolic processes.",
                Description = "Often paired with aspartame or sucralose in diet beverages. 200x sweeter than sugar.",
                BannedInCountries = []
            },
            new()
            {
                Id = 19, Name = "Brominated Vegetable Oil (BVO)",
                AlternateNames = ["BVO", "Brominated Vegetable Oil"],
                Category = "Emulsifier", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                HealthConcerns = "Accumulates in body fat and organs. Linked to neurological issues and thyroid problems. FDA revoked authorization in 2024.",
                Description = "Was used to keep citrus flavoring from separating in sodas. Banned by FDA effective August 2024.",
                BannedInCountries = ["US", "EU", "Japan", "India"]
            },
            new()
            {
                Id = 20, ENumber = "E216", Name = "Propylparaben",
                AlternateNames = ["Propylparaben", "Propyl 4-hydroxybenzoate"],
                Category = "Preservative", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                HealthConcerns = "Endocrine disruptor. Mimics estrogen. Linked to decreased sperm counts and reduced testosterone.",
                Description = "Preservative used in baked goods, tortillas, and food coatings. Banned as food additive in EU.",
                BannedInCountries = ["EU"]
            },
            new()
            {
                Id = 21, ENumber = "E150d", Name = "Caramel Color (Class IV / 4-MEI)",
                AlternateNames = ["Caramel Color", "Sulfite Ammonia Caramel", "4-MEI"],
                Category = "Color", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Contains 4-methylimidazole (4-MEI), classified as possibly carcinogenic. California requires cancer warning label above 29 mcg/day.",
                Description = "Most widely consumed food coloring by weight. Found in cola, soy sauce, beer, bread.",
                BannedInCountries = []
            },
            new()
            {
                Id = 22, Name = "Mycoprotein",
                AlternateNames = ["Mycoprotein", "Quorn"],
                Category = "Protein Source", CspiRating = CspiRating.CertainPeopleShouldAvoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Approved,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Can cause severe allergic reactions including anaphylaxis in sensitive individuals.",
                Description = "Fungal protein used in Quorn brand meat substitutes.",
                BannedInCountries = []
            },
            new()
            {
                Id = 23, ENumber = "E433", Name = "Polysorbate 80",
                AlternateNames = ["Polysorbate 80", "Tween 80"],
                Category = "Emulsifier", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                EfsaAdiMgPerKgBw = 25m,
                HealthConcerns = "Animal studies suggest it may promote intestinal inflammation and metabolic syndrome by altering gut bacteria.",
                Description = "Emulsifier found in ice cream, sauces, baked goods, and cosmetics.",
                BannedInCountries = []
            },
            new()
            {
                Id = 24, ENumber = "E466", Name = "Carboxymethylcellulose (CMC)",
                AlternateNames = ["CMC", "Cellulose Gum", "Carboxymethylcellulose"],
                Category = "Emulsifier", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Animal studies link to gut inflammation, altered microbiome, and metabolic syndrome.",
                Description = "Thickener/stabilizer in ice cream, dressings, gluten-free baked goods, toothpaste.",
                BannedInCountries = []
            },
            new()
            {
                Id = 25, ENumber = "E211", Name = "Sodium Benzoate",
                AlternateNames = ["Sodium Benzoate"],
                Category = "Preservative", CspiRating = CspiRating.CertainPeopleShouldAvoid,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                HealthConcerns = "Can form benzene (a carcinogen) when combined with ascorbic acid (vitamin C). Linked to hyperactivity in children.",
                Description = "Preservative in acidic foods: soft drinks, pickles, salad dressings, fruit juices.",
                BannedInCountries = []
            },
            new()
            {
                Id = 26, ENumber = "E100", Name = "Curcumin",
                AlternateNames = ["Turmeric", "CI 75300", "Natural Yellow 3"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May cause mild GI upset in very high supplemental doses. No genotoxicity concerns at food-use levels.",
                Description = "Natural yellow-orange pigment from turmeric root. Used in curry powders, mustard, cheese products, yogurts, beverages, and confectionery.",
                BannedInCountries = []
            },
            new()
            {
                Id = 27, ENumber = "E101", Name = "Riboflavin (Vitamin B2)",
                AlternateNames = ["Vitamin B2", "Lactoflavin", "E101i", "E101a (Riboflavin-5'-Phosphate)", "E106 (Riboflavin-5-Sodium Phosphate)"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns at food levels. Excess is excreted in urine (bright yellow). Very rare photosensitivity in extremely high supplemental doses.",
                Description = "Essential water-soluble B vitamin used as a yellow-orange food colorant. Found naturally in eggs, dairy, and green vegetables. Used in breakfast cereals, sauces, processed cheese, beverages, and infant formula.",
                BannedInCountries = []
            },
            new()
            {
                Id = 28, ENumber = "E103", Name = "Alkannin",
                AlternateNames = ["Alkanet", "Anchusa", "CI 75520", "Natural Red 20"],
                Category = "Colorant", CspiRating = CspiRating.Unknown,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Limited modern safety data. Historical use shows no major concerns. Not currently used in commercial food production.",
                Description = "Natural naphthoquinone pigment extracted from the roots of Alkanna tinctoria. Produces pink to red-brown hues. Historically used in cosmetics and traditional foods; no longer actively used as a food additive.",
                BannedInCountries = ["United States", "European Union"]
            },
            new()
            {
                Id = 29, ENumber = "E105", Name = "Fast Yellow AB",
                AlternateNames = ["Acid Yellow 9", "CI 13015"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Historical toxicity concerns including potential carcinogenicity. Withdrawn from the market globally.",
                Description = "Synthetic yellow azo dye that was historically used in food but was banned due to safety concerns. No longer permitted or used in any major market.",
                BannedInCountries = ["United States", "European Union", "United Kingdom", "Australia", "Canada", "Japan"]
            },
            new()
            {
                Id = 30, ENumber = "E107", Name = "Yellow 2G",
                AlternateNames = ["Acid Yellow 17", "CI 18965"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Limited toxicological data. As an azo dye, potential for azo-reduction by gut bacteria to aromatic amines. Not actively used in commercial food production.",
                Description = "Synthetic yellow monoazo dye. Listed in historical Codex Alimentarius but never widely adopted as a food additive. Not currently approved for food use in major regulatory jurisdictions.",
                BannedInCountries = ["United States", "European Union", "United Kingdom", "Australia", "Canada"]
            },
            new()
            {
                Id = 31, ENumber = "E1103", Name = "Invertase",
                AlternateNames = ["Beta-fructofuranosidase", "Invertin"],
                Category = "Stabilizer", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Enzyme is denatured and digested like any protein. No specific health concerns.",
                Description = "Enzyme that hydrolyzes sucrose into glucose and fructose. Used in confectionery (chocolate-covered cherries, fondant) to maintain soft texture.",
                BannedInCountries = []
            },
            new()
            {
                Id = 32, ENumber = "E1105", Name = "Lysozyme",
                AlternateNames = ["Lysozyme hydrochloride", "Muramidase"],
                Category = "Preservative", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May cause rare allergic reactions (particularly in egg-allergic individuals). May have beneficial antimicrobial effects in gut.",
                Description = "Antimicrobial enzyme used as a preservative in cheese, wine, and other products. Breaks down bacterial cell walls.",
                BannedInCountries = []
            },
            new()
            {
                Id = 33, ENumber = "E111", Name = "Orange GGN",
                AlternateNames = ["Alpha-naphthol orange", "Acid Orange 1", "CI 13080"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Historical toxicity concerns led to withdrawal from food use. Not permitted in any major food market.",
                Description = "Synthetic orange azo dye historically used in foods. Withdrawn from commercial use following safety evaluations that identified toxicity concerns. No longer found in food products.",
                BannedInCountries = ["United States", "European Union", "United Kingdom", "Australia", "Canada", "Japan"]
            },
            new()
            {
                Id = 34, ENumber = "E120", Name = "Carmine (Cochineal)",
                AlternateNames = ["Cochineal", "Carminic Acid", "Natural Red 4", "CI 75470"],
                Category = "Colorant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Allergic reactions (urticaria, angioedema, anaphylaxis) in rare sensitive individuals due to residual insect proteins. Must be labeled as 'carmine' or 'cochineal extract' in the US. Unsuitable for vegans.",
                Description = "Natural crimson pigment extracted from female Dactylopius coccus insects. Used in yogurts, juices, candies, ice cream, processed meats, and cosmetics.",
                BannedInCountries = []
            },
            new()
            {
                Id = 35, ENumber = "E1200", Name = "Polydextrose",
                AlternateNames = ["Polydextrose", "Litesse", "E1200"],
                Category = "Bulking Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May cause gas and bloating at high doses. Well-tolerated compared to other fibers. Prebiotic benefits documented.",
                Description = "Low-calorie bulking agent and soluble fiber used in reduced-calorie foods. Provides texture and mouthfeel. Found in baked goods, desserts, and confectionery.",
                BannedInCountries = []
            },
            new()
            {
                Id = 36, ENumber = "E1201", Name = "Polyvinylpyrrolidone",
                AlternateNames = ["PVP", "Povidone", "Polyvidone"],
                Category = "Stabilizer", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Not significantly absorbed. Used in pharmaceutical applications safely for decades.",
                Description = "Synthetic polymer used as a stabilizer in food, clarifying agent in beverages, and binder in tablets.",
                BannedInCountries = []
            },
            new()
            {
                Id = 37, ENumber = "E1202", Name = "Polyvinylpolypyrrolidone",
                AlternateNames = ["PVPP", "Crospovidone", "Polyclar"],
                Category = "Stabilizer", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Insoluble, not absorbed. Used to remove polyphenols from beverages. No significant health concerns.",
                Description = "Insoluble polymer used as a fining agent to remove polyphenols and prevent haze in beer, wine, and other beverages.",
                BannedInCountries = []
            },
            new()
            {
                Id = 38, ENumber = "E1203", Name = "Polyvinyl Alcohol",
                AlternateNames = ["PVA", "PVOH"],
                Category = "Stabilizer", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Minimal absorption. Used in pharmaceutical capsules and coatings.",
                Description = "Synthetic polymer used as a coating agent for food supplements and as a binder in food applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 39, ENumber = "E1204", Name = "Pullulan",
                AlternateNames = ["Pullulan", "E1204"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Non-digestible polysaccharide. Slowly fermented in colon. Used as edible film in breath fresheners and food packaging.",
                Description = "Linear polysaccharide produced by fungal fermentation. Used as a film former (edible films), thickener, and binder in confectionery.",
                BannedInCountries = []
            },
            new()
            {
                Id = 40, ENumber = "E121", Name = "Citrus Red 2",
                AlternateNames = ["Citrus Red No. 2", "CI 12156", "Solvent Red 80"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Restricted,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Restricted to surface application on oranges only (max 2 ppm). Potential carcinogenicity concerns if ingested internally. Not for use in any other food.",
                Description = "Synthetic red azo dye approved in the US exclusively for coloring the skin of mature oranges (FD&C Citrus Red 2). Not permitted for use in any other food product globally.",
                BannedInCountries = ["European Union", "United Kingdom", "Australia", "Canada", "Japan"]
            },
            new()
            {
                Id = 41, ENumber = "E123", Name = "Amaranth",
                AlternateNames = ["FD&C Red No. 2", "Azorubin S", "CI 16185", "Acid Red 27", "Food Red 9"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Banned in the US as suspected carcinogen. Some studies linked to DNA damage and colonic inflammation. May cause urticaria and asthma in sensitive individuals. EU has set strict ADI of 0.15 mg/kg bw.",
                Description = "Synthetic red azo dye used in the EU for aperitif wines, fish roe, and certain spirits. Was used globally before the US ban. Now replaced by Allura Red AC in most applications.",
                BannedInCountries = ["United States", "Norway", "Austria"]
            },
            new()
            {
                Id = 42, ENumber = "E125", Name = "Ponceau SX",
                AlternateNames = ["Scarlet GN", "CI 14700", "FD&C Red No. 4"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Restricted,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Not approved for food use in any major market. Limited toxicity data. Only permitted for topical/external applications in the US.",
                Description = "Synthetic red azo dye. Formerly used in foods but now restricted to external drugs and cosmetics in the US. Not permitted as a food additive in the EU or most other countries.",
                BannedInCountries = ["European Union", "United Kingdom", "Australia", "Canada", "Japan"]
            },
            new()
            {
                Id = 43, ENumber = "E126", Name = "Ponceau 6R",
                AlternateNames = ["Crystal Ponceau 6R", "CI 16290", "Acid Red 44"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Withdrawn from food use due to safety concerns. No current food exposure in any major market.",
                Description = "Synthetic red azo dye historically used in foods. No longer permitted as a food additive globally. Represents an obsolete additive that has been replaced by safer alternatives.",
                BannedInCountries = ["United States", "European Union", "United Kingdom", "Australia", "Canada", "Japan"]
            },
            new()
            {
                Id = 44, ENumber = "E128", Name = "Red 2G",
                AlternateNames = ["Azogeranine", "CI 18050", "Acid Red 1"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Avoid,
                HealthConcerns = "EFSA determined Red 2G is metabolized to the genotoxic carcinogen aniline. EU ban enacted in 2007. China classified it as a non-edible substance in 2025.",
                Description = "Synthetic red azo dye formerly used in breakfast sausages and burger meat with cereal content. Banned after EFSA found it breaks down into aniline, a genotoxic carcinogen. No longer permitted in any major market.",
                BannedInCountries = ["European Union", "United States", "United Kingdom", "Australia", "Canada", "Japan", "China", "Russia", "Switzerland", "Norway"]
            },
            new()
            {
                Id = 45, ENumber = "E130", Name = "Indanthrene Blue RS",
                AlternateNames = ["Indanthrone", "CI 69800", "Vat Blue 4"],
                Category = "Colorant", CspiRating = CspiRating.Unknown,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Limited toxicological data for food use. Not actively used as a food colorant in any major market.",
                Description = "Synthetic blue anthraquinone dye primarily used in textile dyeing. Listed in historical additive catalogs but never widely adopted as a food colorant. Not currently approved for food use.",
                BannedInCountries = ["United States", "European Union"]
            },
            new()
            {
                Id = 46, ENumber = "E140", Name = "Chlorophylls and Chlorophyllins",
                AlternateNames = ["Chlorophyll", "Chlorophyllin", "Natural Green 3", "CI 75810"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns at food-use levels. Very rarely may cause mild green discoloration of stools. Some individuals report mild digestive discomfort with high doses.",
                Description = "Natural green pigment extracted from edible plants such as alfalfa, grass, nettles, and spinach. Used in green-colored foods, confectionery, ice cream, sauces, and beverages.",
                BannedInCountries = []
            },
            new()
            {
                Id = 47, ENumber = "E141", Name = "Copper Complexes of Chlorophylls",
                AlternateNames = ["Copper Chlorophyllin", "Copper Complex of Chlorophyll", "E141i", "E141ii", "CI 75815", "Natural Green 3 (copper complex)"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "EFSA has established an acceptable daily intake. Trace copper release is well within safe limits. No significant health concerns at approved use levels.",
                Description = "Semi-synthetic green pigment created by replacing the central magnesium atom in chlorophyll with copper for increased stability. Used in green vegetables (canned), confectionery, chewing gum, sauces, and soups.",
                BannedInCountries = []
            },
            new()
            {
                Id = 48, ENumber = "E143", Name = "Fast Green FCF",
                AlternateNames = ["FD&C Green No. 3", "Fast Green", "CI 42053"],
                Category = "Colorant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "FDA approved for food use (FD&C Green No. 3). Some studies raised concerns about bladder tumors in animal studies at very high doses, but no human evidence. Banned in the EU.",
                Description = "Synthetic blue-green triarylmethane dye used in the US for green-colored foods including canned vegetables, jellies, desserts, and puddings. Also used in cosmetics and drugs.",
                BannedInCountries = ["European Union", "United Kingdom", "Norway", "Switzerland"]
            },
            new()
            {
                Id = 49, ENumber = "E1452", Name = "Starch Aluminium Octenyl Succinate",
                AlternateNames = ["Starch aluminium octenylsuccinate", "SAOS"],
                Category = "Thickener", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Aluminium content is concern. Limited toxicological data. EU specifies strict limits. Potential for aluminium accumulation with chronic exposure.",
                Description = "Chemically modified starch containing aluminium. Used as a stabilizer, thickener, and anti-caking agent in limited applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 50, ENumber = "E1505", Name = "Triethyl Citrate",
                AlternateNames = ["Triethyl citrate", "TEC"],
                Category = "Stabilizer", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Metabolized to normal body metabolites (ethanol in small amounts and citric acid). No significant health concerns at food-use levels.",
                Description = "Citrate ester used as a stabilizer, solvent, and plasticizer in food-grade films and coatings.",
                BannedInCountries = []
            },
            new()
            {
                Id = 51, ENumber = "E150a", Name = "Plain Caramel (Class I)",
                AlternateNames = ["Plain Caramel", "Class I Caramel Color", "Caustic Caramel", "Spirit Caramel"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns. EFSA re-evaluation concluded no safety issues at current exposure levels. Does not contain the processing contaminants 4-MEI or THI found in other caramel classes.",
                Description = "The simplest caramel color, produced by controlled heat treatment of carbohydrates (corn syrup, sucrose, glucose) without any ammonia or sulfite compounds. Used in spirits, baked goods, and confectionery.",
                BannedInCountries = []
            },
            new()
            {
                Id = 52, ENumber = "E150b", Name = "Caustic Sulfite Caramel (Class II)",
                AlternateNames = ["Class II Caramel Color", "Sulfite Caramel"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "EFSA re-evaluation concluded no major safety concerns. Contains sulfite residues that may trigger reactions in sulfite-sensitive asthmatics (rare). No 4-MEI formation (no ammonia used).",
                Description = "Caramel color produced by heating carbohydrates with sulfite compounds but without ammonia. Less common than Class III and IV caramels. Used in some spirits and vinegars.",
                BannedInCountries = []
            },
            new()
            {
                Id = 53, ENumber = "E151", Name = "Brilliant Black BN (Black PN)",
                AlternateNames = ["Black PN", "Brilliant Black PN", "Food Black 1", "CI 28440", "Naphthol Black"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Banned in the US and Japan. Part of the 'Southampton Six' colors associated with increased hyperactivity in children. EU requires warning labels on products containing it. May cause allergic reactions. EFSA established ADI of 1 mg/kg bw.",
                Description = "Synthetic black diazo dye used in the EU for sauces, desserts, confectionery, ice cream, mustard, soft drinks, and fish products. Provides black color in many food products.",
                BannedInCountries = ["United States", "Japan"]
            },
            new()
            {
                Id = 54, ENumber = "E1517", Name = "Glyceryl Diacetate (Diacetin)",
                AlternateNames = ["Diacetin", "Glycerol diacetate"],
                Category = "Solvent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Digested to normal body components (glycerol and acetic acid).",
                Description = "Glycerol ester used as a solvent for flavors and as a humectant in food applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 55, ENumber = "E1518", Name = "Glyceryl Triacetate (Triacetin)",
                AlternateNames = ["Triacetin", "Glycerol triacetate"],
                Category = "Humectant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Considered nontoxic. GRAS since 1975. Used as pharmaceutical excipient.",
                Description = "Triglyceride-like compound used as humectant, solvent for flavors, and plasticizer in chewing gum.",
                BannedInCountries = []
            },
            new()
            {
                Id = 56, ENumber = "E1519", Name = "Benzyl Alcohol",
                AlternateNames = ["Phenylmethanol"],
                Category = "Solvent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe at food-use levels. Can cause allergic reactions in sensitive individuals. Rapidly oxidized to benzoic acid in liver.",
                Description = "Aromatic alcohol used as a solvent for flavors and as a preservative in very limited applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 57, ENumber = "E152", Name = "Carbon Black (Hydrocarbon)",
                AlternateNames = ["Carbon Black", "Channel Black", "Furnace Black", "CI 77266"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Not approved for food use. May contain carcinogenic PAHs as impurities. E153 (vegetable carbon) is the approved alternative.",
                Description = "Synthetic black pigment produced by the incomplete combustion of hydrocarbon feedstocks. Not approved as a food additive. E153 (vegetable carbon) is the permitted alternative. Used in some non-food applications.",
                BannedInCountries = ["United States", "European Union", "United Kingdom", "Australia", "Canada"]
            },
            new()
            {
                Id = 58, ENumber = "E1520", Name = "Propan-1,2-diol (Propylene Glycol)",
                AlternateNames = ["Propylene glycol", "1,2-Propanediol"],
                Category = "Humectant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Metabolized to lactic acid, a normal metabolite. Very high doses may cause CNS depression (rare at food-use levels). FSA has specific warnings for slush ice drinks.",
                Description = "Food-grade glycol used as a humectant in baked goods, solvent for flavors and colors, and carrier for food additives. Also used in slush ice drinks.",
                BannedInCountries = []
            },
            new()
            {
                Id = 59, ENumber = "E1521", Name = "Polyethylene Glycol",
                AlternateNames = ["PEG", "Macrogol", "Polyethylene glycol"],
                Category = "Anti-foaming Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe at food-use levels. Low molecular weight PEG has some absorption. High molecular weight PEG (pharmaceutical laxative) is not absorbed and used as colonoscopy prep.",
                Description = "Synthetic polymer used as an anti-foaming agent, carrier, and plasticizer in food applications. Different molecular weights used for different purposes.",
                BannedInCountries = []
            },
            new()
            {
                Id = 60, ENumber = "E153", Name = "Vegetable Carbon (Activated Charcoal)",
                AlternateNames = ["Vegetable Carbon", "Activated Charcoal", "Carbon Black (vegetable)", "CI 77266", "Carbo Vegetabilis"],
                Category = "Colorant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "EU approved. Banned in the US as a food color additive. May bind medications and reduce their absorption. High doses may interfere with nutrient absorption in the gut. May cause constipation or black stools.",
                Description = "Fine black powder produced by carbonization of vegetable matter (coconut shells, peat, wood). Used in EU as black colorant in confectionery, ice cream, sauces, and trendy 'charcoal' foods.",
                BannedInCountries = ["United States"]
            },
            new()
            {
                Id = 61, ENumber = "E154", Name = "Brown FK (Kipper Brown)",
                AlternateNames = ["Kipper Brown", "Chocolate Brown FK", "Food Brown 1", "CI 30215"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Avoid,
                HealthConcerns = "EFSA could not conclude on safety due to deficiencies in toxicity data. Approval withdrawn in 2011. Previously used only for dyeing kippers. Banned in US, Canada, Japan, Australia, and most other countries.",
                Description = "Mixture of six synthetic azo dyes formerly used in smoked and cured fish (kippers). Gave an orange-brown color to fish products. EU approval was withdrawn in November 2011 after EFSA could not establish safety.",
                BannedInCountries = ["United States", "European Union", "Australia", "Canada", "Japan", "Switzerland", "New Zealand", "Norway", "Russia"]
            },
            new()
            {
                Id = 62, ENumber = "E155", Name = "Brown HT (Chocolate Brown HT)",
                AlternateNames = ["Chocolate Brown HT", "Food Brown 3", "CI 20285"],
                Category = "Colorant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Part of the 'Southampton Six' associated with hyperactivity in children. EU requires warning labels. Banned in US, Australia, Austria, Belgium, Denmark, France, Germany, Norway, Sweden, Switzerland, and Russia. May cause allergic skin reactions.",
                Description = "Synthetic brown diazo dye used primarily as a cocoa or caramel substitute in chocolate cakes, desserts, cookies, candies, cheeses, yogurts, jams, ice cream, and chocolate drinks.",
                BannedInCountries = ["United States", "Australia", "Austria", "Belgium", "Denmark", "France", "Germany", "Norway", "Sweden", "Switzerland", "Russia"]
            },
            new()
            {
                Id = 63, ENumber = "E160a", Name = "Carotenes (Alpha-, Beta-, Gamma-)",
                AlternateNames = ["Beta-Carotene", "Provitamin A", "CI 75130", "Mixed Carotenes"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No adverse effects at food-use levels. Extremely high intake from supplements may cause harmless yellowing of skin (carotenodermia). Beta-carotene supplementation in heavy smokers associated with increased lung cancer risk in clinical trials.",
                Description = "Natural orange-yellow pigments found in carrots, pumpkins, sweet potatoes, and dark leafy greens. Used in margarine, butter, cheese, fruit juices, baked goods, and beverages.",
                BannedInCountries = []
            },
            new()
            {
                Id = 64, ENumber = "E160c", Name = "Paprika Extract (Capsanthin, Capsorubin)",
                AlternateNames = ["Paprika Oleoresin", "Capsanthin", "Capsorubin", "CI 75125"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns. Very rare allergic reactions in individuals sensitive to bell peppers. Generally recognized as safe by FDA and EFSA.",
                Description = "Natural orange-red carotenoid pigment extracted from sweet red bell peppers. Used in processed meats (sausages), cheeses, sauces, snack foods, seasonings, and spice blends.",
                BannedInCountries = []
            },
            new()
            {
                Id = 65, ENumber = "E160d", Name = "Lycopene",
                AlternateNames = ["CI 75125", "Natural Yellow 27"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe at food-use levels. No adverse effects known. High supplemental doses may cause harmless skin discoloration (lycopenodermia).",
                Description = "Natural red carotenoid pigment from tomatoes and other red fruits. Used in soups, sauces, fruit preparations, beverages, and dietary supplements.",
                BannedInCountries = []
            },
            new()
            {
                Id = 66, ENumber = "E160e", Name = "Beta-apo-8'-carotenal (C30)",
                AlternateNames = ["Apo-Carotenal", "CI 40820", "Food Orange 6"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns at food-use levels. EFSA and FDA have established ADIs. Limited data on long-term high-dose effects but well-tolerated in studies.",
                Description = "Synthetic orange-red carotenoid colorant. Used in cheeses, juices, confectionery, and snack foods. Provides a more stable orange color than natural carotenoids in some applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 67, ENumber = "E160f", Name = "Ethyl Ester of Beta-apo-8'-carotenic Acid (C30)",
                AlternateNames = ["Food Orange 7", "CI 40825"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Similar safety profile to E160e. EU did not renew authorization but there is no specific evidence of harm. Banned in EU due to lack of data rather than proven risk.",
                Description = "Synthetic orange-red carotenoid colorant. Ethyl ester derivative of apocarotenoic acid. Formerly approved in the EU; approval not renewed. Still approved in Australia and New Zealand.",
                BannedInCountries = ["European Union"]
            },
            new()
            {
                Id = 68, ENumber = "E161a", Name = "Flavoxanthin",
                AlternateNames = ["CI 75135", "Xanthophyll"],
                Category = "Colorant", CspiRating = CspiRating.Unknown,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Very limited toxicological data specific to food additive use. Not actively used in commercial food production. Insufficient data for formal safety assessment.",
                Description = "Natural golden-yellow xanthophyll carotenoid found in buttercups, dandelions, and other yellow flowers. Listed in Codex Alimentarius but never widely adopted as a commercial food colorant.",
                BannedInCountries = ["United States", "European Union"]
            },
            new()
            {
                Id = 69, ENumber = "E161b", Name = "Lutein",
                AlternateNames = ["Marigold Extract", "Xanthophyll", "CI 75135"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns. Beneficial for eye health (macular degeneration prevention). Well-tolerated at food-use levels. EFSA concluded no safety concerns at current exposure.",
                Description = "Natural yellow carotenoid pigment extracted from marigold flowers (Tagetes erecta). Used in chicken feed to enhance egg yolk color and in foods, beverages, and dietary supplements.",
                BannedInCountries = []
            },
            new()
            {
                Id = 70, ENumber = "E161c", Name = "Cryptoxanthin",
                AlternateNames = ["Caricaxanthin", "CI 75136"],
                Category = "Colorant", CspiRating = CspiRating.Unknown,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Limited data specific to food additive use. Naturally present in fruits and vegetables (papaya, mango, oranges). Not actively used as a commercial colorant.",
                Description = "Natural orange-red xanthophyll carotenoid found in papaya, mango, oranges, and bell peppers. Has provitamin A activity. Listed in Codex but not commercially significant as a food additive.",
                BannedInCountries = ["United States", "European Union"]
            },
            new()
            {
                Id = 71, ENumber = "E161d", Name = "Rubixanthin",
                AlternateNames = ["CI 75137"],
                Category = "Colorant", CspiRating = CspiRating.Unknown,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Very limited data specific to food additive use. Naturally present in some fruits. No safety concerns expected at dietary levels but not formally evaluated as a food additive.",
                Description = "Natural orange-red xanthophyll carotenoid found in rose hips, sea buckthorn, and other fruits. Listed in Codex Alimentarius but not commercially significant as a food colorant.",
                BannedInCountries = ["United States", "European Union"]
            },
            new()
            {
                Id = 72, ENumber = "E161e", Name = "Violaxanthin",
                AlternateNames = ["CI 75138"],
                Category = "Colorant", CspiRating = CspiRating.Unknown,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Minimal data for food additive use. Naturally occurring in many vegetables. Not actively used in food production.",
                Description = "Natural orange xanthophyll carotenoid found in pansies, spinach, and other green vegetables. Listed in Codex Alimentarius but not commercially used as a food additive.",
                BannedInCountries = ["United States", "European Union"]
            },
            new()
            {
                Id = 73, ENumber = "E161f", Name = "Rhodoxanthin",
                AlternateNames = ["CI 75139"],
                Category = "Colorant", CspiRating = CspiRating.Unknown,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Very limited data. Naturally found in yew berries and some other plants. Not actively used in commercial food production.",
                Description = "Natural purple-red xanthophyll carotenoid found in yew berries and other plants. Listed in Codex Alimentarius but not commercially used as a food colorant.",
                BannedInCountries = ["United States", "European Union"]
            },
            new()
            {
                Id = 74, ENumber = "E161g", Name = "Canthaxanthin",
                AlternateNames = ["CI 40850", "Food Orange 8"],
                Category = "Colorant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "EU banned for food use. High doses (from tanning pills, not food) associated with retinopathy (canthaxanthin deposits in the retina). EFSA could not establish an ADI for food use. US allows it in food.",
                Description = "Natural orange carotenoid pigment found in mushrooms, crustaceans, and fish. Used as a feed additive for salmon (flesh color) and egg yolks. Limited direct use in human foods.",
                BannedInCountries = ["European Union"]
            },
            new()
            {
                Id = 75, ENumber = "E161h", Name = "Zeaxanthin",
                AlternateNames = ["CI 75136"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns. Beneficial for eye health. Well-tolerated at food-use levels. Used as a dietary supplement for vision support.",
                Description = "Natural orange-red xanthophyll carotenoid found in corn, peppers, saffron, and dark leafy greens. Used as a food colorant in some products and as a dietary supplement. Often paired with lutein.",
                BannedInCountries = []
            },
            new()
            {
                Id = 76, ENumber = "E161i", Name = "Citranaxanthin",
                AlternateNames = ["CI 40855"],
                Category = "Colorant", CspiRating = CspiRating.Unknown,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Limited toxicological data for human food use. Used in animal feed for egg yolk and salmonid coloration. Not actively used in direct human food products.",
                Description = "Synthetic xanthophyll carotenoid used mainly in poultry feed to enhance egg yolk color. Listed in Codex Alimentarius but not actively used as a direct human food additive in most jurisdictions.",
                BannedInCountries = ["United States", "European Union"]
            },
            new()
            {
                Id = 77, ENumber = "E161j", Name = "Astaxanthin",
                AlternateNames = ["CI 75136", "Astaxanthin Ester", "Microalgae Extract"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns. Potent antioxidant with documented health benefits including skin, eye, and immune support. Very well-tolerated.",
                Description = "Natural red carotenoid pigment from the microalgae Haematococcus pluvialis, also found in salmon, shrimp, and lobster. Used as a feed additive for salmon and as a dietary supplement.",
                BannedInCountries = []
            },
            new()
            {
                Id = 78, ENumber = "E162", Name = "Beetroot Red (Betanin)",
                AlternateNames = ["Betanin", "Beetroot Extract", "CI 75840", "Natural Red 33"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns. May cause harmless red/pink discoloration of urine and stools (beeturia) in some individuals. Nitrate content in beetroot may be a consideration for those on restricted diets.",
                Description = "Natural red-purple pigment extracted from red beetroot (Beta vulgaris). Used in ice cream, yogurts, beverages, confectionery, processed meats, and plant-based burger colorants.",
                BannedInCountries = []
            },
            new()
            {
                Id = 79, ENumber = "E163", Name = "Anthocyanins",
                AlternateNames = ["Grape Skin Extract", "Blackcurrant Extract", "Red Cabbage Extract", "E163(i-v)"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns. Associated with numerous health benefits including improved cardiovascular health, reduced inflammation, and potential prebiotic effects.",
                Description = "Natural flavonoid pigments responsible for red, purple, and blue colors in fruits and vegetables. Extracted from grape skins, blackcurrants, red cabbage, elderberries, and purple carrots. Used in beverages, jams, yogurts, and confectionery.",
                BannedInCountries = []
            },
            new()
            {
                Id = 80, ENumber = "E164", Name = "Saffron (Crocin)",
                AlternateNames = ["Saffron Extract", "Crocin", "Crocein", "CI 75100", "Natural Yellow 6"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe at food-use levels. Very high doses (grams) can be toxic. Adulteration is a concern. May cause allergic reactions in very rare cases.",
                Description = "Natural orange-yellow carotenoid pigment from Crocus sativus stigmas. The world's most expensive spice by weight. Used in rice dishes, baked goods, confectionery, and beverages.",
                BannedInCountries = []
            },
            new()
            {
                Id = 81, ENumber = "E170", Name = "Calcium Carbonate",
                AlternateNames = ["Chalk", "Limestone", "CI 77220", "Calcium Carbonate (E170i)", "Calcium Hydrogen Carbonate (E170ii)"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns at food levels. Acts as a dietary calcium source. High supplemental doses may cause constipation, gas, or bloating. Calcium supplementation may interfere with iron absorption.",
                Description = "Naturally occurring mineral used as a white colorant, calcium fortificant, acidity regulator, and anti-caking agent. Found in toothpaste, baked goods, breakfast cereals, soy milk, and supplements.",
                BannedInCountries = []
            },
            new()
            {
                Id = 82, ENumber = "E172", Name = "Iron Oxides and Hydroxides",
                AlternateNames = ["Iron Oxide Red (E172i)", "Iron Oxide Yellow (E172ii)", "Iron Oxide Black (E172iii)", "CI 77491", "CI 77492", "CI 77499"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "No significant health concerns at food-use levels. Iron oxides are poorly absorbed. EFSA re-evaluation confirmed safety. Some nano-sized particles are an area of ongoing research but current use is within safe limits.",
                Description = "Naturally occurring or synthetic iron oxide pigments used as brown, red, yellow, and black colorants. Used in cake decorations, surimi, olives, confectionery coatings, and pet foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 83, ENumber = "E173", Name = "Aluminium",
                AlternateNames = ["Aluminium Powder", "CI 77000"],
                Category = "Colorant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Permitted in the EU only for external coating of confectionery and decoration of cakes. Aluminium intake from all sources is a concern as it can accumulate in tissues. Some studies link aluminium to neurotoxicity. Banned in US for food use.",
                Description = "Fine metallic aluminium powder used as a silver-gray colorant for decorative coatings on confectionery, sugar pearls, and cake decorations. Very limited use.",
                BannedInCountries = ["United States"]
            },
            new()
            {
                Id = 84, ENumber = "E174", Name = "Silver",
                AlternateNames = ["Silver Leaf", "Silver Powder", "CI 77820"],
                Category = "Colorant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Approved in EU only for external decorative coatings and liqueurs. Ingested silver can cause argyria (irreversible gray-blue skin discoloration) with chronic high intake. Antimicrobial effects may disrupt gut microbiome. Banned in US for food use.",
                Description = "Metallic silver used as a decorative silver colorant in the EU for confectionery, cake decorations, and sugar pearls. Very limited use in dragées and liqueurs.",
                BannedInCountries = ["United States"]
            },
            new()
            {
                Id = 85, ENumber = "E175", Name = "Gold",
                AlternateNames = ["Gold Leaf", "Gold Powder", "CI 77480"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Chemically inert noble metal. No known toxicity at food-use levels. Passes through the GI tract unabsorbed. Very expensive, so use is limited. Banned in US for food use.",
                Description = "Metallic gold used in the EU as a decorative gold colorant on confectionery, luxury cakes, chocolates, and in premium alcoholic beverages (e.g., Goldschläger). Extremely limited use due to high cost.",
                BannedInCountries = ["United States"]
            },
            new()
            {
                Id = 86, ENumber = "E180", Name = "Lithol Rubine BK (Pigment Rubine)",
                AlternateNames = ["Pigment Rubine", "Lithol Rubine", "CI 15850", "D&C Red No. 6", "D&C Red No. 7"],
                Category = "Colorant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.Banned,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Limited to surface applications (cheese rinds, fruit coatings) in the EU. Banned in the US. As an azo pigment, potential concerns about azo reduction and heavy metal content.",
                Description = "Synthetic red azo pigment (calcium salt of 3-hydroxy-4-[(4-methyl-2-sulfophenyl)azo]-2-naphthalenecarboxylic acid). Used only for coloring edible cheese rinds and fruit surface coatings.",
                BannedInCountries = ["United States"]
            },
            new()
            {
                Id = 87, ENumber = "E300", Name = "Ascorbic Acid (Vitamin C)",
                AlternateNames = ["Vitamin C", "L-Ascorbic Acid"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Very high supplemental doses may cause diarrhea and GI upset. No specific gut health concerns at food-use levels.",
                Description = "Natural antioxidant. Preserves color and freshness in cured meats, beverages, fruit products, bread, and oils.",
                BannedInCountries = []
            },
            new()
            {
                Id = 88, ENumber = "E301", Name = "Sodium Ascorbate",
                AlternateNames = ["Sodium L-Ascorbate", "Vitamin C Sodium Salt"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute sodium intake at very high levels. Same minor GI upset risk as ascorbic acid at excessive doses.",
                Description = "Mineral salt of vitamin C used as antioxidant in cured meats, beverages, and fruit products. More stable than ascorbic acid in some applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 89, ENumber = "E302", Name = "Calcium Ascorbate",
                AlternateNames = ["Calcium L-Ascorbate", "Vitamin C Calcium Salt"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Provides calcium which may be beneficial. No notable gut health concerns at food additive levels.",
                Description = "Mineral salt of vitamin C used as antioxidant in beverages, fruit products, and processed foods. Also serves as calcium fortificant.",
                BannedInCountries = []
            },
            new()
            {
                Id = 90, ENumber = "E303", Name = "Potassium Ascorbate",
                AlternateNames = ["Potassium L-Ascorbate", "Vitamin C Potassium Salt"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Minimal documented adverse effects at food-use levels. May contribute potassium intake.",
                Description = "Mineral salt of vitamin C used as antioxidant in select food applications. Less common than E301 and E302.",
                BannedInCountries = []
            },
            new()
            {
                Id = 91, ENumber = "E304", Name = "Ascorbyl Palmitate",
                AlternateNames = ["Vitamin C Palmitate", "L-Ascorbyl Palmitate"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Hydrolyzed in gut to ascorbic acid and palmitic acid, both naturally occurring. No specific gut health concerns.",
                Description = "Fat-soluble vitamin C derivative used as antioxidant in oils, fats, and fat-containing foods. Also used in infant formulas.",
                BannedInCountries = []
            },
            new()
            {
                Id = 92, ENumber = "E305", Name = "Ascorbyl Stearate",
                AlternateNames = ["L-Ascorbyl Stearate", "Vitamin C Stearate"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Hydrolyzed to ascorbic acid and stearic acid, both naturally occurring. No known gut health concerns.",
                Description = "Fat-soluble vitamin C ester used as antioxidant in fats and oils. Less common than ascorbyl palmitate.",
                BannedInCountries = []
            },
            new()
            {
                Id = 93, ENumber = "E306", Name = "Tocopherols (Vitamin E, Natural)",
                AlternateNames = ["Mixed Tocopherols", "Natural Vitamin E", "Tocopherol-Rich Extract"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe and beneficial. Natural Vitamin E is an essential nutrient. No adverse gut effects at food additive levels.",
                Description = "Natural antioxidant from vegetable oils. Prevents fat rancidity in oils, margarine, baked goods, and snacks.",
                BannedInCountries = []
            },
            new()
            {
                Id = 94, ENumber = "E307", Name = "Alpha-Tocopherol (Synthetic)",
                AlternateNames = ["dl-Alpha-Tocopherol", "Synthetic Vitamin E", "all-rac-Alpha-Tocopherol"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Synthetic Vitamin E is well absorbed. No significant gut health concerns at food additive levels.",
                Description = "Synthetic form of Vitamin E used as antioxidant in oils, fats, and processed foods. Has approximately half the biological activity of natural Vitamin E.",
                BannedInCountries = []
            },
            new()
            {
                Id = 95, ENumber = "E308", Name = "Gamma-Tocopherol",
                AlternateNames = ["Gamma-Tocopherol", "Vitamin E Gamma"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Natural component of many vegetable oils. No known gut health concerns at food-use levels.",
                Description = "Natural form of Vitamin E used as antioxidant in fats, oils, and processed foods. Particularly effective against nitrogen-based radicals.",
                BannedInCountries = []
            },
            new()
            {
                Id = 96, ENumber = "E309", Name = "Delta-Tocopherol",
                AlternateNames = ["Delta-Tocopherol", "Vitamin E Delta"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Natural component of some vegetable oils. No known gut health concerns.",
                Description = "Natural form of Vitamin E with strong antioxidant activity. Used in oils, fats, and lipid-containing foods to prevent rancidity.",
                BannedInCountries = []
            },
            new()
            {
                Id = 97, ENumber = "E310", Name = "Propyl Gallate",
                AlternateNames = ["Propyl 3,4,5-Trihydroxybenzoate", "Propyl Gallate"],
                Category = "Antioxidant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Possible carcinogen in animal studies at high doses. May cause stomach irritation. Some evidence of estrogenic activity. Restricted use levels in both EU and US.",
                Description = "Synthetic antioxidant used in oils, fats, meat products, chewing gum, and snack foods. Usually used in combination with BHA/BHT or citric acid.",
                BannedInCountries = ["Some countries restrict use in infant foods"]
            },
            new()
            {
                Id = 98, ENumber = "E311", Name = "Octyl Gallate",
                AlternateNames = ["Octyl 3,4,5-Trihydroxybenzoate", "E311"],
                Category = "Antioxidant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.NotAuthorized,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Avoid,
                HealthConcerns = "Limited safety data. Potential for allergic reactions and skin sensitization. Not authorized for food use in the US. EFSA has raised concerns about data gaps.",
                Description = "Synthetic antioxidant used in fats, oils, and some processed foods. Rarely used due to regulatory restrictions and safety concerns.",
                BannedInCountries = ["United States (not authorized)", "Japan (restricted)"]
            },
            new()
            {
                Id = 99, ENumber = "E312", Name = "Dodecyl Gallate",
                AlternateNames = ["Dodecyl 3,4,5-Trihydroxybenzoate", "Lauryl Gallate", "E312"],
                Category = "Antioxidant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.NotAuthorized,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Avoid,
                HealthConcerns = "Not authorized in the US. Animal studies suggest potential for liver and kidney effects at high doses. Limited long-term safety data. Skin sensitization potential.",
                Description = "Synthetic antioxidant used in some fats, oils, and processed meats. Similar application to other gallates but with longer carbon chain.",
                BannedInCountries = ["United States (not authorized)"]
            },
            new()
            {
                Id = 100, ENumber = "E313", Name = "Ethyl Gallate",
                AlternateNames = ["Ethyl 3,4,5-Trihydroxybenzoate", "E313"],
                Category = "Antioxidant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.NotAuthorized,
                EuRegulatoryStatus = EuRegulatoryStatus.NotAuthorized,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Not authorized for food use in the EU or US. Very limited toxicological data. Similar concerns to other gallates regarding potential toxicity.",
                Description = "Synthetic antioxidant. Rarely used in food. Most regulatory authorities have not approved it due to insufficient safety data.",
                BannedInCountries = ["European Union (not authorized)", "United States (not authorized)"]
            },
            new()
            {
                Id = 101, ENumber = "E314", Name = "Guaiac Resin",
                AlternateNames = ["Guaiac Gum", "Guaiac", "Gum Guaiacum"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Natural plant resin with long history of use. Some individuals may experience allergic reactions. No significant gut health concerns.",
                Description = "Natural antioxidant from Guaiacum wood. Used historically in butter, margarine, and oils. Now largely replaced by other antioxidants.",
                BannedInCountries = []
            },
            new()
            {
                Id = 102, ENumber = "E315", Name = "Erythorbic Acid",
                AlternateNames = ["Isoascorbic Acid", "D-Araboascorbic Acid", "E315"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Some studies suggest potential interference with vitamin C metabolism at very high doses. May cause oxalate kidney stones in susceptible individuals at high intake.",
                Description = "Synthetic isomer of ascorbic acid used as antioxidant in cured meats, beverages, and frozen foods. Prevents nitrosamine formation in cured meats.",
                BannedInCountries = []
            },
            new()
            {
                Id = 103, ENumber = "E316", Name = "Sodium Erythorbate",
                AlternateNames = ["Sodium Isoascorbate", "Erythorbic Acid Sodium Salt", "E316"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Some newer cohort studies have suggested small associations with cancer incidence at very high intakes, but causal link not established. May contribute dietary sodium.",
                Description = "Synthetic antioxidant used in cured and processed meats to stabilize color and reduce nitrosamine formation. Also used in beverages and frozen foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 104, ENumber = "E317", Name = "Isoascorbic Acid",
                AlternateNames = ["Erythorbic Acid", "D-Isoascorbic Acid", "E317"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Same profile as erythorbic acid (E315). No significant gut health concerns at food additive levels.",
                Description = "Same compound as E315 (erythorbic acid). Used as antioxidant in cured meats, beverages, and processed foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 105, ENumber = "E318", Name = "Sodium Isoascorbate",
                AlternateNames = ["Sodium Erythorbate", "Sodium D-Isoascorbate", "E318"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Same profile as sodium erythorbate (E316). May contribute dietary sodium. No significant gut concerns.",
                Description = "Synthetic antioxidant used in cured meats, beverages, and processed foods. Same compound as E316.",
                BannedInCountries = []
            },
            new()
            {
                Id = 106, ENumber = "E323", Name = "Anoxomer",
                AlternateNames = ["E323", "Polymeric Antioxidant"],
                Category = "Antioxidant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.NotAuthorized,
                EuRegulatoryStatus = EuRegulatoryStatus.NotAuthorized,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Not authorized in EU or US. Insufficient safety data for food use. Limited toxicological information available.",
                Description = "Synthetic polymeric antioxidant developed as an alternative to BHA/BHT. Never widely adopted. Not approved for food use in most jurisdictions.",
                BannedInCountries = ["European Union (not authorized)", "United States (not authorized)", "Most countries"]
            },
            new()
            {
                Id = 107, ENumber = "E324", Name = "Ethoxyquin",
                AlternateNames = ["1,2-Dihydro-6-ethoxy-2,2,4-trimethylquinoline", "EMQ", "E324"],
                Category = "Antioxidant", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.Restricted,
                EuRegulatoryStatus = EuRegulatoryStatus.Banned,
                SafetyRating = SafetyRating.Avoid,
                HealthConcerns = "Classified as a possible human carcinogen. Associated with liver and kidney toxicity in animal studies. Banned in EU for food use. May cause allergic reactions. Only permitted as feed additive with strict limits in some countries.",
                Description = "Synthetic antioxidant used primarily in animal feed to prevent fat rancidity. Not used in human food in most jurisdictions due to safety concerns.",
                BannedInCountries = ["European Union (banned in food)", "Many countries restrict severely"]
            },
            new()
            {
                Id = 108, ENumber = "E325", Name = "Sodium Lactate",
                AlternateNames = ["Lactic Acid Sodium Salt", "Sodium 2-Hydroxypropanoate", "E325"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute dietary sodium. Very well tolerated. No significant gut health concerns.",
                Description = "Sodium salt of lactic acid used as acidity regulator, humectant, and antimicrobial in processed meats, cheese, and dressings. Also used as a shelf-life extender.",
                BannedInCountries = []
            },
            new()
            {
                Id = 109, ENumber = "E326", Name = "Potassium Lactate",
                AlternateNames = ["Lactic Acid Potassium Salt", "Potassium 2-Hydroxypropanoate", "E326"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. People on potassium-restricted diets may need to monitor intake. No significant gut health concerns.",
                Description = "Potassium salt of lactic acid used as acidity regulator and antimicrobial preservative in processed meats, poultry, and other foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 110, ENumber = "E327", Name = "Calcium Lactate",
                AlternateNames = ["Lactic Acid Calcium Salt", "Calcium 2-Hydroxypropanoate", "E327"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute to calcium intake. Very well tolerated. No significant gut health concerns at food additive levels.",
                Description = "Calcium salt of lactic acid used as acidity regulator, firming agent in fruits/vegetables, leavening agent, and calcium supplement in foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 111, ENumber = "E328", Name = "Ammonium Lactate",
                AlternateNames = ["Lactic Acid Ammonium Salt", "Ammonium 2-Hydroxypropanoate", "E328"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Ammonium may be a concern for individuals with liver impairment at high doses. No significant gut health concerns at normal food levels.",
                Description = "Ammonium salt of lactic acid used as acidity regulator in some processed foods. Less common than sodium, potassium, and calcium lactates.",
                BannedInCountries = []
            },
            new()
            {
                Id = 112, ENumber = "E329", Name = "Magnesium Lactate",
                AlternateNames = ["Lactic Acid Magnesium Salt", "Magnesium 2-Hydroxypropanoate", "E329"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May cause mild laxative effect at very high doses. Magnesium is an essential mineral. No significant concerns at food additive levels.",
                Description = "Magnesium salt of lactic acid used as acidity regulator and mineral fortificant in foods and beverages.",
                BannedInCountries = []
            },
            new()
            {
                Id = 113, ENumber = "E332", Name = "Potassium Citrates",
                AlternateNames = ["Tripotassium Citrate", "Monopotassium Citrate", "Potassium Citrate", "E332"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Individuals on potassium-restricted diets should monitor. May have mild laxative effect at very high doses. No significant gut concerns at food levels.",
                Description = "Potassium salts of citric acid used as acidity regulators in beverages, dairy products, processed cheeses, and desserts. Also used as potassium supplement.",
                BannedInCountries = []
            },
            new()
            {
                Id = 114, ENumber = "E333", Name = "Calcium Citrates",
                AlternateNames = ["Tricalcium Citrate", "Monocalcium Citrate", "Calcium Citrate", "E333"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Very high total calcium intake may cause mild digestive discomfort. No significant concerns at food additive levels.",
                Description = "Calcium salts of citric acid used as acidity regulators in beverages, dairy products, and as calcium fortificants. Also used as firming agent in canned fruits and vegetables.",
                BannedInCountries = []
            },
            new()
            {
                Id = 115, ENumber = "E335", Name = "Sodium Tartrate",
                AlternateNames = ["Disodium Tartrate", "Tartaric Acid Disodium Salt", "E335"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute dietary sodium. No significant gut health concerns at food additive levels.",
                Description = "Sodium salt of tartaric acid used as acidity regulator in wines, baking powder, gelatin desserts, and jams.",
                BannedInCountries = []
            },
            new()
            {
                Id = 116, ENumber = "E336", Name = "Potassium Tartrates (Cream of Tartar)",
                AlternateNames = ["Cream of Tartar", "Potassium Bitartrate", "Monopotassium Tartrate", "Dipotassium Tartrate", "E336"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. People on potassium-restricted diets should monitor intake. May have mild laxative effect at very high doses.",
                Description = "Potassium salts of tartaric acid. Cream of tartar used in baking as leavening agent and stabilizer for egg whites. Also used in wines and confectionery.",
                BannedInCountries = []
            },
            new()
            {
                Id = 117, ENumber = "E337", Name = "Sodium Potassium Tartrate",
                AlternateNames = ["Rochelle Salt", "Potassium Sodium Tartrate", "Seignette Salt", "E337"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute both sodium and potassium to diet. No significant gut health concerns.",
                Description = "Double salt of tartaric acid used as acidity regulator in cheeses, confectionery, baking powder, and gelatin desserts.",
                BannedInCountries = []
            },
            new()
            {
                Id = 118, ENumber = "E340", Name = "Potassium Phosphates",
                AlternateNames = ["Monopotassium Phosphate", "Dipotassium Phosphate", "Tripotassium Phosphate", "E340"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Emerging evidence links excessive phosphate intake to kidney disease, cardiovascular calcification, and bone mineral loss. Individuals with CKD should be particularly cautious. EFSA group ADI for all phosphates: 40 mg/kg bw/day (as phosphorus).",
                Description = "Potassium salts of phosphoric acid used as acidity regulators, emulsifying salts in processed cheese, buffering agents, and mineral supplements.",
                BannedInCountries = []
            },
            new()
            {
                Id = 119, ENumber = "E342", Name = "Ammonium Phosphates",
                AlternateNames = ["Monoammonium Phosphate", "Diammonium Phosphate", "Ammonium Phosphate", "E342"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Excessive phosphate intake linked to kidney and cardiovascular health concerns. Ammonium component may be a concern for individuals with liver impairment at high doses. EFSA group ADI for phosphates: 40 mg/kg bw/day.",
                Description = "Ammonium salts of phosphoric acid used as acidity regulators in baked goods (leavening), as yeast nutrients in brewing, and in processed cheeses.",
                BannedInCountries = []
            },
            new()
            {
                Id = 120, ENumber = "E343", Name = "Magnesium Phosphates",
                AlternateNames = ["Dimagnesium Phosphate", "Trimagnesium Phosphate", "Magnesium Phosphate", "E343"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute beneficial magnesium. Same phosphate concerns as other phosphates at very high intake but mitigated by magnesium content.",
                Description = "Magnesium salts of phosphoric acid used as acidity regulators, anti-caking agents, and mineral supplements in foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 121, ENumber = "E344", Name = "Lecithin Citrate",
                AlternateNames = ["Citrated Lecithin", "E344"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Hydrolyzed to lecithin and citric acid in the gut, both safe substances. No significant gut health concerns.",
                Description = "Modified lecithin used as emulsifier and acidity regulator in select food applications. Rarely encountered.",
                BannedInCountries = []
            },
            new()
            {
                Id = 122, ENumber = "E345", Name = "Magnesium Citrate",
                AlternateNames = ["Trimagnesium Citrate", "Magnesium Citrate", "E345"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May have mild laxative effect at higher supplemental doses. Magnesium is an essential mineral. No significant concerns at food additive levels.",
                Description = "Magnesium salt of citric acid used as acidity regulator, magnesium supplement, and buffering agent in foods and beverages.",
                BannedInCountries = []
            },
            new()
            {
                Id = 123, ENumber = "E349", Name = "Ammonium Malate",
                AlternateNames = ["Malic Acid Ammonium Salt", "Ammonium Malate", "E349"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Malic acid is naturally present in many fruits. Ammonium may be a concern for individuals with liver impairment at very high doses.",
                Description = "Ammonium salt of malic acid used as acidity regulator in some processed foods and beverages.",
                BannedInCountries = []
            },
            new()
            {
                Id = 124, ENumber = "E350", Name = "Sodium Malate",
                AlternateNames = ["Disodium Malate", "Malic Acid Sodium Salt", "E350"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute dietary sodium. No significant gut health concerns. Malic acid is a natural fruit acid.",
                Description = "Sodium salt of malic acid used as acidity regulator in beverages, desserts, jams, and processed foods. Also used as flavor enhancer.",
                BannedInCountries = []
            },
            new()
            {
                Id = 125, ENumber = "E351", Name = "Potassium Malate",
                AlternateNames = ["Dipotassium Malate", "Malic Acid Potassium Salt", "E351"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Individuals on potassium-restricted diets should monitor. No significant gut health concerns.",
                Description = "Potassium salt of malic acid used as acidity regulator in beverages and processed foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 126, ENumber = "E352", Name = "Calcium Malate",
                AlternateNames = ["Dicalcium Malate", "Malic Acid Calcium Salt", "E352"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute to calcium intake. Very well tolerated. No significant gut health concerns.",
                Description = "Calcium salt of malic acid used as acidity regulator in beverages, baby foods, and as a calcium fortificant.",
                BannedInCountries = []
            },
            new()
            {
                Id = 127, ENumber = "E353", Name = "Metatartaric Acid",
                AlternateNames = ["Metatartaric Acid", "E353"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Slowly hydrolyzed to tartaric acid in the gut. No significant gut health concerns. Primarily used in wine production.",
                Description = "Polymerized tartaric acid used as stabilizer in wines to prevent potassium bitartrate precipitation. Not commonly found outside wine.",
                BannedInCountries = []
            },
            new()
            {
                Id = 128, ENumber = "E354", Name = "Calcium Tartrate",
                AlternateNames = ["Tartaric Acid Calcium Salt", "Calcium Tartrate", "E354"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute calcium. No significant gut health concerns.",
                Description = "Calcium salt of tartaric acid used as acidity regulator in baking powder, desserts, and processed foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 129, ENumber = "E355", Name = "Adipic Acid",
                AlternateNames = ["Hexanedioic Acid", "Adipic Acid", "E355"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Naturally found in beets and other plants. No significant gut health concerns at food additive levels.",
                Description = "Naturally occurring acid used as acidity regulator in beverages, baking powder, gelatin desserts, and confectionery. Also used as leavening acid.",
                BannedInCountries = []
            },
            new()
            {
                Id = 130, ENumber = "E356", Name = "Sodium Adipate",
                AlternateNames = ["Disodium Adipate", "Adipic Acid Sodium Salt", "E356"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute dietary sodium. No significant gut health concerns.",
                Description = "Sodium salt of adipic acid used as acidity regulator in beverages, desserts, and processed foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 131, ENumber = "E357", Name = "Potassium Adipate",
                AlternateNames = ["Dipotassium Adipate", "Adipic Acid Potassium Salt", "E357"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Individuals on potassium-restricted diets should monitor. No significant gut health concerns.",
                Description = "Potassium salt of adipic acid used as acidity regulator in select food and beverage applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 132, ENumber = "E363", Name = "Succinic Acid",
                AlternateNames = ["Butanedioic Acid", "Amber Acid", "Succinic Acid", "E363"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Natural cellular metabolite. No significant gut health concerns at food additive levels.",
                Description = "Naturally occurring acid involved in cellular energy metabolism. Used as acidity regulator in beverages, desserts, and soups. Imparts a brothy flavor.",
                BannedInCountries = []
            },
            new()
            {
                Id = 133, ENumber = "E365", Name = "Sodium Fumarate",
                AlternateNames = ["Disodium Fumarate", "Fumaric Acid Sodium Salt", "E365"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May contribute dietary sodium. No significant gut health concerns. Fumaric acid is a naturally occurring fruit acid.",
                Description = "Sodium salt of fumaric acid used as acidity regulator in beverages, baking powder, and processed foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 134, ENumber = "E370", Name = "1,4-Heptonolactone",
                AlternateNames = ["Heptonolactone", "1,4-Heptonolactone", "E370"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Limited data but generally safe based on available information. Hydrolyzed to heptanoic acid and its derivatives in the body.",
                Description = "Lactone compound used as acidity regulator in select food applications. Rarely encountered in modern food products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 135, ENumber = "E375", Name = "Nicotinic Acid (Niacin, Vitamin B3)",
                AlternateNames = ["Niacin", "Vitamin B3", "Nicotinic Acid", "E375"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe and beneficial. Essential B vitamin. At very high supplemental doses may cause temporary skin flushing and mild GI upset. No concerns at food-use levels.",
                Description = "Essential B vitamin used as nutrient supplement and antioxidant in flour, cereals, bread, and other fortified foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 136, ENumber = "E380", Name = "Triammonium Citrate",
                AlternateNames = ["Ammonium Citrate", "Triammonium Citrate", "E380"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Ammonium may be a concern for individuals with liver impairment at high doses. Citrate component is safe. Limited direct studies.",
                Description = "Ammonium salt of citric acid used as acidity regulator in processed cheeses, beverages, and other foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 137, ENumber = "E381", Name = "Ammonium Ferric Citrate",
                AlternateNames = ["Ferric Ammonium Citrate", "Iron(III) Ammonium Citrate", "E381"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Excess iron intake may cause GI irritation, constipation, and oxidative stress in the gut. Iron overload is a concern for individuals with hemochromatosis. Usefully provides iron for those deficient.",
                Description = "Iron-containing compound used as iron fortificant and acidity regulator. Used in some nutritional supplements and fortified foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 138, ENumber = "E385", Name = "Calcium Disodium EDTA",
                AlternateNames = ["Calcium Disodium Ethylenediaminetetraacetate", "Calcium Disodium Edetate", "EDTA", "E385"],
                Category = "Antioxidant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "May cause GI irritation and loose stools at high intake. Can chelate essential minerals (zinc, iron) with chronic high exposure. Newer animal studies suggest EDTA may aggravate colitis in inflamed guts. Limited use in both EU and US (specific products only). JECFA ADI: 0-2.5 mg/kg bw/day.",
                Description = "Synthetic chelating agent used to preserve color, flavor, and texture in dressings, sauces, canned seafood, canned legumes, and soft drinks.",
                BannedInCountries = []
            },
            new()
            {
                Id = 139, ENumber = "E386", Name = "Disodium EDTA",
                AlternateNames = ["Disodium Ethylenediaminetetraacetate", "Disodium Edetate", "EDTA", "E386"],
                Category = "Antioxidant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Similar to E385: may chelate essential minerals, cause GI irritation at high doses, and potentially worsen gut inflammation. More restricted use than E385 in food. JECFA ADI applies to total EDTA intake.",
                Description = "Synthetic chelating agent used in some foods, beverages, and cosmetics. Less common in food than calcium disodium EDTA.",
                BannedInCountries = []
            },
            new()
            {
                Id = 140, ENumber = "E387", Name = "Oxystearin",
                AlternateNames = ["Oxidized Stearin", "E387"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Limited modern usage. No significant gut health concerns at food additive levels.",
                Description = "Oxidized glyceride of stearic and other fatty acids used as stabilizer in salad oils and as defoaming agent. Rarely used.",
                BannedInCountries = []
            },
            new()
            {
                Id = 141, ENumber = "E388", Name = "Thiodipropionic Acid",
                AlternateNames = ["TDPA", "3,3'-Thiodipropionic Acid", "E388"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Limited data but no significant safety concerns identified. No known gut health concerns at food additive levels.",
                Description = "Synthetic antioxidant used in some oils, fats, and food packaging materials. Also used as a stabilizer.",
                BannedInCountries = []
            },
            new()
            {
                Id = 142, ENumber = "E389", Name = "Dilauryl Thiodipropionate",
                AlternateNames = ["DLTDP", "Dilauryl Thiodipropionate", "E389"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Hydrolyzed to lauryl alcohol and thiodipropionic acid in the gut. No significant gut concerns at food additive levels.",
                Description = "Ester of lauryl alcohol and thiodipropionic acid used as antioxidant in fats, oils, and fat-containing foods. Rarely used.",
                BannedInCountries = []
            },
            new()
            {
                Id = 143, ENumber = "E390", Name = "Distearyl Thiodipropionate",
                AlternateNames = ["DSTDP", "Distearyl Thiodipropionate", "E390"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Hydrolyzed to stearyl alcohol and thiodipropionic acid. No significant gut health concerns.",
                Description = "Ester of stearyl alcohol and thiodipropionic acid used as antioxidant in fats, oils, and food packaging. Rarely used in direct food applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 144, ENumber = "E391", Name = "Phytic Acid (Inositol Hexaphosphate)",
                AlternateNames = ["Inositol Hexaphosphate", "IP6", "Phytate", "E391"],
                Category = "Antioxidant", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Reduces absorption of essential minerals (iron, zinc, calcium) by forming insoluble complexes. May contribute to mineral deficiencies in vulnerable populations. Also has antioxidant and potential anticancer properties. Generally only a concern with high intake from whole grains combined with marginal mineral status.",
                Description = "Naturally occurring phosphorus storage compound in plant seeds. Added to some foods as antioxidant. Also used as preservative in canned seafood to prevent discoloration.",
                BannedInCountries = []
            },
            new()
            {
                Id = 145, ENumber = "E392", Name = "Rosemary Extract",
                AlternateNames = ["Extracts of Rosemary", "Rosemary Extract", "E392", "Carnosic Acid"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Natural herb extract with good safety profile. Some concerns about very high doses of isolated carnosic acid under study, but food-use levels are safe. No significant gut health concerns.",
                Description = "Natural antioxidant from rosemary leaves used in oils, meats, snacks, and seasonings. Increasingly used as a clean-label alternative to BHA/BHT/TBHQ.",
                BannedInCountries = []
            },
            new()
            {
                Id = 146, ENumber = "E399", Name = "Calcium Lactobionate",
                AlternateNames = ["Lactobionic Acid Calcium Salt", "Calcium Lactobionate", "E399"],
                Category = "Acidity Regulator", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Provides bioavailable calcium. No significant gut health concerns at food additive levels.",
                Description = "Calcium salt of lactobionic acid used as acidity regulator, stabilizer in beverages, and calcium fortificant. Also used in some medical and cosmetic applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 147, ENumber = "E406", Name = "Agar",
                AlternateNames = ["Agar-agar", "Kanten", "Japanese isinglass"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May cause mild bloating or laxative effect at very high doses. Safe for most individuals.",
                Description = "Seaweed-derived gelling agent used as a vegan gelatin substitute. Found in desserts, jellies, soups, ice cream, and as a thickener in baked goods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 148, ENumber = "E408", Name = "Furcellaran",
                AlternateNames = ["Danish agar", "Furcellaran gum"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.Unknown,
                EuRegulatoryStatus = EuRegulatoryStatus.NotAuthorized,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Similar to carrageenan but less studied. Limited current use.",
                Description = "Seaweed polysaccharide historically used as a gelling agent in dairy products, desserts, and puddings.",
                BannedInCountries = []
            },
            new()
            {
                Id = 149, ENumber = "E409", Name = "Arabinogalactan",
                AlternateNames = ["Larch gum", "Larch arabinogalactan"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Well-tolerated. May cause mild bloating at high doses. Some immune-stimulating effects documented.",
                Description = "Highly branched polysaccharide from Larix species. Used as a thickener, stabilizer, and emulsifier. Also sold as a dietary fiber supplement.",
                BannedInCountries = []
            },
            new()
            {
                Id = 150, ENumber = "E411", Name = "Oat Gum",
                AlternateNames = ["Oat beta-glucan"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Beneficial for heart health. May cause bloating in sensitive individuals. Safe for gluten-free diets when certified.",
                Description = "Soluble fiber extracted from oats. Used as a thickener and stabilizer. Known for beta-glucan content with heart health benefits.",
                BannedInCountries = []
            },
            new()
            {
                Id = 151, ENumber = "E416", Name = "Karaya Gum",
                AlternateNames = ["Gum karaya", "Sterculia gum", "Indian tragacanth"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May cause mild bloating or allergic reactions in sensitive individuals. Can act as a bulk-forming laxative at high doses.",
                Description = "Natural gum from Sterculia urens trees. Used as a thickener and stabilizer in salad dressings, ice cream, cheese spreads, and sauces.",
                BannedInCountries = []
            },
            new()
            {
                Id = 152, ENumber = "E419", Name = "Gum Ghatti",
                AlternateNames = ["Indian gum"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.NotAuthorized,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally considered safe. Limited modern toxicological data. May cause mild digestive upset at high doses.",
                Description = "Natural gum from Indian trees. Used as a thickener, stabilizer, and emulsifier in foods. Historically used as a substitute for gum arabic.",
                BannedInCountries = []
            },
            new()
            {
                Id = 153, ENumber = "E423", Name = "Octenyl Succinic Acid Modified Gum Arabic",
                AlternateNames = ["OSA modified gum arabic", "Modified acacia gum"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Similar to gum arabic - minimal gut effects. May cause mild gas at high doses.",
                Description = "Chemically modified form of gum arabic (E414) with improved emulsification properties. Used in beverage emulsions and flavor encapsulation.",
                BannedInCountries = []
            },
            new()
            {
                Id = 154, ENumber = "E425", Name = "Konjac",
                AlternateNames = ["Konjac gum", "Glucomannan", "Konnyaku", "Shirataki"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Choking/intestinal blockage risk with mini-cup jelly format (banned in EU, Australia). Dilated esophagus risk. Otherwise well-tolerated fiber. May reduce cholesterol.",
                Description = "Glucomannan polysaccharide from konjac root. Used as thickener, gelling agent in noodles (shirataki), and vegan seafood alternatives. Jelly candies pose choking risk.",
                BannedInCountries = ["EU (jelly candies)", "Australia (jelly candies)", "US (warning on jelly candies)"]
            },
            new()
            {
                Id = 155, ENumber = "E426", Name = "Soybean Hemicellulose",
                AlternateNames = ["Soybean polysaccharide", "Soy fiber"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May cause minor bloating. Safe for most individuals including those with soy allergies (highly processed, minimal protein).",
                Description = "Soluble dietary fiber from soybeans. Used as a thickener, stabilizer, and texturizer in various food products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 156, ENumber = "E427", Name = "Cassia Gum",
                AlternateNames = ["Cassia tora gum"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe at approved levels. May cause mild laxative effect at very high doses.",
                Description = "Seed gum from Cassia tora plants. Used as a thickener, stabilizer, and gelling agent in dairy products and desserts.",
                BannedInCountries = []
            },
            new()
            {
                Id = 157, ENumber = "E430", Name = "Polyoxyethylene (8) Stearate",
                AlternateNames = ["Polyoxyl 8 stearate"],
                Category = "Emulsifier", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.NotAuthorized,
                EuRegulatoryStatus = EuRegulatoryStatus.NotAuthorized,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Potential contamination with 1,4-dioxane (carcinogenic byproduct of ethoxylation). Toxicological data limited. Not commonly used in modern food production.",
                Description = "Synthetic emulsifier historically used in bakery products. Largely phased out due to manufacturing impurity concerns.",
                BannedInCountries = ["EU"]
            },
            new()
            {
                Id = 158, ENumber = "E431", Name = "Polyoxyethylene (40) Stearate",
                AlternateNames = ["Polyoxyl 40 stearate", "Myrj 52"],
                Category = "Emulsifier", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.NotAuthorized,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Potential contamination with 1,4-dioxane (carcinogen). Limited recent safety data. Some concerns about gut barrier effects.",
                Description = "Synthetic emulsifier used as a dough conditioner in bread and bakery products. Also used in pharmaceutical and cosmetic applications.",
                BannedInCountries = ["EU (as food additive)"]
            },
            new()
            {
                Id = 159, ENumber = "E442", Name = "Ammonium Phosphatides",
                AlternateNames = ["Emulsifier YN", "Mixed ammonium salts of phosphorylated glycerides"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Used in small quantities in chocolate. No significant health concerns at typical exposure levels.",
                Description = "Emulsifier used in chocolate and confectionery to reduce viscosity and improve flow properties. Alternative to soy lecithin.",
                BannedInCountries = []
            },
            new()
            {
                Id = 160, ENumber = "E444", Name = "Sucrose Acetate Isobutyrate",
                AlternateNames = ["SAIB", "Sucrose acetoisobutyrate"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe at food-use levels. Not metabolized to sucrose in significant amounts. Limited but adequate safety data.",
                Description = "Sucrose ester used as a density-adjusting agent in flavored beverages to keep citrus oils suspended. Also used in cosmetics.",
                BannedInCountries = []
            },
            new()
            {
                Id = 161, ENumber = "E445", Name = "Glycerol Esters of Wood Rosins",
                AlternateNames = ["GEWR", "Glyceryl abietate", "Ester gum"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Not absorbed to significant degree. Used at very low levels in beverages.",
                Description = "Oil-soluble emulsifier from pine rosin. Used to keep fruit oils suspended in soft drinks and as a chewing gum base.",
                BannedInCountries = []
            },
            new()
            {
                Id = 162, ENumber = "E459", Name = "Beta-Cyclodextrin",
                AlternateNames = ["Beta-cyclodextrin", "Cyclomaltoheptaose"],
                Category = "Stabilizer", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May reduce cholesterol and fat-soluble vitamin absorption. Well-tolerated in human studies.",
                Description = "Cyclic oligosaccharide that encapsulates sensitive ingredients. Used for flavor protection, cholesterol reduction in eggs, and bitter taste masking.",
                BannedInCountries = []
            },
            new()
            {
                Id = 163, ENumber = "E462", Name = "Ethyl Cellulose",
                AlternateNames = ["Ethylcellulose", "EC"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Very safe. Non-toxic, non-absorbable. Passes through digestive tract unchanged. Used in pharmaceutical coatings.",
                Description = "Ethyl ether of cellulose. Used as a coating for tablets and capsules, as a binder, and as a water-resistant thickener in foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 164, ENumber = "E465", Name = "Ethyl Methyl Cellulose",
                AlternateNames = ["Methylethyl cellulose"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Non-digestible. May cause mild laxative effect at very high doses like other cellulose derivatives.",
                Description = "Cellulose ether used as a thickener, stabilizer, and emulsifier in various food products. Provides viscosity and water-binding properties.",
                BannedInCountries = []
            },
            new()
            {
                Id = 165, ENumber = "E468", Name = "Crosslinked Sodium Carboxymethyl Cellulose",
                AlternateNames = ["Crosscarmellose", "Crosslinked CMC"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Non-digestible. Higher viscosity may affect nutrient absorption minimally. Well-tolerated.",
                Description = "Modified cellulose with crosslinking for enhanced thickening. Used in sauces, dairy products, and bakery fillings.",
                BannedInCountries = []
            },
            new()
            {
                Id = 166, ENumber = "E469", Name = "Enzymatically Hydrolysed Carboxymethyl Cellulose",
                AlternateNames = ["Enzymatically hydrolysed CMC"],
                Category = "Thickener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Similar safety profile to CMC.",
                Description = "CMC treated with cellulase enzymes to reduce molecular weight. Used in products requiring specific texture and stability properties.",
                BannedInCountries = []
            },
            new()
            {
                Id = 167, ENumber = "E470a", Name = "Sodium, Potassium and Calcium Salts of Fatty Acids",
                AlternateNames = ["Fatty acid salts", "Soaps"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. These are essentially purified soaps of food-grade fatty acids. No significant health concerns.",
                Description = "Salts of fatty acids used as emulsifiers, stabilizers, and anti-caking agents in various processed foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 168, ENumber = "E470b", Name = "Magnesium Salts of Fatty Acids",
                AlternateNames = ["Magnesium stearate", "Magnesium soaps"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Provides small amount of dietary magnesium. Fatty acid component metabolized normally.",
                Description = "Magnesium salts of fatty acids used as emulsifiers, stabilizers, and anti-caking agents in food and supplement tablets.",
                BannedInCountries = []
            },
            new()
            {
                Id = 169, ENumber = "E472a", Name = "Acetic Acid Esters of Mono- and Diglycerides",
                AlternateNames = ["Acetem", "Acetylated mono- and diglycerides"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Digested to fatty acids, glycerol, and acetic acid - all normal dietary components.",
                Description = "Emulsifier made by esterifying mono- and diglycerides with acetic acid. Used in shortenings, cakes, and whipped toppings.",
                BannedInCountries = []
            },
            new()
            {
                Id = 170, ENumber = "E472b", Name = "Lactic Acid Esters of Mono- and Diglycerides",
                AlternateNames = ["Lactem", "Lactylated mono- and diglycerides"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Lactic acid is a normal gut metabolite. No specific health concerns at food-use levels.",
                Description = "Emulsifier produced by esterifying mono- and diglycerides with lactic acid. Used in bakery, dairy, and confectionery products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 171, ENumber = "E472c", Name = "Citric Acid Esters of Mono- and Diglycerides",
                AlternateNames = ["Citrem", "Citrated mono- and diglycerides"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Citric acid is a normal component of the TCA cycle. No specific health concerns.",
                Description = "Emulsifier and antioxidant synergist made from mono- and diglycerides esterified with citric acid. Used in fats, oils, and margarine.",
                BannedInCountries = []
            },
            new()
            {
                Id = 172, ENumber = "E472d", Name = "Tartaric Acid Esters of Mono- and Diglycerides",
                AlternateNames = ["Tartem", "Tartrated mono- and diglycerides"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Tartaric acid is naturally present in grapes and wine. No specific health concerns.",
                Description = "Emulsifier from mono- and diglycerides esterified with tartaric acid. Used in bakery products and margarine.",
                BannedInCountries = []
            },
            new()
            {
                Id = 173, ENumber = "E472f", Name = "Mixed Acetic and Tartaric Acid Esters of Mono- and Diglycerides",
                AlternateNames = ["MATEM", "Mixed acetic and tartaric acid esters"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Combination of E472a and E472d. No specific safety concerns.",
                Description = "Mixed ester emulsifier with both acetate and tartrate groups on mono- and diglycerides. Used in bakery products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 174, ENumber = "E477", Name = "Propane-1,2-diol Esters of Fatty Acids",
                AlternateNames = ["Propylene glycol esters of fatty acids", "PGMS"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Propylene glycol is metabolized to lactic acid in the body. Large doses may cause mild osmotic effects.",
                Description = "Emulsifier made from propylene glycol and fatty acids. Used as a dough conditioner and in whipped products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 175, ENumber = "E478", Name = "Lactylated Fatty Acid Esters of Glycerol and Propane-1,2-diol",
                AlternateNames = ["Lactylated propylene glycol esters"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Limited recent use. Combined safety profile of lactylated and propylene glycol esters.",
                Description = "Complex emulsifier combining glycerol and propylene glycol backbones with lactic and fatty acid esters.",
                BannedInCountries = []
            },
            new()
            {
                Id = 176, ENumber = "E479b", Name = "Thermally Oxidised Soya Bean Oil Interacted with Mono- and Diglycerides",
                AlternateNames = ["TOSOM", "Oxidised soybean oil"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Limited toxicological data. Thermal oxidation may produce potentially harmful compounds. Used at very low levels (release agent). Some concerns about oxidized fat absorption.",
                Description = "Specially processed soybean oil used primarily as a release agent in baking and as an emulsifier. Limited applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 177, ENumber = "E483", Name = "Stearyl Tartrate",
                AlternateNames = ["Stearyl tartaric acid esters"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Stearyl alcohol is a normal long-chain alcohol. Tartaric acid is safe at food-use levels.",
                Description = "Emulsifier used in bakery products as a dough conditioner and crumb softener. Improves loaf volume.",
                BannedInCountries = []
            },
            new()
            {
                Id = 178, ENumber = "E497", Name = "Stigmasterol-Rich Plant Sterols",
                AlternateNames = ["Plant sterols", "Phytosterols", "Stigmasterol"],
                Category = "Stabilizer", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May reduce fat-soluble vitamin absorption slightly. Beneficial effect on LDL cholesterol. Avoid in sitosterolemia.",
                Description = "Plant-derived sterols added to functional foods for cholesterol-lowering effects. Found in spreads, dairy products, and supplements.",
                BannedInCountries = []
            },
            new()
            {
                Id = 179, ENumber = "E499", Name = "Stigmasterol-Rich Plant Sterols",
                AlternateNames = ["Plant sterol esters"],
                Category = "Stabilizer", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Same as E497 - generally safe with small effect on fat-soluble vitamin absorption. Beneficial for cholesterol management.",
                Description = "Concentrated plant sterol preparation used in functional foods for cardiovascular health benefits.",
                BannedInCountries = []
            },
            new()
            {
                Id = 180, ENumber = "E542", Name = "Bone Phosphate",
                AlternateNames = ["Edible bone phosphate", "Calcium phosphate"],
                Category = "Anti-caking Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Provides calcium and phosphorus. May be a concern for individuals watching phosphate intake (kidney disease).",
                Description = "Mineral powder from processed animal bones. Used as an anti-caking agent in powdered foods and as a calcium supplement.",
                BannedInCountries = []
            },
            new()
            {
                Id = 181, ENumber = "E552", Name = "Calcium Silicate",
                AlternateNames = ["Calcium monosilicate"],
                Category = "Anti-caking Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Provides small amount of calcium. Silicate component is not significantly absorbed.",
                Description = "Mineral anti-caking agent used in table salt, baking powder, and powdered foods to prevent moisture absorption and clumping.",
                BannedInCountries = []
            },
            new()
            {
                Id = 182, ENumber = "E553a", Name = "Magnesium Silicate / Magnesium Trisilicate",
                AlternateNames = ["Magnesium trisilicate", "Talc (magnesium silicate)"],
                Category = "Anti-caking Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Used medicinally as an antacid. May adsorb some nutrients at very high doses.",
                Description = "Mineral anti-caking agent. Also used as a pharmaceutical antacid. Found in powdered foods and supplements.",
                BannedInCountries = []
            },
            new()
            {
                Id = 183, ENumber = "E553b", Name = "Talc",
                AlternateNames = ["Magnesium silicate hydroxide", "Talcum"],
                Category = "Anti-caking Agent", CspiRating = CspiRating.Avoid,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Controversial. Asbestos-free food-grade talc is considered safe by FDA. IARC classifies as possibly carcinogenic via inhalation. Oral ingestion considered low risk.",
                Description = "Soft mineral used as anti-caking agent, polishing agent for rice, and coating for confectionery. Food-grade must be asbestos-free.",
                BannedInCountries = []
            },
            new()
            {
                Id = 184, ENumber = "E554", Name = "Sodium Aluminium Silicate",
                AlternateNames = ["Sodium aluminosilicate", "Aluminium sodium silicate"],
                Category = "Anti-caking Agent", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Aluminium content is a concern - potential neurotoxicity with chronic high exposure. Especially problematic for individuals with impaired kidney function. EU restricts aluminium-containing additives.",
                Description = "Synthetic aluminosilicate used as an anti-caking agent. Found in table salt, dried milk, and powdered foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 185, ENumber = "E555", Name = "Potassium Aluminium Silicate",
                AlternateNames = ["Potassium aluminosilicate", "Microcline"],
                Category = "Anti-caking Agent", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Aluminium bioavailability concern. EU has stricter limits on aluminium-containing additives. Avoid for individuals with kidney disease.",
                Description = "Aluminium-containing mineral used as an anti-caking agent in powdered foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 186, ENumber = "E556", Name = "Calcium Aluminium Silicate",
                AlternateNames = ["Calcium aluminosilicate"],
                Category = "Anti-caking Agent", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Aluminium-related concerns. May also provide calcium. EU restricts aluminium-based additives.",
                Description = "Aluminium-containing anti-caking agent used in powdered foods and baking products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 187, ENumber = "E558", Name = "Bentonite",
                AlternateNames = ["Montmorillonite", "Bentonite clay"],
                Category = "Anti-caking Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May adsorb beneficial nutrients if consumed excessively. Used pharmaceutically to treat diarrhea. May cause constipation.",
                Description = "Natural clay used as an anti-caking agent, in wine clarification, and as a suspension stabilizer. Also used in natural health products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 188, ENumber = "E559", Name = "Aluminium Silicate (Kaolin)",
                AlternateNames = ["Kaolin", "China clay", "Hydrated aluminium silicate"],
                Category = "Anti-caking Agent", CspiRating = CspiRating.Caution,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Unknown,
                SafetyRating = SafetyRating.Warning,
                HealthConcerns = "Aluminium content. Used pharmaceutically as anti-diarrheal (Kaopectate). Aluminium bioavailability concern. EU restricts use.",
                Description = "Fine white clay used as anti-caking agent and coating for confectionery. Also used in pharmaceutical anti-diarrheal preparations.",
                BannedInCountries = []
            },
            new()
            {
                Id = 189, ENumber = "E576", Name = "Sodium Gluconate",
                AlternateNames = ["Sodium salt of gluconic acid"],
                Category = "Sequestrant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Gluconate is a normal metabolite. May contribute small amount of sodium.",
                Description = "Sequestering agent used to bind metal ions in foods. Prevents discoloration and rancidity. Also used in cleaning products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 190, ENumber = "E577", Name = "Potassium Gluconate",
                AlternateNames = ["Potassium salt of gluconic acid"],
                Category = "Sequestrant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Provides potassium which may benefit blood pressure. Caution for individuals with kidney disease or on potassium-sparing diuretics.",
                Description = "Potassium supplement and sequestrant. Used in foods as a mineral fortificant and acidity regulator.",
                BannedInCountries = []
            },
            new()
            {
                Id = 191, ENumber = "E578", Name = "Calcium Gluconate",
                AlternateNames = ["Calcium salt of gluconic acid"],
                Category = "Sequestrant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Good source of bioavailable calcium. Very high doses may cause constipation or kidney stone risk in susceptible individuals.",
                Description = "Calcium supplement and food additive used as a sequestrant, firming agent, and acidity regulator. Found in fortified foods.",
                BannedInCountries = []
            },
            new()
            {
                Id = 192, ENumber = "E579", Name = "Ferrous Gluconate",
                AlternateNames = ["Iron(II) gluconate"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Provides dietary iron. May cause GI irritation at high doses. Iron overload concern for individuals with hemochromatosis.",
                Description = "Used to stabilize the black color of olives (ferrous gluconate oxidizes to black ferric complex). Also used as an iron supplement.",
                BannedInCountries = []
            },
            new()
            {
                Id = 193, ENumber = "E585", Name = "Ferrous Lactate",
                AlternateNames = ["Iron(II) lactate"],
                Category = "Colorant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May cause GI irritation at high doses. Risk of iron overload in susceptible individuals. Constipation possible.",
                Description = "Iron compound used to stabilize black color in olives and as a dietary iron supplement.",
                BannedInCountries = []
            },
            new()
            {
                Id = 194, ENumber = "E586", Name = "4-Hexylresorcinol",
                AlternateNames = ["4-hexyl-1,3-benzenediol"],
                Category = "Antioxidant", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe at food-use levels. Used as an antiseptic in throat lozenges. May cause mild skin irritation in sensitive individuals.",
                Description = "Antioxidant used to prevent melanosis (black spotting) in shrimp and other crustaceans. Also used as an antiseptic in throat lozenges.",
                BannedInCountries = []
            },
            new()
            {
                Id = 195, ENumber = "E620", Name = "Glutamic Acid",
                AlternateNames = ["L-glutamic acid", "Glutamate"],
                Category = "Flavor Enhancer", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. 'Chinese restaurant syndrome' is not supported by robust evidence. Some sensitive individuals report headaches. No proven gut toxicity.",
                Description = "Naturally occurring amino acid used as a flavor enhancer providing umami taste. Found in tomatoes, cheese, and many foods naturally.",
                BannedInCountries = []
            },
            new()
            {
                Id = 196, ENumber = "E650", Name = "Zinc Acetate",
                AlternateNames = ["Zinc diacetate"],
                Category = "Flavor Enhancer", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Provides essential zinc. Very high doses may cause nausea and copper deficiency. Used pharmaceutically for Wilson's disease and cold lozenges.",
                Description = "Zinc compound used as a flavor enhancer in chewing gum and as a dietary zinc source. Also used in pharmaceutical lozenges.",
                BannedInCountries = []
            },
            new()
            {
                Id = 197, ENumber = "E900", Name = "Dimethylpolysiloxane",
                AlternateNames = ["PDMS", "Dimethicone", "Polydimethylsiloxane"],
                Category = "Anti-foaming Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Very safe at food-use levels. Not absorbed, passes through GI tract unchanged. EFSA recommended limiting cyclopolysiloxane impurities from manufacturing.",
                Description = "Silicone polymer used as an anti-foaming agent in cooking oils, fried foods, beverages, and as a processing aid.",
                BannedInCountries = []
            },
            new()
            {
                Id = 198, ENumber = "E901", Name = "Beeswax, White and Yellow",
                AlternateNames = ["Cera alba", "E901"],
                Category = "Glazing Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Non-digestible. Very rare allergic reactions. Not suitable for strict vegans.",
                Description = "Natural wax from honeybees. Used as a protective glaze for candies, chocolate, and pharmaceutical tablets.",
                BannedInCountries = []
            },
            new()
            {
                Id = 199, ENumber = "E902", Name = "Candelilla Wax",
                AlternateNames = ["Candelilla wax", "E902"],
                Category = "Glazing Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Non-digestible. Safe for vegans.",
                Description = "Plant wax from candelilla shrubs. Used as a glazing agent for candies, citrus fruits, and as a chewing gum base.",
                BannedInCountries = []
            },
            new()
            {
                Id = 200, ENumber = "E903", Name = "Carnauba Wax",
                AlternateNames = ["Brazil wax", "Carnauba palm wax"],
                Category = "Glazing Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Non-digestible. Very rare allergic reactions. Considered one of the safest food additives.",
                Description = "Hard natural wax used as a glazing and polishing agent for candies, chocolate, tablets, and fresh fruits.",
                BannedInCountries = []
            },
            new()
            {
                Id = 201, ENumber = "E904", Name = "Shellac",
                AlternateNames = ["Confectioner's glaze", "Lac resin", "Pharmaceutical glaze"],
                Category = "Glazing Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. May cause rare allergic reactions. Not suitable for vegans. Used as pharmaceutical coating.",
                Description = "Natural resin secreted by lac bugs. Used as a shiny coating on candies (jelly beans), chocolate, pills, and citrus fruit.",
                BannedInCountries = []
            },
            new()
            {
                Id = 202, ENumber = "E905", Name = "Microcrystalline Wax",
                AlternateNames = ["Microcrystalline wax", "Petroleum wax", "Paraffin wax (food grade)"],
                Category = "Glazing Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Non-digestible. Petroleum origin may concern some consumers. No known toxicity at food-use levels.",
                Description = "Refined petroleum wax used as a protective coating for cheese, as a glazing agent and as chewing gum base.",
                BannedInCountries = []
            },
            new()
            {
                Id = 203, ENumber = "E907", Name = "Hydrogenated Poly-1-Decene",
                AlternateNames = ["Hydrogenated polydecene", "Polydecene"],
                Category = "Glazing Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Limited toxicological data. Not absorbed. Used at very low levels.",
                Description = "Synthetic hydrocarbon used as a glazing and release agent in food processing.",
                BannedInCountries = []
            },
            new()
            {
                Id = 204, ENumber = "E914", Name = "Oxidised Polyethylene Wax",
                AlternateNames = ["Oxidized polyethylene", "OPE"],
                Category = "Glazing Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Limited absorption. Low toxicity profile.",
                Description = "Synthetic wax used as a glazing agent on fruits and vegetables and as a release agent.",
                BannedInCountries = []
            },
            new()
            {
                Id = 205, ENumber = "E920", Name = "L-Cysteine",
                AlternateNames = ["Cysteine", "L-cysteine hydrochloride"],
                Category = "Flour Treatment Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Traditionally sourced from human hair or duck feathers (some vegetarian/religious concerns). Synthetic versions available. No significant health concerns.",
                Description = "Amino acid used as a flour treatment agent to improve dough handling and reduce mixing time. Also used in flavorings.",
                BannedInCountries = []
            },
            new()
            {
                Id = 206, ENumber = "E927b", Name = "Carbamide (Urea)",
                AlternateNames = ["Urea", "Carbamide"],
                Category = "Flour Treatment Agent", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe at food-use levels. Urea is a normal human metabolite.",
                Description = "Nitrogen compound used as a flour treatment agent and as a nutrient for yeast fermentation in baking.",
                BannedInCountries = []
            },
            new()
            {
                Id = 207, ENumber = "E938", Name = "Argon",
                AlternateNames = ["Argon gas"],
                Category = "Packaging Gas", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Completely safe. Inert gas that does not interact with the body. No toxicological concerns.",
                Description = "Inert gas used in food packaging to prevent oxidation and spoilage. Also used as a propellant.",
                BannedInCountries = []
            },
            new()
            {
                Id = 208, ENumber = "E939", Name = "Helium",
                AlternateNames = ["Helium gas"],
                Category = "Packaging Gas", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Completely safe. Inert gas. No known health concerns.",
                Description = "Inert gas used in food packaging and as a propellant. Also used in leak detection.",
                BannedInCountries = []
            },
            new()
            {
                Id = 209, ENumber = "E941", Name = "Nitrogen",
                AlternateNames = ["Nitrogen gas", "N2"],
                Category = "Packaging Gas", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Completely safe at food-use levels. Major component of air we breathe.",
                Description = "Used in modified atmosphere packaging to preserve freshness. Used as a propellant in whipped cream and beer dispensing.",
                BannedInCountries = []
            },
            new()
            {
                Id = 210, ENumber = "E942", Name = "Nitrous Oxide",
                AlternateNames = ["N2O", "Laughing gas"],
                Category = "Packaging Gas", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Safe in food use as propellant. Abuse as inhalant can cause vitamin B12 deficiency and neurological damage. Banned in some countries for non-food uses.",
                Description = "Gas used as a propellant in aerosol whipped cream and culinary foams. Also used for its anesthetic properties.",
                BannedInCountries = []
            },
            new()
            {
                Id = 211, ENumber = "E943a", Name = "Butane",
                AlternateNames = ["n-Butane"],
                Category = "Packaging Gas", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Safe as food propellant at trace levels. Highly flammable.",
                Description = "Hydrocarbon propellant used in aerosol food products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 212, ENumber = "E943b", Name = "Isobutane",
                AlternateNames = ["2-Methylpropane", "i-Butane"],
                Category = "Packaging Gas", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Safe as food propellant. Highly flammable.",
                Description = "Hydrocarbon propellant used in cooking sprays and aerosol food products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 213, ENumber = "E944", Name = "Propane",
                AlternateNames = ["Propane gas"],
                Category = "Packaging Gas", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Safe as food propellant at trace levels. Highly flammable.",
                Description = "Hydrocarbon propellant used in cooking sprays and aerosol food applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 214, ENumber = "E948", Name = "Oxygen",
                AlternateNames = ["O2", "Oxygen gas"],
                Category = "Packaging Gas", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Completely safe. Essential for life.",
                Description = "Used in food packaging to maintain color of fresh meat and for aerating some products.",
                BannedInCountries = []
            },
            new()
            {
                Id = 215, ENumber = "E949", Name = "Hydrogen",
                AlternateNames = ["H2", "Hydrogen gas"],
                Category = "Packaging Gas", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Safe. Some research shows potential anti-inflammatory effects of molecular hydrogen in the gut.",
                Description = "Gas used in modified atmosphere packaging to extend shelf life. Also used in some therapeutic applications.",
                BannedInCountries = []
            },
            new()
            {
                Id = 216, ENumber = "E957", Name = "Thaumatin",
                AlternateNames = ["Talin", "Thaumatin"],
                Category = "Sweetener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Natural protein. May cause rare allergic reactions. No caloric contribution.",
                Description = "Natural sweet protein extracted from West African katemfe fruit. Used as a sweetener and flavor modifier.",
                BannedInCountries = []
            },
            new()
            {
                Id = 217, ENumber = "E959", Name = "Neohesperidin DC",
                AlternateNames = ["Neohesperidin dihydrochalcone", "Neo-DHC"],
                Category = "Sweetener", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Limited but adequate safety data. Also found naturally in citrus peels.",
                Description = "Intense sweetener with delayed sweetness onset. Used in chewing gum, toothpaste, and pharmaceuticals.",
                BannedInCountries = []
            },
            new()
            {
                Id = 218, ENumber = "E999", Name = "Quillaia Extract",
                AlternateNames = ["Quillaja extract", "Soapbark extract", "Quillaia"],
                Category = "Emulsifier", CspiRating = CspiRating.Safe,
                UsRegulatoryStatus = UsRegulatoryStatus.GRAS,
                EuRegulatoryStatus = EuRegulatoryStatus.Approved,
                SafetyRating = SafetyRating.Caution,
                HealthConcerns = "Generally safe. Saponins can cause mild GI irritation at very high doses. May increase gut permeability in vitro but not at food-use levels.",
                Description = "Natural saponin extract from soapbark tree. Used as a foaming agent in beverages (root beer), and as an emulsifier in food and cosmetics.",
                BannedInCountries = []
            },        };

        foreach (var additive in additives)
        {
            // These are category-level authoritative references; individual claims
            // should not be presented as regulator conclusions without a claim-specific citation.
            additive.EvidenceSources = additive.Category switch
            {
                "Color" =>
                [
                    "https://www.fda.gov/food/color-additives-specific-purpose",
                    "https://www.efsa.europa.eu/en/topics/topic/food-additives"
                ],
                "Preservative" =>
                [
                    "https://www.fda.gov/food/food-ingredients-packaging/food-additives-petitions",
                    "https://www.efsa.europa.eu/en/topics/topic/food-additives"
                ],
                _ =>
                [
                    "https://www.fda.gov/food/food-ingredients-packaging/food-additives-petitions",
                    "https://www.efsa.europa.eu/en/topics/topic/food-additives"
                ]
            };
        }

        foreach (var a in additives)
        {
            if (existingEnumbers.Contains(a.ENumber))
            {
                var stored = existing.FirstOrDefault(x =>
                    string.Equals(x.ENumber, a.ENumber, StringComparison.OrdinalIgnoreCase));
                if (stored is not null)
                {
                    stored.EvidenceSources = a.EvidenceSources;
                    await store.UpsertFoodAdditiveAsync(stored);
                }
                continue;
            }

            await store.UpsertFoodAdditiveAsync(a);
        }
    }
}
