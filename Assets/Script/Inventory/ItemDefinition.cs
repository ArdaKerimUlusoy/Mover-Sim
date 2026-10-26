using UnityEngine;

/// <summary>Eşya türleri (GDD).</summary>
public enum ItemType
{
    Original = 0,     // Müzeden çalınan orijinal eser → karaborsada satılır
    Forgery = 1,      // Atölyede yapılan sahte → müzede orijinalle değiştirilir
    Tool = 2,         // Kilit açma seti, kamera karıştırıcı...
    Distraction = 3,  // Bozuk para vb. (Q ile kullanılacak)
}

/// <summary>
/// Bir eşyanın tanımı. Project penceresinde sağ tık → Create → Sahte Şaheser → Item ile oluşturulur.
/// Oluşturduktan sonra: Tools → Sahte Şaheser → Rebuild Item Database.
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "Sahte Şaheser/Item")]
public class ItemDefinition : ScriptableObject
{
    [Tooltip("Benzersiz kimlik, ör. original_vase. Çeviri anahtarı 'item.<id>' olarak aranır.")]
    public string id = "new_item";

    [Tooltip("Çeviri tablosunda 'item.<id>' yoksa bu isim gösterilir (İngilizce yazın).")]
    public string displayName = "New Item";

    public ItemType type = ItemType.Original;

    [Min(0f)] public float weightKg = 1f;

    [Tooltip("Karaborsadaki taban değer (ileride satış fiyatı = taban × benzerlik çarpanı × aranma çarpanı).")]
    [Min(0)] public int baseValue = 100;

    [Tooltip("Aynı eşyadan birden fazlası tek satırda birikebilir mi? (ör. bozuk para)")]
    public bool stackable;
    [Min(1)] public int maxStack = 1;

    [Tooltip("Sadece sahte eserler: hangi orijinalin sahtesi? Boş = her esere uyar (test).")]
    public ItemDefinition forgeryOf;

    [Tooltip("Çanta panelindeki renk (ikon yokken).")]
    public Color uiColor = Color.white;
    public Texture2D icon;

    [Tooltip("Çantadan bırakılınca sahneye çıkan prefab (NetworkObject + WorldItem içermeli).")]
    public GameObject worldPrefab;

    public string NameKey => "item." + id;
    public string DisplayName => Loc.Has(NameKey) ? Loc.T(NameKey) : displayName;
    public int MaxStack => stackable ? Mathf.Max(1, maxStack) : 1;

    /// <summary>
    /// Küçük ganimet mi? Galeri masalarındaki (LootTable_*) eşyalar: kimliği "loot_" ile başlayan çalınabilir (Original) eşyalar.
    /// Bunlar satılınca para değil, yalnızca yetenek puanı kazandırır (TeamBank.SkillPoints).
    /// </summary>
    public bool IsSmallLoot => type == ItemType.Original && id != null && id.StartsWith("loot_");

    public static string TypeName(ItemType t) => Loc.T("item.type." + t.ToString().ToLowerInvariant());

    /// <summary>Benzerlik derecesi (GDD Bölüm 4): 90+ Kusursuz, 75+ İyi, 50+ Şüpheli, altı Kaba.</summary>
    public static string GradeName(byte similarity)
    {
        if (similarity >= 90) return Loc.T("grade.flawless");
        if (similarity >= 75) return Loc.T("grade.good");
        if (similarity >= 50) return Loc.T("grade.suspicious");
        return Loc.T("grade.crude");
    }

    public static Color GradeColor(byte similarity)
    {
        if (similarity >= 90) return new Color(0.45f, 0.9f, 0.5f);
        if (similarity >= 75) return new Color(0.65f, 0.85f, 0.4f);
        if (similarity >= 50) return new Color(1f, 0.8f, 0.3f);
        return new Color(1f, 0.45f, 0.4f);
    }
}
