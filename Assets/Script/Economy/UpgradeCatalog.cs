/// <summary>Garaj mağazasındaki upgrade'ler. Ağdan numara olarak gönderilir; sona ekleyin.</summary>
public enum UpgradeType : byte
{
    BiggerBag = 0,    // kişisel: +5 kg çanta
    QuietShoes = 1,   // kişisel: daha sessiz ayak sesi
    SwiftGloves = 2,  // kişisel: eser değiştirme daha hızlı
    TeamBackpacks = 3 // ekip: herkese +3 kg
}

public enum PaidFrom : byte { Personal, Team }

/// <summary>Upgrade fiyatları ve etkileri tek yerde (denge ayarı buradan yapılır).</summary>
public static class UpgradeCatalog
{
    public static readonly UpgradeType[] All =
    {
        UpgradeType.BiggerBag, UpgradeType.QuietShoes, UpgradeType.SwiftGloves, UpgradeType.TeamBackpacks
    };

    // Etkiler
    public const float BagKgPerLevel = 5f;
    public const float TeamBagKgPerLevel = 3f;
    public const float ShoesQuietPerLevel = 0.3f;   // ayak sesi %30 kısılır
    public const float GlovesFasterPerLevel = 0.2f; // değiştirme %20 hızlanır

    public static int MaxLevel(UpgradeType t)
    {
        switch (t)
        {
            case UpgradeType.BiggerBag: return 3;
            case UpgradeType.QuietShoes: return 2;
            case UpgradeType.SwiftGloves: return 2;
            case UpgradeType.TeamBackpacks: return 2;
            default: return 0;
        }
    }

    /// <summary>Bir sonraki seviyenin fiyatı (currentLevel = şu anki seviye).</summary>
    public static int Price(UpgradeType t, int currentLevel)
    {
        int[] prices;
        switch (t)
        {
            case UpgradeType.BiggerBag: prices = new[] { 150, 300, 500 }; break;
            case UpgradeType.QuietShoes: prices = new[] { 120, 260 }; break;
            case UpgradeType.SwiftGloves: prices = new[] { 200, 420 }; break;
            case UpgradeType.TeamBackpacks: prices = new[] { 400, 800 }; break;
            default: return int.MaxValue;
        }
        return currentLevel >= 0 && currentLevel < prices.Length ? prices[currentLevel] : int.MaxValue;
    }

    public static PaidFrom Payer(UpgradeType t) => t == UpgradeType.TeamBackpacks ? PaidFrom.Team : PaidFrom.Personal;

    public static string NameKey(UpgradeType t) => "upg." + t.ToString().ToLowerInvariant() + ".name";
    public static string DescKey(UpgradeType t) => "upg." + t.ToString().ToLowerInvariant() + ".desc";
}
