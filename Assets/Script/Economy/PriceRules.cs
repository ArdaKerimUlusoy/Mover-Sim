using UnityEngine;

/// <summary>
/// Karaborsa fiyat kuralları (GDD Bölüm 4 ve 7):
///   Satış fiyatı = taban değer × benzerlik çarpanı × kaide çarpanı (× ileride aranma çarpanı)
/// Orijinal eserin "benzerlik" değeri, müzede YERİNE BIRAKILAN sahtenin benzerliğidir:
/// sahte ne kadar iyiyse orijinal o kadar geç fark edilir ve o kadar pahalı satılır.
/// </summary>
public static class PriceRules
{
    /// <summary>Satıştan satan oyuncunun kişisel cüzdanına giden pay (gerisi ekip kasasına).</summary>
    public const float SellerShare = 0.2f;

    /// <summary>Hiç sahte bırakılmadan alınmış ("sıcak") eserlerin çarpanı.</summary>
    public const float HotItemMultiplier = 0.5f;

    public static float SimilarityMultiplier(byte similarity)
    {
        if (similarity == InventorySlot.NoSimilarity) return HotItemMultiplier;
        if (similarity >= 90) return 1f;     // Kusursuz → tam fiyat
        if (similarity >= 75) return 0.85f;  // İyi → yüksek fiyat
        if (similarity >= 50) return 0.65f;  // Şüpheli → orta fiyat
        return 0.45f;                        // Kaba
    }

    public static int SalePrice(ItemDefinition def, byte similarity)
    {
        if (def == null || def.type != ItemType.Original) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(def.baseValue * SimilarityMultiplier(similarity)));
    }

    /// <summary>Çantadaki eşyanın satış fiyatı: eserin geldiği kaidenin değer çarpanı dahil (Staff Only eseri daha pahalı).</summary>
    public static int SalePrice(ItemDefinition def, InventorySlot slot)
    {
        int price = SalePrice(def, slot.Similarity);
        if (price <= 0) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(price * DisplayMultiplier(slot.LookKey)));
    }

    /// <summary>Eserin geldiği kaidenin değer çarpanı (yoksa 1).</summary>
    public static float DisplayMultiplier(int lookKey)
    {
        ArtworkDisplay d = lookKey != 0 ? ArtworkDisplay.FindByKey(lookKey) : null;
        return d != null ? d.ValueMultiplier : 1f;
    }

    /// <summary>Küçük ganimet satış değeri kaça bölünüp yetenek puanına çevrilir (160$ değerli eşya ≈ 16 puan).</summary>
    public const int SkillPointDivisor = 10;

    /// <summary>Küçük ganimetin tek adedinin kazandıracağı yetenek puanı (küçük ganimet değilse 0).</summary>
    public static int SkillPointsFor(ItemDefinition def, InventorySlot slot)
    {
        if (def == null || !def.IsSmallLoot) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(SalePrice(def, slot) / (float)SkillPointDivisor));
    }

    public static void Split(int price, bool hasTeamBank, out int team, out int personal)
    {
        if (!hasTeamBank)
        {
            team = 0;
            personal = price;
            return;
        }
        personal = Mathf.RoundToInt(price * SellerShare);
        team = price - personal;
    }
}
