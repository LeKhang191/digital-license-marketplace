using LicenseVault.Api.Application.Interfaces;

namespace LicenseVault.Api.Infrastructure.Kinguin;

// Fixed demo prices and stock, not live Steam or Kinguin offers.
// Derived from datasets/demo/steam-games.csv at a simulated 70% cost and 30,000 VND/EUR.
internal static class MockGameCatalog
{
    private static readonly Dictionary<string, SupplierQuote> Games = new(StringComparer.Ordinal)
    {
        ["mock-steam-1091500"] = new("mock-steam-1091500", 1091500, 8.17m, 20, 20, 20),
        ["mock-steam-1174180"] = new("mock-steam-1174180", 1174180, 10.50m, 27, 27, 27),
        ["mock-steam-782330"] = new("mock-steam-782330", 782330, 5.83m, 34, 34, 34),
        ["mock-steam-379720"] = new("mock-steam-379720", 379720, 4.20m, 41, 41, 41),
        ["mock-steam-1030830"] = new("mock-steam-1030830", 1030830, 5.13m, 48, 48, 48),
        ["mock-steam-412020"] = new("mock-steam-412020", 412020, 5.60m, 55, 55, 55),
        ["mock-steam-814380"] = new("mock-steam-814380", 814380, 10.50m, 62, 62, 62),
        ["mock-steam-1237970"] = new("mock-steam-1237970", 1237970, 2.33m, 69, 69, 69),
        ["mock-steam-2000950"] = new("mock-steam-2000950", 2000950, 10.50m, 76, 76, 76),
        ["mock-steam-1593500"] = new("mock-steam-1593500", 1593500, 10.50m, 22, 22, 22),
        ["mock-steam-203160"] = new("mock-steam-203160", 203160, 2.80m, 29, 29, 29),
        ["mock-steam-391220"] = new("mock-steam-391220", 391220, 4.20m, 36, 36, 36),
        ["mock-steam-750920"] = new("mock-steam-750920", 750920, 5.83m, 43, 43, 43),
        ["mock-steam-620"] = new("mock-steam-620", 620, 1.17m, 50, 50, 50),
        ["mock-steam-220"] = new("mock-steam-220", 220, 1.17m, 57, 57, 57),
        ["mock-steam-400"] = new("mock-steam-400", 400, 1.17m, 64, 64, 64),
        ["mock-steam-383870"] = new("mock-steam-383870", 383870, 2.80m, 71, 71, 71),
        ["mock-steam-242760"] = new("mock-steam-242760", 242760, 3.50m, 78, 78, 78),
        ["mock-steam-892970"] = new("mock-steam-892970", 892970, 5.13m, 24, 24, 24),
        ["mock-steam-264710"] = new("mock-steam-264710", 264710, 4.20m, 31, 31, 31),
        ["mock-steam-1245620"] = new("mock-steam-1245620", 1245620, 12.83m, 0, 0, 0),
        ["mock-steam-1086940"] = new("mock-steam-1086940", 1086940, 12.83m, 45, 45, 45),
        ["mock-steam-292030"] = new("mock-steam-292030", 292030, 3.50m, 52, 52, 52),
        ["mock-steam-489830"] = new("mock-steam-489830", 489830, 5.13m, 59, 59, 59),
        ["mock-steam-377160"] = new("mock-steam-377160", 377160, 4.20m, 66, 66, 66),
        ["mock-steam-435150"] = new("mock-steam-435150", 435150, 5.83m, 73, 73, 73),
        ["mock-steam-367520"] = new("mock-steam-367520", 367520, 2.80m, 80, 80, 80),
        ["mock-steam-108600"] = new("mock-steam-108600", 108600, 5.83m, 26, 26, 26),
        ["mock-steam-632470"] = new("mock-steam-632470", 632470, 4.20m, 33, 33, 33),
        ["mock-steam-1096530"] = new("mock-steam-1096530", 1096530, 10.50m, 40, 40, 40),
        ["mock-steam-1551360"] = new("mock-steam-1551360", 1551360, 10.50m, 47, 47, 47),
        ["mock-steam-805550"] = new("mock-steam-805550", 805550, 5.83m, 54, 54, 54),
        ["mock-steam-244210"] = new("mock-steam-244210", 244210, 2.80m, 61, 61, 61),
        ["mock-steam-228380"] = new("mock-steam-228380", 228380, 4.20m, 68, 68, 68),
        ["mock-steam-1066890"] = new("mock-steam-1066890", 1066890, 8.17m, 75, 75, 75),
        ["mock-steam-227300"] = new("mock-steam-227300", 227300, 3.50m, 21, 21, 21),
        ["mock-steam-270880"] = new("mock-steam-270880", 270880, 3.50m, 28, 28, 28),
        ["mock-steam-1248130"] = new("mock-steam-1248130", 1248130, 5.83m, 35, 35, 35),
        ["mock-steam-621060"] = new("mock-steam-621060", 621060, 2.80m, 42, 42, 42),
        ["mock-steam-413150"] = new("mock-steam-413150", 413150, 2.33m, 49, 49, 49),
        ["mock-steam-289070"] = new("mock-steam-289070", 289070, 4.20m, 56, 56, 56),
        ["mock-steam-281990"] = new("mock-steam-281990", 281990, 5.83m, 63, 63, 63),
        ["mock-steam-394360"] = new("mock-steam-394360", 394360, 5.83m, 70, 70, 70),
        ["mock-steam-236850"] = new("mock-steam-236850", 236850, 5.13m, 77, 77, 77),
        ["mock-steam-294100"] = new("mock-steam-294100", 294100, 6.53m, 23, 23, 23),
        ["mock-steam-2050650"] = new("mock-steam-2050650", 2050650, 10.50m, 30, 30, 30),
        ["mock-steam-883710"] = new("mock-steam-883710", 883710, 5.83m, 37, 37, 37),
        ["mock-steam-952060"] = new("mock-steam-952060", 952060, 5.83m, 44, 44, 44),
        ["mock-steam-1196590"] = new("mock-steam-1196590", 1196590, 8.17m, 51, 51, 51),
        ["mock-steam-418370"] = new("mock-steam-418370", 418370, 4.20m, 58, 58, 58),
        ["mock-steam-1259970"] = new("mock-steam-1259970", 1259970, 2.33m, 65, 65, 65),
        ["mock-steam-1465360"] = new("mock-steam-1465360", 1465360, 5.13m, 72, 72, 72),
        ["mock-steam-1961050"] = new("mock-steam-1961050", 1961050, 5.83m, 79, 79, 79),
        ["mock-steam-1677350"] = new("mock-steam-1677350", 1677350, 8.17m, 25, 25, 25),
        ["mock-steam-1520310"] = new("mock-steam-1520310", 1520310, 5.13m, 32, 32, 32),
        ["mock-steam-1145360"] = new("mock-steam-1145360", 1145360, 4.20m, 39, 39, 39),
        ["mock-steam-504230"] = new("mock-steam-504230", 504230, 2.80m, 46, 46, 46),
        ["mock-steam-268910"] = new("mock-steam-268910", 268910, 3.50m, 53, 53, 53),
        ["mock-steam-588650"] = new("mock-steam-588650", 588650, 4.20m, 60, 60, 60),
        ["mock-steam-105600"] = new("mock-steam-105600", 105600, 1.87m, 67, 67, 67),
    };

    public static SupplierQuote? Find(string id) => Games.GetValueOrDefault(id);
    public static string? ForSlug(string? slug)
    {
        if (slug is null) return null;
        var id = "mock-" + slug;
        return Games.ContainsKey(id) ? id : null;
    }
}
