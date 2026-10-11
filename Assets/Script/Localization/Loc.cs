using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Desteklenen diller. Yeni dil eklemek için: buraya ekle, LanguageNames'e adını yaz,
/// tablodaki her satıra o dilin metnini ekle.</summary>
public enum Language
{
    English = 0,
    Turkish = 1,
}

/// <summary>
/// Basit çeviri sistemi. Oyundaki tüm metinler buradaki tablodan gelir.
/// Kullanım:  Loc.T("menu.create")              → "Create Room" / "Oda Kur"
///            Loc.T("chat.joined", "Mustafa")   → "Mustafa joined the room."
/// Varsayılan dil İngilizce. Seçim bilgisayara kaydedilir.
/// </summary>
public static class Loc
{
    private const string LanguageKey = "MoverSim_Language";

    /// <summary>Menüde gösterilecek dil adları (her dil kendi dilinde yazılır).</summary>
    public static readonly string[] LanguageNames = { "English", "Türkçe" };

    /// <summary>Dil değişince tetiklenir.</summary>
    public static event Action LanguageChanged;

    private static bool loaded;
    private static Language current = Language.English;

    public static Language Current
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                int saved = PlayerPrefs.GetInt(LanguageKey, (int)Language.English);
                current = Enum.IsDefined(typeof(Language), saved) ? (Language)saved : Language.English;
            }
            return current;
        }
        set
        {
            if (loaded && current == value) return;
            loaded = true;
            current = value;
            PlayerPrefs.SetInt(LanguageKey, (int)value);
            PlayerPrefs.Save();
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>Anahtarın çevirisini döner. Anahtar yoksa anahtarın kendisini döner (eksik çeviri fark edilsin diye).</summary>
    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (!Table.TryGetValue(key, out string[] row)) return key;

        int i = (int)Current;
        string text = i < row.Length && !string.IsNullOrEmpty(row[i]) ? row[i] : row[0]; // eksikse İngilizce
        return text;
    }

    /// <summary>{0}, {1} gibi yer tutuculu çeviri.</summary>
    public static string T(string key, params object[] args)
    {
        string format = T(key);
        if (args == null || args.Length == 0) return format;
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            Debug.LogWarning($"[Loc] Hatalı format: {key}");
            return format;
        }
    }

    public static bool Has(string key) => !string.IsNullOrEmpty(key) && Table.ContainsKey(key);

    // ------------------------------------------------------------------
    //  ÇEVİRİ TABLOSU   anahtar → { English, Türkçe }
    // ------------------------------------------------------------------
    private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
    {
        // Ana menü
        { "menu.name",        new[] { "Your name:", "Adın:" } },
        { "menu.color",       new[] { "Your color:", "Rengin:" } },
        { "menu.create",      new[] { "Create Room", "Oda Kur" } },
        { "menu.joincode",    new[] { "Friend's room code:", "Arkadaşının oda kodu:" } },
        { "menu.join",        new[] { "Join", "Katıl" } },
        { "menu.language",    new[] { "Language:", "Dil:" } },

        // Oyun içi üst bilgi
        { "hud.code",         new[] { "Room Code: {0}", "Oda Kodu: {0}" } },
        { "hud.players",      new[] { "Players: {0}/{1}  •  [Esc] Menu", "Oyuncular: {0}/{1}  •  [Esc] Menü" } },
        { "hud.connecting",   new[] { "Connecting...", "Bağlanılıyor..." } },
        { "ping.host",        new[] { "Host • 0 ms", "Host • 0 ms" } },
        { "ping.value",       new[] { "Ping: {0} ms", "Ping: {0} ms" } },

        // Esc menüsü
        { "pause.title",      new[] { "Paused", "Duraklatıldı" } },
        { "pause.resume",     new[] { "Resume", "Devam Et" } },
        { "pause.players",    new[] { "PLAYERS ({0}/{1})", "OYUNCULAR ({0}/{1})" } },
        { "pause.tag.host",   new[] { "(Host)", "(Host)" } },
        { "pause.tag.you",    new[] { "(You)", "(Sen)" } },
        { "pause.kick",       new[] { "Kick", "At" } },
        { "pause.color",      new[] { "YOUR COLOR", "RENGİN" } },
        { "pause.settings",   new[] { "SETTINGS", "AYARLAR" } },
        { "pause.volume",     new[] { "Volume: {0}%", "Ses: %{0}" } },
        { "pause.sensitivity",new[] { "Mouse sensitivity: {0}x", "Fare hassasiyeti: {0}x" } },
        { "pause.language",   new[] { "LANGUAGE", "DİL" } },
        { "pause.copycode",   new[] { "Copy Code ({0})", "Kodu Kopyala ({0})" } },
        { "pause.close",      new[] { "Close Room", "Odayı Kapat" } },
        { "pause.leave",      new[] { "Leave Room", "Odadan Ayrıl" } },

        // Bağlantı durumları
        { "status.servicesfail", new[] { "Couldn't reach online services (check your internet connection).",
                                         "Servislere bağlanılamadı (internet bağlantını kontrol et)." } },
        { "status.creating",     new[] { "Creating room...", "Oda kuruluyor..." } },
        { "status.createfail",   new[] { "Couldn't create the room: {0}", "Oda kurulamadı: {0}" } },
        { "status.entercode",    new[] { "Enter a room code first.", "Önce oda kodunu yaz." } },
        { "status.joining",      new[] { "Joining room...", "Odaya katılınıyor..." } },
        { "status.joinfail",     new[] { "Couldn't join. The code may be wrong, or the room is full/closed.",
                                         "Katılınamadı. Kod yanlış olabilir ya da oda dolu/kapalı." } },
        { "status.roomclosed",   new[] { "The room was closed.", "Oda kapandı." } },
        { "status.disconnected", new[] { "Lost connection to the host.", "Host ile bağlantı koptu." } },
        { "status.kicked",       new[] { "The host kicked you from the room.", "Host seni odadan attı." } },

        // Sohbet
        { "chat.hint",        new[] { "[Enter] Chat", "[Enter] Sohbet" } },
        { "chat.created",     new[] { "{0} created the room.", "{0} odayı kurdu." } },
        { "chat.joined",      new[] { "{0} joined the room.", "{0} odaya katıldı." } },
        { "chat.left",        new[] { "{0} left the room.", "{0} odadan ayrıldı." } },
        { "chat.kicked",      new[] { "{0} was kicked from the room.", "{0} odadan atıldı." } },
        { "chat.renamed",     new[] { "{0} is now {1}.", "{0} artık {1} adını kullanıyor." } },

        // Taşıma
        { "carry.holding",    new[] { "[E] Drop    [Hold LMB] Power Throw    [R] Rotate    [Weight: {0} kg]",
                                      "[E] Bırak    [Sol Tık Basılı Tut] Güçlü Fırlat    [R] Döndür    [Ağırlık: {0} kg]" } },
        { "carry.pickup",     new[] { "[E] Pick up: {0} ({1} kg)", "[E] Kaldır: {0} ({1} kg)" } },
        { "carry.tooheavy",   new[] { "Too heavy to lift alone! ({0} - {1} kg)", "Bu çok ağır, tek kaldıramazsın! ({0} - {1} kg)" } },
        { "carry.locked",     new[] { "{0} (Can't be carried)", "{0} (Taşınamaz)" } },
        { "carry.heldbyother",new[] { "{0} (Someone else is carrying it)", "{0} (Başka biri taşıyor)" } },

        // Çanta
        { "inv.title",        new[] { "BAG", "ÇANTA" } },
        { "inv.hud",          new[] { "Bag", "Çanta" } },
        { "inv.weight",       new[] { "{0} / {1} kg", "{0} / {1} kg" } },
        { "inv.open",         new[] { "[Tab] Open bag", "[Tab] Çantayı aç" } },
        { "inv.close",        new[] { "[Tab] Close", "[Tab] Kapat" } },
        { "inv.empty",        new[] { "Your bag is empty.", "Çantan boş." } },
        { "inv.drop",         new[] { "Drop", "Bırak" } },
        { "inv.speed",        new[] { "Movement speed: {0}%", "Hareket hızı: %{0}" } },
        { "inv.pickup",       new[] { "[E] Take: {0} ({1} kg)", "[E] Al: {0} ({1} kg)" } },
        { "inv.toofull",      new[] { "{0} ({1} kg) won't fit in your bag", "{0} ({1} kg) çantana sığmıyor" } },
        { "inv.full",         new[] { "Not enough room in your bag!", "Çantanda yer yok!" } },
        { "inv.cantdrop",     new[] { "This item can't be dropped.", "Bu eşya bırakılamaz." } },
        { "inv.similarity",   new[] { "Similarity {0}%", "Benzerlik %{0}" } },

        { "inv.hold",         new[] { "Hold", "Ele Al" } },
        { "inv.putaway",      new[] { "Put Away", "Kaldır" } },
        { "inv.inhand",       new[] { "(in hand)", "(elinde)" } },

        // Bilgi kartı
        { "card.weight",      new[] { "Weight: {0} kg", "Ağırlık: {0} kg" } },
        { "card.count",       new[] { "Amount: {0}", "Adet: {0}" } },
        { "card.value",       new[] { "Value: ${0}", "Değer: ${0}" } },
        { "card.similarity",  new[] { "Similarity: {0}% ({1})", "Benzerlik: %{0} ({1})" } },
        { "card.throwhint",   new[] { "Press [Q] to throw and distract guards.", "[Q] ile fırlatıp güvenliklerin dikkatini dağıt." } },
        { "grade.flawless",   new[] { "Flawless", "Kusursuz" } },
        { "grade.good",       new[] { "Good", "İyi" } },
        { "grade.suspicious", new[] { "Suspicious", "Şüpheli" } },
        { "grade.crude",      new[] { "Crude", "Kaba" } },

        // Fırlatma
        { "throw.hint",       new[] { "[Q] Throw coin", "[Q] Para at" } },
        { "throw.none",       new[] { "You have nothing to throw.", "Fırlatacak bir şeyin yok." } },

        // Çalma
        { "steal.prompt",     new[] { "[Hold E] Steal from {0}", "[E Basılı Tut] {0} kişisinden çal" } },
        { "steal.getbehind",  new[] { "Sneak behind {0} to steal", "Çalmak için {0} kişisinin arkasına geç" } },
        { "steal.emptyprompt",new[] { "{0}'s bag is empty", "{0} kişisinin çantası boş" } },
        { "steal.title",      new[] { "{0}'S BAG", "{0} - ÇANTA" } },
        { "steal.help",       new[] { "Pick one item to steal. Be quick!", "Çalmak için bir eşya seç. Çabuk ol!" } },
        { "steal.take",       new[] { "Steal", "Çal" } },
        { "steal.nofit",      new[] { "Won't fit", "Sığmıyor" } },
        { "steal.emptybag",   new[] { "Their bag is empty.", "Çantası boş." } },
        { "steal.close",      new[] { "[Tab] / [Esc] Back off", "[Tab] / [Esc] Vazgeç" } },
        { "steal.rummage",    new[] { "{0} is going through your bag!", "{0} çantanı karıştırıyor!" } },
        { "steal.stolen",     new[] { "{0} stole {1} from you!", "{0} çantandan {1} çaldı!" } },
        { "steal.success",    new[] { "You stole {1} from {0}!", "{0} kişisinden {1} çaldın!" } },

        // Para
        { "money.team",       new[] { "Team: ${0}", "Ekip: ${0}" } },
        { "money.personal",   new[] { "You: ${0}", "Sen: ${0}" } },

        // Etkileşim noktaları
        { "interact.open",    new[] { "[E] Open", "[E] Aç" } },
        { "interact.close",   new[] { "[E] Close", "[E] Kapat" } },
        { "interact.search",  new[] { "[E] Search", "[E] Ara" } },
        { "interact.search.hold", new[] { "[Hold E] Search", "[E Basılı Tut] Ara" } },
        { "interact.empty",   new[] { "Empty", "Boş" } },
        { "interact.found",   new[] { "Found: {1}", "Bulundu: {1}" } },

        // Eşya türleri
        { "item.type.original",    new[] { "Original", "Orijinal" } },
        { "item.type.forgery",     new[] { "Forgery", "Sahte" } },
        { "item.type.tool",        new[] { "Tool", "Alet" } },
        { "item.type.distraction", new[] { "Distraction", "Dikkat Dağıtma" } },

        // Örnek eşyalar (Tools → Sahte Şaheser → Create Sample Items)
        { "item.original_painting", new[] { "Small Painting", "Küçük Tablo" } },
        { "item.original_vase",     new[] { "Ancient Vase", "Antik Vazo" } },
        { "item.original_ring",     new[] { "Golden Ring", "Altın Yüzük" } },
        { "item.original_bust",     new[] { "Marble Bust", "Mermer Büst" } },
        { "item.forgery_painting",  new[] { "Forged Painting", "Sahte Tablo" } },
        { "item.tool_lockpick",     new[] { "Lockpick Set", "Maymuncuk Seti" } },
        { "item.distraction_coin",  new[] { "Coin", "Bozuk Para" } },
    };
}
