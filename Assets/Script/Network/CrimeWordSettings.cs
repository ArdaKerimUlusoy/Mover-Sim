using UnityEngine;

/// <summary>
/// Sesli sohbette yasaklı (suç) kelime algılamanın ayarları (VoiceKeywordListener + CrimeTalk).
/// Asset: Assets/Resources/CrimeWordSettings.asset (menü: Tools/Sahte Şaheser/Voice Filter Settings).
/// Asset yoksa koddaki varsayılanlar kullanılır. Play sırasında yapılan değişiklikler ~0.5 sn içinde uygulanır ve
/// asset'e kaydedildiği için Play bitince KAYBOLMAZ.
///
/// "Denge" tek kaydırıcıdır: 0 = en hızlı / en hassas (anında algılar, yanlış alarm riski yüksek),
/// 1 = en güvenli / en geç (sadece cümle bitince kesin sonuç). "Ayrıntılı alanları elle ayarla" kapalıyken
/// ayrıntılı alanlar Denge'den hesaplanır (Inspector'da gri görünür); açıkken Denge yok sayılır ve alanlar elle girilir.
/// </summary>
[CreateAssetMenu(fileName = "CrimeWordSettings", menuName = "Sahte Şaheser/Crime Word Settings")]
public class CrimeWordSettings : ScriptableObject
{
    public const string ResourceName = "CrimeWordSettings";

    // ---------------- Denge ----------------
    [Header("Denge (ana ayar)")]
    [Tooltip("0 = EN HIZLI / EN HASSAS: kelime ara sonuçta ilk göründüğü an algılanır, eşikler düşük (yanlış alarm riski yüksek).\n" +
             "0.5 = önerilen: kelime ~0.1-0.3 sn kararlı görünmeli ve iki tanıyıcı (kelime listeli + serbest) uyuşmalı.\n" +
             "1 = EN GÜVENLİ / EN GEÇ: ara sonuç kullanılmaz, sadece cümle bitince (~0.5-1 sn sonra) yüksek güvenli kesin sonuç.")]
    [Range(0f, 1f)] public float balance = 0.5f;

    [Tooltip("Kapalı (önerilen): aşağıdaki gri alanlar Denge kaydırıcısından otomatik hesaplanır.\n" +
             "Açık: Denge yok sayılır, ayrıntılı alanları tek tek elle ayarlarsınız.")]
    public bool manualDetails = false;

    // ---------------- Anında algılama (ara / partial sonuçlar) ----------------
    [Header("Anında algılama (ara sonuçlar)")]
    [Tooltip("Açık: kelime söylendiği anda (cümle bitmeden) ara sonuçtan algılanır. Kapalı: sadece cümle bitince kesin sonuç (daha geç, daha güvenli).")]
    public bool partialEnabled = true;

    [Tooltip("Kelime ara sonuçta art arda en az bu kadar güncellemede (her biri ~40 ms) görünmeli. 1 = ilk görüldüğü an. Yüksek = daha az yanlış alarm, daha geç.")]
    [Range(1, 10)] public int partialStableUpdates = 3;

    [Tooltip("Kelime ara sonuçta en az bu kadar saniye kesintisiz görünmeli (ses akışı zamanı). 0 = bekleme yok.")]
    [Range(0f, 1f)] public float partialStableSeconds = 0.2f;

    [Tooltip("Kelime ara sonucun SON kelimesiyse (hâlâ uzayabilir: \"çal\" → \"çalışıyorum\") ya da çok kısaysa (çal, rob) eklenen fazladan bekleme (sn).")]
    [Range(0f, 1f)] public float partialTailExtraSeconds = 0.175f;

    [Tooltip("Açık: serbest tanıyıcının ara sonuçta duyduğu kelime, aynı dilin kelime listeli tanıyıcısı da benzer bir suç kelimesi duymadıkça sayılmaz (yanlış alarmı belirgin azaltır). " +
             "Kelime listeli tanıyıcının bulduğu kelime için serbest tanıyıcı uyumu HER ZAMAN aranır.")]
    public bool partialRequireAgreement = true;

    // ---------------- Kesin sonuç filtresi ----------------
    [Header("Kesin sonuç (cümle sonu) filtresi")]
    [Tooltip("Serbest tanıyıcının kesin sonuçta bulduğu suç kelimesi için en düşük Vosk güveni (0-1).")]
    [Range(0f, 1f)] public float freeMinConf = 0.715f;

    [Tooltip("Kelime listeli tanıyıcının kesin sonuçta bulduğu kelime için en düşük güven. Bu tanıyıcı her sesi listeye zorladığı için daha yüksek tutulur.")]
    [Range(0f, 1f)] public float grammarMinConf = 0.825f;

    [Tooltip("4 harf ve daha kısa kelimeler (çal, rob, fake, swap, loot) için güven eşiğine eklenen pay.")]
    [Range(0f, 0.3f)] public float shortWordExtraConf = 0.04f;

    [Tooltip("Kelime listeli tanıyıcının bulduğu kelime, serbest tanıyıcının aynı anda duyduğuna en az bu kadar benzemeli (0-1 harf benzerliği; uzatılmış harfler sıkıştırılarak karşılaştırılır). Ara sonuç uyumunda da kullanılır.")]
    [Range(0f, 1f)] public float minSimilarity = 0.65f;

    [Tooltip("Serbest tanıyıcı aynı yere en az bu güvenle suç OLMAYAN tek bir kelime yazdıysa kelime listeli tanıyıcının bulduğu kesin sonuç sayılmaz. Düşük = daha katı.")]
    [Range(0f, 1f)] public float freeOverrideConf = 0.85f;

    // ---------------- Ses kapısı ----------------
    [Header("Ses kapısı")]
    [Tooltip("Mutlak taban (0-1): kelimenin söylendiği andaki ses bundan düşükse (sessizlik / hışırtı) sayılmaz.")]
    [Range(0f, 1f)] public float minWordLevel = 0.04f;

    [Tooltip("Kelime, oyuncunun KENDİ normal konuşma seviyesinin en az bu katı yüksek olmalı (fısıltı ~0.5). Kısık mikrofonlu oyuncu da adil ölçülür.")]
    [Range(0f, 1f)] public float minRelativeLevel = 0.35f;

    [Tooltip("O anda hoparlörden başka bir oyuncu konuşuyorsa (sesi mikrofona sızabilir) istenen kat.")]
    [Range(0f, 1.5f)] public float echoRelativeLevel = 0.7f;

    // ---------------- Dil (Denge'den bağımsız) ----------------
    [Header("Dil (Denge'den bağımsız)")]
    [Tooltip("Kapalı (önerilen): Türkçe konuşurken söylenen İngilizce yasaklı kelime de (ve tersi) algılanır.\n" +
             "Açık: diğer dilin serbest tanıyıcısı aynı bölümü 'Dil hakemliği payı' kadar daha emin tanıdıysa bu dilin kesin sonuçtaki kelimesi veto edilir (dil karıştıranlar kaçırılır).")]
    public bool languageArbitration = false;

    [Tooltip("Dil hakemliği açıksa: diğer dilin ortalama güveni bu kadar yüksekse veto edilir.")]
    [Range(0f, 1f)] public float languageMargin = 0.15f;

    // ---------------- Aksan (Denge'den bağımsız) ----------------
    [Header("Aksan / sesteş kelimeler (Denge'den bağımsız)")]
    [Tooltip("Serbest tanıyıcının aksan yüzünden yanlış yazdığı kelimeler: \"duyulan=yasaklı\" (ör. \"still=steal\").\n" +
             "Kelime listeli tanıyıcı yasaklı kelimeyi duyup serbest tanıyıcı bunlardan birini yazdıysa ÇELİŞKİ SAYILMAZ, uyum sayılır.\n" +
             "Ayrıca İngilizcede ünlüler (a e i o u y) aksanla çok kaydığı için ünsüz iskeleti aynı olan kelimeler de uyumlu sayılır " +
             "(steal = still = steel = stil: hepsi \"stl\").\n" +
             "Tanıyıcının ne yazdığını görmek için aşağıdaki 'Tanıma günlüğü'nü açın, kaçırılan kelimeyi buraya ekleyin.")]
    public string[] soundAlikes =
    {
        "still=steal", "steel=steal", "steele=steal", "stil=steal", "stiil=steal", "stell=steal", "steals=steal",
        "stilling=stealing", "steeling=stealing",
        "swop=swap", "swopped=swapped",
    };

    [Tooltip("Kapalı (önerilen): yukarıdaki kelimeler TEK BAŞINA algılatmaz; sadece kelime listeli tanıyıcı yasaklı kelimeyi duyunca onu onaylar.\n" +
             "Açık: serbest tanıyıcı \"still\" yazınca bile yasaklı sayılır (aksanlı oyuncuda en hassas hali; ama İngilizce konuşan biri " +
             "\"still\" dediğinde de yakalanır).")]
    public bool soundAlikesStandalone = false;

    // ---------------- Ek yasaklı kelimeler ----------------
    [Header("Ek yasaklı kelimeler (koda dokunmadan)")]
    [Tooltip("Türkçe tanıyıcının yasaklı sayacağı ek kelimeler (tek kelime, küçük harf). 5+ harfliler kök gibi davranır: " +
             "\"kaçır\" → kaçırdım, kaçıralım... Daha kısalar birebir eşleşir.\n" +
             "Kelime listeli tanıyıcıya model YÜKLENİRKEN eklenir: değiştirince Play'i durdurup yeniden başlatın.")]
    public string[] extraTurkishWords = { };

    [Tooltip("İngilizce tanıyıcının yasaklı sayacağı ek kelimeler (tek kelime, küçük harf). 5+ harfliler kök gibi davranır: " +
             "\"smuggle\" → smuggle, smuggled, smuggles. Daha kısalar birebir. Kelime listeli tanıyıcı yalnızca yazdığınız GERÇEK " +
             "kelimeleri dinler, bu yüzden sık kullanılan biçimleri ayrı ayrı ekleyin (smuggle, smuggled, smuggling).\n" +
             "Model yüklenirken eklenir: değiştirince Play'i yeniden başlatın. (change / replace zaten kodda var.)")]
    public string[] extraEnglishWords = { };

    // ---------------- Hata ayıklama ----------------
    [Header("Hata ayıklama")]
    [Tooltip("Açık: tanıyıcıların duyduğu her cümle, algılanan ve REDDEDİLEN kelimeler (nedeniyle) Console'a yazılır. " +
             "\"steal\" dediğinde tanıyıcı ne anlıyor görmek için açın (oyun dağıtılırken kapatın).")]
    public bool logRecognition = false;

    // ---------------- Tekrar / gönderim (Denge'den bağımsız) ----------------
    [Header("Tekrar ve gönderim (Denge'den bağımsız)")]
    [Tooltip("Çift sayım koruması (sn): AYNI söyleyişin hem ara hem kesin sonuçtan (ya da iki tanıyıcıdan) iki kez sayılmasını önler. " +
             "Her tanıyıcı her söyleyişi bir kez sayar; farklı söyleyişler (\"çal ... çal\") ayrı ayrı bildirilir. 0 = koruma yok.")]
    [Range(0f, 2f)] public float doubleCountSeconds = 0.8f;

    [Tooltip("İstemci: iki bildirim arasında sunucuya gönderimde en az bu kadar sn (sadece taşkın koruması). 0 = her algılama gönderilir.")]
    [Range(0f, 5f)] public float clientSendMinInterval = 0.25f;

    [Tooltip("Sunucu: aynı oyuncudan iki 'duyuldu' işlemi arasında en az bu kadar sn (hileli/taşkın istemciye karşı). Eskiden 4 sn idi.")]
    [Range(0f, 10f)] public float serverMinInterval = 0.5f;

    [Tooltip("Sunucu: bildirim, o istemciden son bu kadar sn içinde GERÇEKTEN ses paketi geldiyse kabul edilir (sesi olmayan hileli bildirimlere karşı). " +
             "Kesin sonuç cümle bitince (~0.8-1 sn sonra) geldiği için 1.5'tan düşük önerilmez. Host'un kendi bildirimi muaf.")]
    [Range(0.5f, 10f)] public float serverVoiceRecentSeconds = 2.5f;

    [Tooltip("Sunucu: aynı eser için 'konuşma duyuldu → sahte fark etme' zarı en fazla bu kadar sn'de bir atılır (arada gelen bildirimler güvenliği yine yönlendirir, " +
             "son zarın sonucunu kullanır). Spamle zar attırmayı önler.")]
    [Range(0f, 30f)] public float overheardRollInterval = 5f;

    // =====================================================================

    /// <summary>İş parçacığı güvenli, değişmez anlık kopya (tanıma iş parçacığı bunu okur).</summary>
    public sealed class Values
    {
        public bool partialEnabled, partialRequireAgreement, languageArbitration, logRecognition;
        public int partialStableUpdates;
        public float partialStableSeconds, partialTailExtraSeconds;
        public float freeMinConf, grammarMinConf, shortWordExtraConf, minSimilarity, freeOverrideConf;
        public float minWordLevel, minRelativeLevel, echoRelativeLevel;
        public float languageMargin, doubleCountSeconds, clientSendMinInterval, serverMinInterval;
    }

    /// <summary>Denge'den türetilen değerler (manualDetails kapalıyken geçerli olanlar).</summary>
    public static void DeriveFromBalance(float b, Values v)
    {
        b = Mathf.Clamp01(b);
        // Anında algılama için bekleme süreleri kısaltıldı (0.5'te ~0.15 sn): kısa tek kelimelik cümlelerde ("çalıyorum.")
        // ara sonuç kararlı sayılmadan cümle bitiyor ve kelime kaçıyordu. Yanlış alarmı asıl iki tanıyıcı uyumu önler.
        v.partialEnabled = b < 0.95f;
        v.partialStableUpdates = Mathf.RoundToInt(Mathf.Lerp(1f, 4f, b));
        v.partialStableSeconds = Mathf.Lerp(0f, 0.30f, b);
        v.partialTailExtraSeconds = Mathf.Lerp(0f, 0.25f, b);
        v.partialRequireAgreement = b >= 0.4f;
        v.freeMinConf = Mathf.Lerp(0.50f, 0.85f, b);
        v.grammarMinConf = Mathf.Lerp(0.60f, 0.92f, b);
        v.shortWordExtraConf = Mathf.Lerp(0f, 0.08f, b);
        v.minSimilarity = Mathf.Lerp(0.50f, 0.80f, b);
        v.freeOverrideConf = Mathf.Lerp(0.95f, 0.75f, b);
        v.minWordLevel = Mathf.Lerp(0.03f, 0.05f, b);
        v.minRelativeLevel = Mathf.Lerp(0.20f, 0.50f, b);
        v.echoRelativeLevel = Mathf.Lerp(0.50f, 0.90f, b);
    }

    /// <summary>Şu an geçerli değerler.</summary>
    public Values Resolve()
    {
        var v = new Values
        {
            partialEnabled = partialEnabled, partialRequireAgreement = partialRequireAgreement,
            partialStableUpdates = partialStableUpdates, partialStableSeconds = partialStableSeconds,
            partialTailExtraSeconds = partialTailExtraSeconds,
            freeMinConf = freeMinConf, grammarMinConf = grammarMinConf, shortWordExtraConf = shortWordExtraConf,
            minSimilarity = minSimilarity, freeOverrideConf = freeOverrideConf,
            minWordLevel = minWordLevel, minRelativeLevel = minRelativeLevel, echoRelativeLevel = echoRelativeLevel,
        };
        if (!manualDetails) DeriveFromBalance(balance, v);
        v.languageArbitration = languageArbitration;
        v.languageMargin = languageMargin;
        v.logRecognition = logRecognition;
        v.doubleCountSeconds = doubleCountSeconds;
        v.clientSendMinInterval = clientSendMinInterval;
        v.serverMinInterval = serverMinInterval;
        return v;
    }

    /// <summary>Denge'den hesaplanan değerleri ayrıntılı alanlara yazar (Inspector'da ne uygulandığı görünsün).</summary>
    [ContextMenu("Dengeyi ayrıntılı alanlara uygula")]
    public void ApplyBalanceToFields()
    {
        var v = new Values();
        DeriveFromBalance(balance, v);
        partialEnabled = v.partialEnabled;
        partialStableUpdates = v.partialStableUpdates;
        partialStableSeconds = v.partialStableSeconds;
        partialTailExtraSeconds = v.partialTailExtraSeconds;
        partialRequireAgreement = v.partialRequireAgreement;
        freeMinConf = v.freeMinConf;
        grammarMinConf = v.grammarMinConf;
        shortWordExtraConf = v.shortWordExtraConf;
        minSimilarity = v.minSimilarity;
        freeOverrideConf = v.freeOverrideConf;
        minWordLevel = v.minWordLevel;
        minRelativeLevel = v.minRelativeLevel;
        echoRelativeLevel = v.echoRelativeLevel;
    }

    /// <summary>Denge'den türetilen (manualDetails kapalıyken gri görünen) alan adları.</summary>
    public static readonly string[] DerivedFieldNames =
    {
        nameof(partialEnabled), nameof(partialStableUpdates), nameof(partialStableSeconds), nameof(partialTailExtraSeconds),
        nameof(partialRequireAgreement), nameof(freeMinConf), nameof(grammarMinConf), nameof(shortWordExtraConf),
        nameof(minSimilarity), nameof(freeOverrideConf), nameof(minWordLevel), nameof(minRelativeLevel), nameof(echoRelativeLevel),
    };

    private void OnValidate()
    {
        if (!manualDetails) ApplyBalanceToFields();
    }

    // ---------------- Erişim ----------------
    private static CrimeWordSettings active;

    /// <summary>Resources/CrimeWordSettings.asset; yoksa koddaki varsayılanlar. SADECE ana iş parçacığında çağırın.</summary>
    public static CrimeWordSettings Active
    {
        get
        {
            if (active == null)
            {
                active = Resources.Load<CrimeWordSettings>(ResourceName);
                if (active == null)
                {
                    active = CreateInstance<CrimeWordSettings>();
                    active.hideFlags = HideFlags.DontSave;
                    active.name = ResourceName + " (varsayılan)";
                }
            }
            return active;
        }
    }
}
