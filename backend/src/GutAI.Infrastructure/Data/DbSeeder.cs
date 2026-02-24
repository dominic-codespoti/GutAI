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
        if (existing.Count > 0) return;

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
        };

        foreach (var a in additives)
            await store.UpsertFoodAdditiveAsync(a);
    }
}
