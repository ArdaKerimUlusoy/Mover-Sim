using System.Collections.Generic;
using UnityEngine;

/// <summary>Emote kimlikleri (ağda bayt olarak gider; sırası DEĞİŞMEMELİ, yenisi sona eklenir).</summary>
public enum EmoteId : byte
{
    Ok = 0,          // OK işareti
    MiddleFinger = 1, // Orta parmak (eski 'nah' yerine)
    ThumbsUp = 2,    // Başparmak yukarı
    ThumbsDown = 3,  // Başparmak aşağı
    Wave = 4,        // El sallama
    PointLaugh = 5,  // Parmakla gösterip gülme
}

/// <summary>Emote kategorileri (çarkta ileride sekme / filtre için).</summary>
public enum EmoteCategory { Hand, Fun, Rare }

/// <summary>Bir emote'un tanımı. Poz kodu PlayerAvatarEmotes'ta (EmoteId'ye göre).</summary>
public class EmoteDef
{
    public EmoteId Id;
    public string Key;              // çeviri: "emote.<Key>"; isteğe bağlı ses: Resources/Emotes/<Key>
    public float Duration;          // sn (giriş + bekleme + çıkış solması dahil)
    public EmoteCategory Category;
    public bool LockedByDefault;    // ileride kilit açma sistemi için (şimdilik hepsi açık)

    public string NameKey => "emote." + Key;
    public string DisplayName => Loc.T(NameKey);
}

/// <summary>
/// Emote listesi ve çarktaki sıra. Yeni emote eklemek: EmoteId'ye sona bir değer, All'a bir satır, Loc'a "emote.&lt;key&gt;",
/// PlayerAvatarEmotes'a poz, EmoteIcons'a ikon. Ses için Resources/Emotes/&lt;key&gt; adlı bir AudioClip koymak yeter
/// (ya da EmoteSettings varlığındaki listeye).
/// </summary>
public static class EmoteCatalog
{
    public static readonly EmoteDef[] All =
    {
        new EmoteDef { Id = EmoteId.Ok,         Key = "ok",         Duration = 2.2f, Category = EmoteCategory.Hand },
        new EmoteDef { Id = EmoteId.MiddleFinger, Key = "middle",   Duration = 2.3f, Category = EmoteCategory.Hand },
        new EmoteDef { Id = EmoteId.ThumbsUp,   Key = "thumbsup",   Duration = 2.0f, Category = EmoteCategory.Hand },
        new EmoteDef { Id = EmoteId.ThumbsDown, Key = "thumbsdown", Duration = 2.0f, Category = EmoteCategory.Hand },
        new EmoteDef { Id = EmoteId.Wave,       Key = "wave",       Duration = 2.6f, Category = EmoteCategory.Hand },
        new EmoteDef { Id = EmoteId.PointLaugh, Key = "pointlaugh", Duration = 3.0f, Category = EmoteCategory.Hand },
    };

    /// <summary>Çarkta varsayılan sıra (saat yönünde, en üstten). İleride oyuncu kendi düzenini PlayerPrefs'e kaydedebilir.</summary>
    private static readonly EmoteId[] DefaultOrder =
    {
        EmoteId.Wave, EmoteId.ThumbsUp, EmoteId.Ok, EmoteId.PointLaugh, EmoteId.MiddleFinger, EmoteId.ThumbsDown,
    };

    private const string OrderPref = "SS_EmoteOrder";

    public static EmoteDef Get(int id)
    {
        for (int i = 0; i < All.Length; i++) if ((int)All[i].Id == id) return All[i];
        return null;
    }

    /// <summary>Oyuncu bu emote'u kullanabilir mi (kilit sistemi buraya bağlanır).</summary>
    public static bool IsUnlocked(EmoteDef d) => d != null && (!d.LockedByDefault || PlayerPrefs.GetInt("SS_EmoteUnlock_" + d.Key, 0) == 1);

    /// <summary>Çarkta görünecek emote'lar (oyuncunun kaydettiği sıra varsa o; yoksa varsayılan). Kilitliler atlanır.</summary>
    public static List<EmoteDef> WheelList()
    {
        var list = new List<EmoteDef>();
        string saved = PlayerPrefs.GetString(OrderPref, "");
        if (!string.IsNullOrEmpty(saved))
        {
            foreach (string part in saved.Split(','))
                if (int.TryParse(part, out int id))
                {
                    EmoteDef d = Get(id);
                    if (d != null && IsUnlocked(d) && !list.Contains(d)) list.Add(d);
                }
        }
        if (list.Count == 0)
            foreach (EmoteId id in DefaultOrder)
            {
                EmoteDef d = Get((int)id);
                if (d != null && IsUnlocked(d)) list.Add(d);
            }
        return list;
    }

    /// <summary>Oyuncunun çark düzenini kaydet (ileride emote düzenleme ekranı kullanır).</summary>
    public static void SaveWheelOrder(IList<EmoteId> order)
    {
        var parts = new List<string>();
        foreach (EmoteId id in order) parts.Add(((int)id).ToString());
        PlayerPrefs.SetString(OrderPref, string.Join(",", parts));
    }
}
