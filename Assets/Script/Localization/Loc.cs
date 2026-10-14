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
        { "menu.outfit",      new[] { "Your outfit (click to change):", "Kıyafetin (değiştirmek için tıkla):" } },
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
        { "pause.outfit",     new[] { "YOUR OUTFIT", "KIYAFETİN" } },
        { "outfit.hat.0",     new[] { "Hat: Beanie", "Şapka: Bere" } },
        { "outfit.hat.1",     new[] { "Hat: Cap", "Şapka: Kasket" } },
        { "outfit.hat.2",     new[] { "Hat: Top hat", "Şapka: Silindir" } },
        { "outfit.hat.3",     new[] { "Hat: None", "Şapka: Yok" } },
        { "outfit.face.0",    new[] { "Face: Mask", "Yüz: Maske" } },
        { "outfit.face.1",    new[] { "Face: Bandana", "Yüz: Bandana" } },
        { "outfit.face.2",    new[] { "Face: Bare", "Yüz: Açık" } },
        { "outfit.build.0",   new[] { "Body: Chubby", "Gövde: Tombul" } },
        { "outfit.build.1",   new[] { "Body: Slim", "Gövde: İnce" } },
        { "outfit.build.2",   new[] { "Body: Big", "Gövde: İri" } },
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
        { "voice.section",    new[] { "Voice chat", "Sesli sohbet" } },
        { "voice.mode.ptt",   new[] { "Push to talk (V)", "Bas konuş (V)" } },
        { "voice.mode.open",  new[] { "Open mic", "Sürekli açık" } },
        { "voice.device",     new[] { "Microphone: {0}", "Mikrofon: {0}" } },
        { "voice.nodevice",   new[] { "No microphone found.", "Mikrofon bulunamadı." } },
        { "voice.volume",     new[] { "Incoming voices: {0}%", "Gelen sesler: %{0}" } },
        { "voice.gain",       new[] { "Mic boost: {0}x", "Mikrofon yükseltme: {0}x" } },
        { "voice.gate",       new[] { "Open mic threshold (green = your voice, red line = threshold)", "Sürekli açık eşiği (yeşil = sesin, kırmızı çizgi = eşik)" } },
        { "voice.hud.ptt",    new[] { "Hold [V] to talk", "Konuşmak için [V] basılı tut" } },
        { "voice.hud.talking",new[] { "Talking...", "Konuşuyorsun..." } },
        { "voice.hud.open",   new[] { "Mic on  •  [V] mute", "Mikrofon açık  •  [V] kapat" } },
        { "voice.hud.muted",  new[] { "Mic muted  •  [V] unmute", "Mikrofon kapalı  •  [V] aç" } },
        { "voice.hud.nomic",  new[] { "No microphone", "Mikrofon yok" } },
        { "voice.hud.banned", new[] { "Banned word! Guards may hear", "Yasaklı kelime! Güvenlik duyabilir" } },

        // Sesli sohbette hırsızlık kelimeleri (VoiceKeywordListener / CrimeTalk)
        { "crimetalk.section",            new[] { "Theft words (guards can hear what you say)", "Hırsızlık kelimeleri (güvenlikler ne dediğini duyar)" } },
        { "crimetalk.mode.off",           new[] { "Off", "Kapalı" } },
        { "crimetalk.mode.both",          new[] { "TR + EN", "TR + EN" } },
        { "crimetalk.mode.turkish",       new[] { "Turkish", "Türkçe" } },
        { "crimetalk.mode.english",       new[] { "English", "İngilizce" } },
        { "crimetalk.help",               new[] { "Saying \"stole\", \"swapped\", \"fake\", \"heist\" (or \"çaldım\", \"değiştirdim\", \"sahte\") near a guard makes him come and inspect the artwork. Whisper and only very close guards hear it.", "Bir güvenliğin yakınında \"çaldım\", \"değiştirdim\", \"sahte\", \"soygun\" (ya da \"stole\", \"fake\") dersen gelip eseri inceler. Fısıldarsan sadece çok yakındaki güvenlik duyar." } },
        { "crimetalk.status.downloading", new[] { "Speech model {0}: downloading {1}%", "Konuşma modeli {0}: indiriliyor %{1}" } },
        { "crimetalk.status.loading",     new[] { "Speech model {0}: loading...", "Konuşma modeli {0}: yükleniyor..." } },
        { "crimetalk.status.ready",       new[] { "{0}: ready", "{0}: hazır" } },
        { "crimetalk.status.waiting",     new[] { "{0}: downloads when you join a room", "{0}: odaya girince indirilecek" } },
        { "crimetalk.status.failed",      new[] { "{0}: couldn't download (put the model in StreamingAssets/Vosk)", "{0}: indirilemedi (modeli StreamingAssets/Vosk içine koyun)" } },
        { "crimetalk.status.unsupported", new[] { "Speech detection only works on Windows for now.", "Konuşma algılama şimdilik sadece Windows'ta çalışıyor." } },
        { "crimetalk.status.nolib",       new[] { "Speech library missing (Assets/Plugins/x86_64/libvosk.dll).", "Konuşma kütüphanesi bulunamadı (Assets/Plugins/x86_64/libvosk.dll)." } },
        { "crimetalk.detected",           new[] { "You said \"{0}\" — guards nearby may hear it!", "\"{0}\" dedin — yakındaki güvenlikler duyabilir!" } },
        { "crimetalk.whisper",            new[] { "(whisper: only within ~{0} m)", "(fısıltı: sadece ~{0} m içi duyar)" } },
        { "crimetalk.normal",             new[] { "(normal voice: ~{0} m)", "(normal ses: ~{0} m)" } },
        { "crimetalk.loud",               new[] { "(loud: ~{0} m!)", "(yüksek ses: ~{0} m!)" } },
        { "crimetalk.heard",              new[] { "A guard heard you say \"{0}\"! He's coming to check.", "Bir güvenlik \"{0}\" dediğini duydu! Bakmaya geliyor." } },
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
        { "held.hint",        new[] { "[G] Put away / take out   [Wheel] Distance   [R] Rotate", "[G] Kaldır / ele al   [Tekerlek] Uzaklık   [R] Çevir" } },
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

        { "card.swapquality", new[] { "Forgery left behind: {0}% ({1})", "Bırakılan sahte: %{0} ({1})" } },
        { "card.hot",         new[] { "No forgery left behind (hot item, -50%)", "Yerine sahte bırakılmadı (sıcak mal, -%50)" } },
        { "card.price",       new[] { "Black market: ${0}", "Karaborsa: ${0}" } },
        { "card.forgeryof",   new[] { "Forgery of: {0}", "Şunun sahtesi: {0}" } },
        { "panel.close",      new[] { "[Tab] / [Esc] Close", "[Tab] / [Esc] Kapat" } },

        // Eser kaidesi
        { "display.swap",       new[] { "[Hold E] Swap {0} with your forgery ({1}%)", "[E Basılı Tut] {0} eserini sahtenle değiştir (%{1})" } },
        { "display.needforgery",new[] { "{0} — you need a forgery of it", "{0} — bunun sahtesi lazım" } },
        { "display.swapped",    new[] { "{0} (already swapped)", "{0} (zaten değiştirildi)" } },
        { "display.done",       new[] { "Swapped! {1} is in your bag.", "Değiştirildi! {1} çantanda." } },

        // Karaborsa
        { "market.prompt",    new[] { "[E] Black Market", "[E] Karaborsa" } },
        { "market.title",     new[] { "BLACK MARKET", "KARABORSA" } },
        { "market.help",      new[] { "Team bank gets {0}%, you get {1}%. Better forgeries left behind = higher prices.",
                                      "Ekip kasasına %{0}, sana %{1}. Yerine bırakılan sahte ne kadar iyiyse fiyat o kadar yüksek." } },
        { "market.helpnobank",new[] { "All money goes to you (no team bank in scene).", "Tüm para sana (sahnede ekip kasası yok)." } },
        { "market.sell",      new[] { "Sell ${0}", "Sat ${0}" } },
        { "market.sellall",   new[] { "Sell All (${0})", "Hepsini Sat (${0})" } },
        { "market.none",      new[] { "You have no originals to sell.", "Satacak orijinal eserin yok." } },
        { "market.hot",       new[] { "Hot item (no forgery left)", "Sıcak mal (sahte bırakılmadı)" } },
        { "trash.prompt",     new[] { "[E] Trash bin — throw away bad forgeries", "[E] Çöp kutusu — kötü sahteleri at" } },
        { "trash.title",      new[] { "Trash Bin", "Çöp Kutusu" } },
        { "trash.help",       new[] { "Forgeries you are not happy with can be thrown away here. They are gone for good.", "Beğenmediğin sahteleri buraya atabilirsin. Atılan sahte geri gelmez." } },
        { "trash.one",        new[] { "Throw away", "Çöpe at" } },
        { "trash.all",        new[] { "Throw away all forgeries ({0})", "Bütün sahteleri at ({0})" } },
        { "trash.none",       new[] { "You have no forgeries in your bag.", "Çantanda sahte yok." } },
        { "trash.done",       new[] { "{1} thrown in the trash.", "{1} çöpe atıldı." } },
        { "trash.doneall",    new[] { "{0} forgeries thrown in the trash.", "{0} sahte çöpe atıldı." } },
        { "market.sold",      new[] { "Sold {1} for ${0}!", "{1} ${0} karşılığında satıldı!" } },
        { "market.soldall",   new[] { "Sold everything for ${0}!", "Hepsi ${0} karşılığında satıldı!" } },

        // Garaj mağazası
        { "shop.prompt",      new[] { "[E] Garage Shop", "[E] Garaj Mağazası" } },
        { "shop.title",       new[] { "GARAGE SHOP", "GARAJ MAĞAZASI" } },
        { "shop.level",       new[] { "Lv {0}/{1}", "Sv {0}/{1}" } },
        { "shop.max",         new[] { "MAX", "MAKS" } },
        { "shop.buy",         new[] { "Buy ${0}", "Al ${0}" } },
        { "shop.payteam",     new[] { "paid by team bank", "ekip kasasından" } },
        { "shop.payyou",      new[] { "paid by you", "senin cüzdanından" } },
        { "shop.bought",      new[] { "Bought: {0}", "Satın alındı: {0}" } },
        { "shop.notenough",   new[] { "Not enough money!", "Yeterli paran yok!" } },
        { "shop.notenoughteam",new[] { "Not enough money in the team bank!", "Ekip kasasında yeterli para yok!" } },
        { "upg.biggerbag.name",     new[] { "Bigger Bag", "Büyük Çanta" } },
        { "upg.biggerbag.desc",     new[] { "+5 kg bag capacity", "+5 kg çanta kapasitesi" } },
        { "upg.quietshoes.name",    new[] { "Quiet Shoes", "Sessiz Ayakkabı" } },
        { "upg.quietshoes.desc",    new[] { "Quieter footsteps", "Daha sessiz ayak sesi" } },
        { "upg.swiftgloves.name",   new[] { "Swift Gloves", "Hızlı Eldiven" } },
        { "upg.swiftgloves.desc",   new[] { "Swap artworks 20% faster", "Eserleri %20 daha hızlı değiştir" } },
        { "upg.teambackpacks.name", new[] { "Team Backpacks", "Ekip Çantaları" } },
        { "upg.teambackpacks.desc", new[] { "+3 kg for the whole team", "Tüm ekibe +3 kg" } },
        { "upg.lockpick.name",      new[] { "Lockpick", "Maymuncuk" } },
        { "upg.lockpick.desc",      new[] { "Opens the Staff Only door. May break (35%)", "Staff Only kapısını açar. Kırılabilir (%35)" } },
        { "shop.count",             new[] { "x{0} (max {1})", "x{0} (en fazla {1})" } },
        { "lockpick.prompt",        new[] { "[Hold E] Pick the lock  (Lockpicks: {0})", "[E basılı tut] Kilidi maymuncukla aç  (Maymuncuk: {0})" } },
        { "lockpick.title",         new[] { "Picking the lock...  Lockpicks: {0}", "Kilit açılıyor...  Maymuncuk: {0}" } },
        { "lockpick.success",       new[] { "Success", "Başarı" } },
        { "lockpick.fail",          new[] { "Break", "Kırılma" } },
        { "lockpick.broke",         new[] { "Your lockpick broke! Lockpicks left: {0}", "Maymuncuk kırıldı! Kalan: {0}" } },
        { "label.staffonly",        new[] { "STAFF ONLY", "STAFF ONLY — PERSONEL" } },
        { "label.staffonly.locked", new[] { "LOCKED — lockpick needed", "KİLİTLİ — maymuncuk gerekir" } },
        { "lockpick.need",          new[] { "Locked — buy a Lockpick at the garage shop (${0})", "Kilitli — garaj mağazasından maymuncuk al (${0})" } },
        { "lockpick.done",          new[] { "Lock picked! The Staff Only door is open.", "Kilit açıldı! Staff Only kapısı açık." } },

        // Test müzesi etiketleri
        { "label.garage",     new[] { "GARAGE", "GARAJ" } },
        { "label.truck",      new[] { "TRUCK — WORKSHOP", "KAMYON — ATÖLYE" } },
        { "label.museum",     new[] { "MUSEUM ENTRANCE", "MÜZE GİRİŞİ" } },
        { "label.paintings",  new[] { "PAINTING GALLERY", "TABLO GALERİSİ" } },
        { "label.vases",      new[] { "VASE GALLERY", "VAZO GALERİSİ" } },
        { "label.treasury",   new[] { "TREASURY", "HAZİNE ODASI" } },
        { "label.masterpiece",new[] { "MASTERPIECE HALL", "BAŞYAPIT SALONU" } },
        { "label.gallery",    new[] { "GALLERY", "GALERİ" } },
        { "label.market",     new[] { "BLACK MARKET", "KARABORSA" } },
        { "label.shop",       new[] { "GARAGE SHOP", "GARAJ MAĞAZASI" } },
        { "label.cabinet",    new[] { "{0}", "{0}" } },

        // Gizlilik / güvenlik
        { "stealth.suspicion",  new[] { "Suspicion", "Şüphe" } },
        { "stealth.watched",    new[] { "Someone is watching you!", "Biri seni izliyor!" } },
        { "stealth.hidden",     new[] { "Hidden", "Saklanıyorsun" } },
        { "display.watched",    new[] { "Not now, someone is watching!", "Şimdi olmaz, biri izliyor!" } },
        { "alarm.banner",       new[] { "ALARM!  {0}s", "ALARM!  {0} sn" } },
        { "alarm.spotted",      new[] { "Security spotted {0}! ALARM!", "Güvenlik {0} kişisini fark etti! ALARM!" } },
        { "alarm.seenswap",     new[] { "{0} was seen swapping an artwork! ALARM!", "{0} eser değiştirirken görüldü! ALARM!" } },
        { "alarm.fake",         new[] { "A guard noticed a fake artwork! ALARM!", "Bir güvenlik sahte eseri fark etti! ALARM!" } },
        { "alarm.secroom",      new[] { "{0} was seen in the security room! ALARM!", "{0} güvenlik odasında görüldü! ALARM!" } },
        { "alarm.witness",      new[] { "A visitor told the guards about {0}! ALARM!", "Bir ziyaretçi {0} kişisini güvenliğe ihbar etti! ALARM!" } },
        { "console.prompt",     new[] { "[Hold E] Shut down cameras ({0}s)", "[E Basılı Tut] Kameraları kapat ({0} sn)" } },
        { "console.off",        new[] { "Cameras offline: {0}s", "Kameralar kapalı: {0} sn" } },
        { "console.cooldown",   new[] { "System rebooting: {0}s", "Sistem yeniden başlıyor: {0} sn" } },
        { "console.banner",     new[] { "CAMERAS OFFLINE  {0}s", "KAMERALAR KAPALI  {0} sn" } },
        { "console.used",       new[] { "{0} shut down the cameras for {1}s!", "{0} kameraları {1} sn kapattı!" } },
        { "ring.step.cast",     new[] { "Cast", "Döküm" } },
        { "ring.step.stones",   new[] { "Gems", "Taşlar" } },
        { "ring.step.polish",   new[] { "Polish", "Cila" } },
        { "ring.cast.help",     new[] { "Pick the mold and metal of the original, then hold the mouse on the mold (or Space) to pour. Stop at the white line!", "Orijinalin kalıbını ve metalini seç, sonra kalıbın üstünde fareye (ya da Boşluk'a) basılı tutup dök. Beyaz çizgide dur!" } },
        { "ring.mold",          new[] { "Mold", "Kalıp" } },
        { "ring.fill",          new[] { "Filled: {0}%", "Doluluk: %{0}" } },
        { "ring.spilled",       new[] { "Overfilled — the band will be too thick!", "Taştı — halka fazla kalın olacak!" } },
        { "ring.emptymold",     new[] { "Empty the mold", "Kalıbı boşalt" } },
        { "ring.next.stones",   new[] { "Next: set the gems", "Sonraki: taşları tak" } },
        { "ring.stones.help",   new[] { "Pick a gem, then click on the ring where it sits on the original (top right). Right click: remove.", "Bir taş seç, sonra halkada orijinaldeki (sağ üst) yerine tıkla. Sağ tık: sök." } },
        { "ring.stonecount",    new[] { "Gems set: {0}/{1}", "Takılan taş: {0}/{1}" } },
        { "ring.removeall",     new[] { "Remove all gems", "Bütün taşları sök" } },
        { "ring.next.polish",   new[] { "Next: polish", "Sonraki: cila" } },
        { "ring.polish.help",   new[] { "Hold the left mouse button and rub along the ring until it shines.", "Sol tıka basılı tutup halkanın üzerinde gezdirerek parlayana kadar ov." } },
        { "ring.shine",         new[] { "Shine", "Parlaklık" } },
        { "ring.pour",          new[] { "Hold: pour molten metal", "Basılı tut: eriyik metali dök" } },
        { "ring.place",         new[] { "Click on the ring to set the gem", "Taşı takmak için halkaya tıkla" } },
        { "ring.rub",           new[] { "Rub the ring", "Halkayı ov" } },
        { "alarm.jailed",       new[] { "The thief is behind bars. The alarm has stopped.", "Hırsız parmaklıklar ardında. Alarm sustu." } },
        { "alarm.jailbreak",    new[] { "{0} broke a prisoner out! ALARM!", "{0} bir mahkumu kaçırdı! ALARM!" } },
        { "jail.attempt",       new[] { "Someone is picking the jail lock — guards are on their way!", "Biri hapis kilidini kurcalıyor — güvenlikler geliyor!" } },
        { "alarm.ended",        new[] { "The alarm has stopped.", "Alarm sustu." } },
        { "chat.caught",        new[] { "{0} was caught and jailed! ({1} originals went back to their pedestals)", "{0} yakalandı ve hapse atıldı! ({1} orijinal yerine geri kondu)" } },
        { "chat.rescued",       new[] { "{0} freed {1} from jail!", "{0}, {1} kişisini hapisten kurtardı!" } },
        { "jail.banner",        new[] { "YOU'RE IN JAIL", "HAPİSTESİN" } },
        { "jail.wait",          new[] { "A teammate can free you at the door. Otherwise you're out in {0}s.", "Bir arkadaşın kapıdan seni kurtarabilir. Yoksa {0} sn sonra çıkarsın." } },
        { "jail.rescue",        new[] { "[Hold E] Free your teammate", "[E Basılı Tut] Arkadaşını kurtar" } },
        { "jail.inside",        new[] { "Locked — wait for a teammate", "Kilitli — arkadaşını bekle" } },
        { "jail.empty",         new[] { "Jail (empty)", "Hapis (boş)" } },
        { "label.jail",         new[] { "JAIL", "HAPİS" } },

        // Saklanma
        { "hide.enter",         new[] { "[E] Hide", "[E] Saklan" } },
        { "hide.occupied",      new[] { "Occupied", "Dolu" } },
        { "hide.leave",         new[] { "[E] Leave hiding spot", "[E] Saklandığın yerden çık" } },
        { "label.staff",        new[] { "STAFF ONLY", "SADECE PERSONEL" } },

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
        { "item.forgery_vase",      new[] { "Forged Vase", "Sahte Vazo" } },
        { "item.forgery_bust",      new[] { "Forged Bust", "Sahte Büst" } },
        { "item.forgery_ring",      new[] { "Forged Ring", "Sahte Yüzük" } },
        { "item.tool_lockpick",     new[] { "Lockpick Set", "Maymuncuk Seti" } },
        { "item.distraction_coin",  new[] { "Coin", "Bozuk Para" } },

        // Küçük ganimetler (galeri masaları — sahtesi gerekmez)
        { "item.loot_snuffbox",  new[] { "Snuff Box", "Enfiye Kutusu" } },
        { "item.loot_figurine",  new[] { "Bronze Figurine", "Bronz Heykelcik" } },
        { "item.loot_coinpouch", new[] { "Old Coin Pouch", "Eski Sikke Kesesi" } },

        // Görev / müze (MuseumSession)
        { "mission.title",     new[] { "MISSION {0} — {1}, {2}", "GÖREV {0} — {1}, {2}" } },
        { "mission.quota",     new[] { "Team earnings: ${0} / ${1}", "Ekip kazancı: ${0} / ${1}" } },
        { "brief.staff",       new[] { "SPECIAL TARGET — in the Staff Only room", "ÖZEL HEDEF — Staff Only odasında" } },
        { "mission.complete",  new[] { "MISSION COMPLETE!", "GÖREV TAMAM!" } },
        { "mission.nexthost",  new[] { "Press N for the next mission (new museum, more to steal)", "Sonraki görev için N'ye bas (yeni müze, daha çok eser)" } },
        { "mission.truck",     new[] { "Back in the truck: {0} / {1}", "Kamyona dönen: {0} / {1}" } },
        { "mission.return",    new[] { "Quota reached! Everyone back to the truck.", "Kota tamam! Herkes kamyona dönsün." } },
        { "mission.nextwait",  new[] { "Waiting for the host to start the next mission...", "Sonraki görevi host başlatacak..." } },
        { "mission.next",      new[] { "Mission {1}: {0}", "Görev {1}: {0}" } },

        // Sahte atölyesi (ForgeryWorkbench / WorkbenchUI / CanvasPainter)
        { "bench.prompt",        new[] { "[E] Forgery workbench", "[E] Sahte atölyesi" } },
        { "bench.notargets",     new[] { "Workbench — no targets in this museum", "Atölye — bu müzede hedef yok" } },
        { "bench.nodef",         new[] { "There is no forgery item for {1}.", "{1} için sahte eşya tanımı yok." } },
        { "bench.done",          new[] { "Forgery ready ({0}%): {1} is in your bag.", "Sahte hazır (%{0}): {1} çantanda." } },
        { "bench.choose",        new[] { "What will you forge?", "Neyin sahtesini yapacaksın?" } },
        { "bench.resume",        new[] { "Continue: {0} (now {1}%)", "Devam et: {0} (şu an %{1})" } },
        { "push.hint",           new[] { "[F] Push", "[F] İt" } },
        { "push.pushed",         new[] { "Someone pushed you!", "Biri seni itti!" } },
        { "push.struck",         new[] { "The police attacked you!", "Polis sana saldırdı!" } },
        { "drop.hint",           new[] { "Q: put it down", "Q: yere bırak" } },
        { "bench.choosehint",    new[] { "Click or press 1-9.  Esc: close.  You don't need the original: forge first, swap it in the museum later.", "Tıkla ya da 1-9.  Esc: kapat.  Orijinal gerekmez: önce sahteyi yap, sonra müzede değiştir." } },
        { "bench.wheel",         new[] { "potter's wheel", "çömlek çarkı" } },
        { "bench.canvas",        new[] { "canvas", "tuval" } },
        { "bench.mold",          new[] { "sculpting mold", "kalıptan şekillendirme" } },
        { "bench.jeweler",       new[] { "jeweler's bench", "kuyumcu tezgahı" } },
        { "bench.moldhelp",      new[] { "Left: your copy. Right: the original.\nDrag the sliders until they match, then pick the stone and the base.\nA / D: turn both", "Solda senin kopyan, sağda orijinal.\nKaydırıcıları ikisi aynı olana kadar oynat, sonra taşı ve kaideyi seç.\nA / D: ikisini de çevir" } },
        { "bench.jewelhelp",     new[] { "Left: your copy. Right: the original.\nPick the body, the metal and the gems so they match.\nA / D: turn both", "Solda senin kopyan, sağda orijinal.\nGövdeyi, metali ve taşları orijinale göre seç.\nA / D: ikisini de çevir" } },
        { "bench.stone",         new[] { "Material", "Malzeme" } },
        { "bench.base",          new[] { "Base", "Kaide" } },
        { "bench.body",          new[] { "Body", "Gövde" } },
        { "bench.metal",         new[] { "Metal", "Metal" } },
        { "bench.stonecount",    new[] { "Number of gems", "Taş sayısı" } },
        { "bench.gem",           new[] { "Gem {0}", "{0}. taş" } },
        { "bench.sculptesc",     new[] { "Esc: give up — finish the shape and paint it to make a forgery", "Esc: vazgeç — sahte için önce şekli bitir ve boya" } },
        { "bench.alreadyswapped",new[] { "   (already swapped)", "   (zaten değiştirildi)" } },
        { "bench.canvastitle",   new[] { "FORGING: {0}", "SAHTE: {0}" } },
        { "bench.sculpt",        new[] { "1) SHAPE", "1) ŞEKİLLENDİR" } },
        { "bench.sculpthelp",    new[] { "Fit the clay to the green guide.\nLeft click: push in (whole ring)  |  Right click: pull out\nShift + click: single point  |  Middle mouse: rotate  |  Wheel: brush", "Kili yeşil rehbere oturt.\nSol tık: içeri it (tüm halka)  |  Sağ tık: dışarı çek\nShift + tık: tek nokta  |  Orta tuş: çevir  |  Tekerlek: fırça" } },
        { "bench.shape",         new[] { "Shape", "Şekil" } },
        { "bench.toPaint",       new[] { "Ready! Press SPACE to paint", "Hazır! Boyamak için SPACE" } },
        { "bench.paint",         new[] { "2) PAINT", "2) BOYA" } },
        { "bench.painthelp",     new[] { "Copy the pattern from the original next to it.\nLeft click: paint  |  Shift: whole band  |  B: fill all\n1-{0}: color  |  Middle mouse: rotate  |  Wheel: brush", "Yandaki orijinale bakarak deseni kopyala.\nSol tık: boya  |  Shift: tüm bant  |  B: hepsini doldur\n1-{0}: renk  |  Orta tuş: çevir  |  Tekerlek: fırça" } },
        { "bench.paintscore",    new[] { "Paint", "Boya" } },
        { "bench.regions",       new[] { "Shapes done: {0}/{1}", "Tamamlanan şekiller: {0}/{1}" } },
        { "bench.similarity",    new[] { "Similarity: {0}% ({1})", "Benzerlik: %{0} ({1})" } },
        { "bench.cancel",        new[] { "Cancel", "Vazgeç" } },
        { "bench.finish",        new[] { "Esc: finish — the forgery goes into your bag", "Esc: bitir — sahte çantana girer" } },
        { "bench.canvashelp",    new[] { "Left click: brush  |  Shift + click: bucket fill  |  Wheel: brush size ({0})  |  1-{1}: color  |  B: fill all  |  G: guide {2}  |  Esc: finish", "Sol tık: fırça  |  Shift + tık: kova  |  Tekerlek: fırça boyu ({0})  |  1-{1}: renk  |  B: hepsini doldur  |  G: rehber {2}  |  Esc: bitir" } },
        { "bench.brushsize",     new[] { "Brush size: {0}   ( [ ] or - + )", "Fırça boyutu: {0}   ( [ ] ya da - + )" } },
        { "bench.on",            new[] { "on", "açık" } },
        { "bench.off",           new[] { "off", "kapalı" } },
        { "bench.original",      new[] { "ORIGINAL", "ORİJİNAL" } },
        { "bench.colors",        new[] { "Colors", "Renkler" } },

        // Brifing terminali
        { "brief.prompt",   new[] { "[E] Mission briefing", "[E] Görev brifingi" } },
        { "brief.waiting",  new[] { "Preparing the museum...", "Müze hazırlanıyor..." } },
        { "brief.close",    new[] { "E / Esc: close", "E / Esc: kapat" } },
        { "brief.quota",    new[] { "Quota: earn ${0} for the team bank (so far ${1})", "Kota: ekip kasasına ${0} kazandır (şu an ${1})" } },
        { "brief.value",    new[] { "Value ${0}  ·  {1} kg  ·  {2}", "Değer ${0}  ·  {1} kg  ·  {2}" } },
        { "brief.noforge",  new[] { "can't be forged", "sahtesi yapılamaz" } },
        { "brief.swapped",  new[] { "Swapped — a {0}% forgery is in its place", "Değiştirildi — yerinde %{0} sahte var" } },
        { "brief.plan",     new[] { "Plan: 1) Make forgeries at the truck workbench.  2) In the museum, hold E at the pedestal to swap.  3) Sell the originals at the black market.  4) When the quota is met, everyone returns to the truck.", "Plan: 1) Kamyondaki atölyede sahteleri yap.  2) Müzede kaideye E basılı tutup değiştir.  3) Orijinalleri karaborsada sat.  4) Kota dolunca herkes kamyona döner." } },
        { "label.workbench", new[] { "FORGERY WORKBENCH", "SAHTE ATÖLYESİ" } },
        { "label.briefing",  new[] { "BRIEFING", "BRİFİNG" } },
    };
}
