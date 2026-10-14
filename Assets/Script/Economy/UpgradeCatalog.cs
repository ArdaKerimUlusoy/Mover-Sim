/// <summary>Garaj mağazasındaki upgrade'ler. Ağdan numara olarak gönderilir; sona ekleyin.</summary>
public enum UpgradeType : byte
{
    BiggerBag = 0,    // kişisel: +5 kg çanta
    QuietShoes = 1,   // KALDIRILDI (mağazada yok). Numara ağda kullanıldığı için silinmedi.
    SwiftGloves = 2,  // kişisel: eser değiştirme daha hızlı
    TeamBackpacks = 3, // ekip: herkese +3 kg
    Lockpick = 4      // kişisel: maymuncuk (Staff Only kapısını açar). Adetle alınır, kırılabilir; seviye = elindeki adet
}

public enum PaidFrom : byte { Personal, Team }

/// <summary>Upgrade fiyatları ve etkileri tek yerde (denge ayarı buradan yapılır).</summary>
public static class UpgradeCatalog
{
    public static readonly UpgradeType[] All =
    {
        UpgradeType.Lockpick, UpgradeType.BiggerBag, UpgradeType.SwiftGloves, UpgradeType.TeamBackpacks // ayak sesi upgrade'i kaldırıldı
    };

    // Etkiler
    public const float BagKgPerLevel = 5f;
    public const float TeamBagKgPerLevel = 3f;
    public const float ShoesQuietPerLevel = 0.3f;   // ayak sesi %30 kısılır
    public const float GlovesFasterPerLevel = 0.2f; // değiştirme %20 hızlanır
    public const int LockpickPrice = 50;             // bir maymuncuğun fiyatı (kişisel cüzdandan)
    public const int LockpickMax = 5;                // aynı anda en fazla kaç maymuncuk taşınır
    public const float LockpickBreakChance = 0.35f;  // her denemede kırılma ihtimali (başarı %65)
    public const float LockpickSeconds = 4f;         // bir denemenin ortalama süresi (E basılı)

    public static int MaxLevel(UpgradeType t)
    {
        switch (t)
        {
            case UpgradeType.BiggerBag: return 3;
            case UpgradeType.QuietShoes: return 0; // kaldırıldı: satın alınamaz
            case UpgradeType.SwiftGloves: return 2;
            case UpgradeType.TeamBackpacks: return 2;
            case UpgradeType.Lockpick: return LockpickMax; // adet
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
            // Ucuz: 1. görevde tek bir tablo satan oyuncunun payı (~$100) iki maymuncuğa yeter. Staff Only eseri 2. görevde açılır.
            case UpgradeType.Lockpick: return currentLevel >= 0 && currentLevel < LockpickMax ? LockpickPrice : int.MaxValue; // her adet aynı fiyat
            default: return int.MaxValue;
        }
        return currentLevel >= 0 && currentLevel < prices.Length ? prices[currentLevel] : int.MaxValue;
    }

    public static PaidFrom Payer(UpgradeType t) => t == UpgradeType.TeamBackpacks ? PaidFrom.Team : PaidFrom.Personal;

    public static string NameKey(UpgradeType t) => "upg." + t.ToString().ToLowerInvariant() + ".name";
    public static string DescKey(UpgradeType t) => "upg." + t.ToString().ToLowerInvariant() + ".desc";
}
