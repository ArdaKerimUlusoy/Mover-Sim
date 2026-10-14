using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// SESLİ SOHBETTE HIRSIZLIK KELİMELERİ: konuşurken "çal", "çaldım", "değiştirdim", "sahte", "soygun" / "steal", "swapped",
/// "fake", "heist" gibi bir şey söylersen yakındaki güvenlikler DUYAR: söylediğin yerin yanındaki esere (değiştirdiğin
/// bir eser varsa önce ona) gidip inceler, sahteyi fark etme şansı artar ve bir süre sana şüpheyle bakarlar (CrimeTalk).
///
///  - Konuşma tanıma: Vosk (çevrimdışı, ücretsiz). Türkçe + İngilizce küçük modeller (~80 MB). StreamingAssets/Vosk'ta
///    varsa sormadan kullanılır; yoksa ilk seferde oyuncuya SORULUR (Evet/Hayır, PlayerPrefs'e kaydedilir). Hayır derse
///    kelime algılama kapalı kalır. Elle koymak için: Assets/StreamingAssets/Vosk/vosk-model-small-tr-0.3 (ve ...-en-us-0.15).
///  - Sadece SESİN GERÇEKTEN GİTTİĞİ anlar dinlenir (V basılıyken ya da açık mikrofonda eşik geçilince).
///    Tanıma ayrı bir iş parçacığında, oyuncunun kendi bilgisayarında yapılır; ağdan sadece bulunan kelime sunucuya gider.
///  - Her zaman Türkçe + İngilizce birlikte dinlenir; dil karıştırılabilir (Türkçe cümlede "steal" de algılanır).
///  - Her dil için İKİ tanıyıcı: serbest (her şeyi yazıya döker) + KELİME LİSTELİ (sadece hırsızlık kelimeleri, gerisi "[unk]").
///  - İKİ yol: (1) ANINDA: ara (partial) sonuçta kelime kararlı görününce (cümle bitmeden), (2) KESİN: cümle bitince
///    güven eşikli sonuç (ilkinin kaçırdıklarını yakalar). Aynı söyleyiş iki kez sayılmaz (ReportGuard).
///  - Hız ↔ yanlış alarm dengesi: Assets/Resources/CrimeWordSettings.asset ("Denge" kaydırıcısı; Tools/Sahte Şaheser/Voice Filter Settings).
///  - Uzatılan kelimeler ("çaaaal", "hırsıııız", "steeeal") harf tekrarları sıkıştırılarak eşleşir.
///  - Algılanınca ekranda yazı çıkmaz; sadece sesli sohbetin alttaki kırmızı mikrofon kutusu yanar.
///  - Şu an yalnızca Windows'ta çalışır (Assets/Plugins/x86_64/libvosk.dll). Diğer sistemlerde sessizce kapalı kalır.
/// Sahneye bir şey eklemeye gerek yok: oyun açılınca kendiliğinden oluşur.
/// </summary>
public class VoiceKeywordListener : MonoBehaviour
{
    public static VoiceKeywordListener Instance { get; private set; }

    public enum Mode { Off = 0, Both = 1, Turkish = 2, English = 3 }

    private const string KeyMode = "SS_CrimeWords_Mode";
    private const string KeyConsent = "SS_CrimeWords_DownloadConsent"; // 1 = evet, 0 = hayır, yok = sor
    private const string MsgName = "SS_CrimeTalk";
    private const string CompleteMarker = ".sahte-saheser-complete";
    private const string LoadingMarker = ".sahte-saheser-loading"; // yükleme sürüyor; kalmışsa önceki deneme çöktü

    private static readonly string[] ModelNames = { "vosk-model-small-tr-0.3", "vosk-model-small-en-us-0.15" };
    private const string ModelUrlBase = "https://alphacephei.com/vosk/models/";

    // PlayerPrefs sadece ana iş parçacığında okunabilir: tanıma iş parçacığı bu kopyayı kullanır
    private static volatile int modeCache = -1;

    /// <summary>Artık ayar yok: her zaman Türkçe + İngilizce (eski kayıtlı "Kapalı" / tek dil ayarı yok sayılır).</summary>
    public static Mode CurrentMode
    {
        get { modeCache = (int)Mode.Both; return Mode.Both; }
        set { modeCache = (int)Mode.Both; }
    }

    /// <summary>Model indirme izni: null = henüz sorulmadı, true = indir, false = indirme (algılama kapalı).
    /// Bir ayar menüsünden değiştirilebilir (null yaparsanız bir sonraki oturumda yeniden sorulur).</summary>
    public static bool? DownloadConsent
    {
        get
        {
            if (!PlayerPrefs.HasKey(KeyConsent)) return null;
            return PlayerPrefs.GetInt(KeyConsent) == 1;
        }
        set
        {
            if (value == null) PlayerPrefs.DeleteKey(KeyConsent);
            else PlayerPrefs.SetInt(KeyConsent, value.Value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Algılama açık mı: "Hayır" (false) dendiyse tamamen kapalı (StreamingAssets'teki model de kullanılmaz);
    /// henüz sorulmadıysa ya da "Evet" dendiyse açık. Esc menüsündeki düğme DownloadConsent'i değiştirir.</summary>
    public static bool DetectionEnabled => DownloadConsent != false;

    private static bool Wants(int lang) => true;

    // Kelime listeli tanıyıcının dinlediği kelimeler (modelin sözlüğünde olmayanları Vosk kendisi atlar; uzatılmış
    // "çaaal" gibi yazımlar sözlükte olmadığı için listeye eklenmez: uzatılan ses yine en yakın kelimeye eşlenir).
    private static readonly string[][] GrammarWords =
    {
        new[]
        {
            "çal", "çalın", "çalar", "çalan", "çaldır", "çaldırdım", "çaldım", "çaldık", "çaldın", "çaldı", "çalalım",
            "çalacağım", "çalacağız", "çalıyorum", "çalıyoruz", "çalmak", "çalıp", "çalındı", "çalınmış", "çalıntı",
            "hırsız", "hırsızlık", "soygun", "soyalım", "soyduk", "soydum", "değiştirdim", "değiştirdik", "değiştirelim",
            "değiştir", "değiştiriyorum", "sahte", "sahtesi", "aşırdım", "arakladım", "yürüttüm",
        },
        new[]
        {
            "steal", "steals", "stealing", "stole", "stolen", "swap", "swapped", "swapping", "fake", "forgery", "forged",
            "heist", "rob", "robs", "robbed", "robbing", "robber", "robbery", "thief", "thieves", "loot", "looted", "looting",
        },
    };

    private static string GrammarJson(int lang)
    {
        var sb = new StringBuilder("[");
        foreach (string w in GrammarWords[lang]) sb.Append('"').Append(w).Append("\", ");
        sb.Append("\"[unk]\"]");
        return sb.ToString();
    }

    // ---------------- Ayarlar (Assets/Resources/CrimeWordSettings.asset) ----------------
    private volatile CrimeWordSettings.Values tuning;
    private float nextTuningRefresh;

    // ---------------- Durum ----------------

    private enum ModelState { Missing, Downloading, Extracting, Loading, Ready, Failed }

    private const int KindFreePartial = 0, KindGrammarPartial = 1, KindFreeFinal = 2, KindGrammarFinal = 3;

    private class LangModel
    {
        public string name;
        public int index;
        public volatile ModelState state = ModelState.Missing;
        public float progress;
        public string error = "";
        public string path;
        public IntPtr model = IntPtr.Zero;
        public IntPtr rec = IntPtr.Zero;
        public IntPtr grm = IntPtr.Zero;   // kelime listeli tanıyıcı (yoksa Zero)
        public UnityWebRequest req;
        public string zipPath;
        public float nextDirCheck;                         // ana iş parçacığı: model klasörü diskte bir sonraki arama anı
        public int loadFailures;                           // art arda yükleme hatası (üstel geri çekilme)
        public long retryAtTicks;                          // bu andan (UTC tick) önce yeniden deneme

        // --- Tanıma iş parçacığı durumu ---
        public long fed;                                   // tanıyıcılara verilen toplam örnek (Vosk kelime zamanları buna göre)
        public long offset;                                // ortak ses saati - fed (diller arası zaman karşılaştırması)
        public double freeCovered;                         // serbest tanıyıcının kesinleştirdiği son an (sn)
        public readonly List<VoskWord> recentFree = new List<VoskWord>(); // son ~20 sn'de serbest tanıyıcının duydukları
        public readonly List<Hit> pending = new List<Hit>();              // doğrulama bekleyen adaylar
        public readonly PartialTracker freeTrack = new PartialTracker(), grmTrack = new PartialTracker();
        public readonly RecentTokens freeSeen = new RecentTokens(), grmSeen = new RecentTokens(); // ara sonuç uyumu
        public const int Ring = 512;                       // ~20 sn'lik 40 ms'lik paket enerjisi
        public readonly long[] eStart = new long[Ring];
        public readonly int[] eLen = new int[Ring];
        public readonly float[] eLevel = new float[Ring];
        public readonly bool[] eEcho = new bool[Ring];
        public int eHead, eCount;

        public double OffsetSec => offset / (double)VoiceChat.Rate;
    }

    private struct AudioFrame { public short[] pcm; public float level; public bool echo; }

    private class Hit
    {
        public VoskWord w;
        public bool grammar;    // kelime listeli tanıyıcıdan mı
        public float level;     // kelime anındaki en yüksek ses seviyesi (-1 bilinmiyor)
    }

    private struct Found { public string word; public float level; }

    private readonly LangModel[] langs = new LangModel[2];
    private string persistentRoot, streamingRoot, pluginDir;
    private bool nativeOk, nativeTried;
    private string nativeError = "";

    // Ses: ana iş parçacığı → tanıma iş parçacığı
    private readonly ConcurrentQueue<AudioFrame> audio = new ConcurrentQueue<AudioFrame>();
    private readonly ConcurrentQueue<Found> found = new ConcurrentQueue<Found>();
    private readonly ReportGuard guard = new ReportGuard(); // iş parçacığı: aynı söyleyişin çift sayımı
    private volatile float typicalForWorker = 0.40f;
    private readonly AutoResetEvent wake = new AutoResetEvent(false);
    private Thread worker;
    private volatile bool running;
    private int workerGen;          // her başlatmada artar: eski iş parçacığı kendi kuşağı geçince çıkar
    private volatile bool destroyed; // OnDestroy sonrası yüklenen model hemen serbest bırakılır
    private long lastAudioTicks;
    private long globalFed;          // iş parçacığı: işlenen toplam örnek (ortak ses saati)
    private int utteranceIndex;      // iş parçacığı: her cümle kapanışında artar (ReportGuard söyleyişleri buna göre ayırır)

    // Ağ
    private CustomMessagingManager regCmm;
    private float nextSend;
    private float nextRetry;
    private bool inSession;

    // İndirme izni penceresi
    private bool askConsent;
    private bool cursorWasLocked;

    // Ekranda (sadece kendine): algılanan kelime
    private float shownLoud;
    private GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("VoiceKeywordListener");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceKeywordListener>();
    }

    private void Awake()
    {
        Instance = this;
        persistentRoot = Path.Combine(Application.persistentDataPath, "VoskModels");
        streamingRoot = Path.Combine(Application.streamingAssetsPath, "Vosk");
        pluginDir = Path.Combine(Application.dataPath, "Plugins", "x86_64");
        for (int i = 0; i < 2; i++) langs[i] = new LangModel { name = ModelNames[i], index = i };
        tuning = CrimeWordSettings.Active.Resolve();
        VoiceChat.LocalSpeechFrame += OnSpeechFrame;
    }

    private void OnDestroy()
    {
        VoiceChat.LocalSpeechFrame -= OnSpeechFrame;
        UnregisterMessages();
        destroyed = true;
        StopWorker();
        foreach (LangModel l in langs) if (l != null && l.req != null) l.req.Dispose();
        // Native bellek (model başına 50-100+ MB): iş parçacığı bittiyse serbest bırak. Hâlâ bir native çağrının
        // içindeyse BIRAKMA (çökme riski); o durumda süreç kapanınca işletim sistemi temizler.
        if (WorkerFinished) foreach (LangModel l in langs) FreeNative(l);
        if (Instance == this) Instance = null;
    }

    private static void FreeNative(LangModel l)
    {
        if (l == null) return;
        lock (l)
        {
            try
            {
                if (l.grm != IntPtr.Zero) Native.vosk_recognizer_free(l.grm);
                if (l.rec != IntPtr.Zero) Native.vosk_recognizer_free(l.rec);
                if (l.model != IntPtr.Zero) Native.vosk_model_free(l.model);
            }
            catch (Exception) { }
            l.grm = l.rec = l.model = IntPtr.Zero;
            l.state = ModelState.Missing;
        }
    }

    // =====================================================================
    //  Ses girişi (VoiceChat gönderdiği her 40 ms'lik paketi buraya da verir)
    // =====================================================================

    // Son ~1.6 sn'nin ses yüksekliği (40 ms'lik paketler): kelimenin fısıltıyla mı bağırarak mı söylendiği
    private readonly float[] recentLevels = new float[40];
    private readonly float[] recentTimes = new float[40];
    private int recentHead;

    // Bu oyuncunun normal konuşma seviyesi (zamanla öğrenilir): mikrofonu kısık / yüksek olan herkes için adil
    private float typicalLevel = 0.40f; // başlangıç tahmini düşük tutulur: kısık mikrofon ilk dakikada da algılanır

    /// <summary>Son ~1.2 sn'deki en yüksek ses, oyuncunun KENDİ normal sesine göre (0 = fısıltı, 0.5 = normal, 1 = bağırma).</summary>
    private float RecentLoudness()
    {
        float best = 0f, now = Time.unscaledTime;
        for (int i = 0; i < recentLevels.Length; i++)
            if (now - recentTimes[i] < 1.2f && recentLevels[i] > best) best = recentLevels[i];
        float t = Mathf.Max(0.15f, typicalLevel);
        return Mathf.Clamp01(Mathf.InverseLerp(t * 0.55f, t * 1.45f, best));
    }

    private void OnSpeechFrame(float[] samples, int count)
    {
        // Ses yüksekliği (her zaman kaydedilir; tanıma kapalıyken de ucuz)
        double sum = 0;
        for (int i = 0; i < count; i++) sum += samples[i] * samples[i];
        float rms = count > 0 ? (float)Math.Sqrt(sum / count) : 0f;
        float lvl = Mathf.Clamp01(Mathf.Sqrt(rms) * 2.2f);
        recentLevels[recentHead] = lvl;
        // Konuşurken yavaşça öğren. Normal sesin yarısının altındaki paketler (sessiz mikrofon / duraklama) çok daha yavaş
        // çeker: kısık mikrofonlu oyuncunun "normal"i de zamanla aşağı iner (eskiden 0.12 altı hiç öğrenilmiyordu).
        if (lvl > 0.03f) typicalLevel = Mathf.Max(0.06f, Mathf.Lerp(typicalLevel, lvl, lvl >= typicalLevel * 0.5f ? 0.01f : 0.002f));
        recentTimes[recentHead] = Time.unscaledTime;
        recentHead = (recentHead + 1) % recentLevels.Length;

        typicalForWorker = typicalLevel;
        if (!running || CurrentMode == Mode.Off) return;
        var pcm = new short[count];
        for (int i = 0; i < count; i++) pcm[i] = (short)Mathf.Clamp(samples[i] * 32767f, -32768f, 32767f);
        if (audio.Count > 150) audio.TryDequeue(out _); // iş parçacığı yetişemiyorsa en eskiyi at
        // Hoparlörden başka oyuncu duyuluyorsa onun sesi mikrofona sızabilir: o anlar için daha yüksek ses istenir
        bool echo = VoiceChat.Instance != null && VoiceChat.Instance.AnyRemoteAudibleSpeaking;
        audio.Enqueue(new AudioFrame { pcm = pcm, level = lvl, echo = echo });
        Interlocked.Exchange(ref lastAudioTicks, DateTime.UtcNow.Ticks);
        wake.Set();
    }

    // =====================================================================
    //  Ana döngü
    // =====================================================================

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        inSession = nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient) && nm.CustomMessagingManager != null;

        if (inSession) { if (regCmm != nm.CustomMessagingManager) RegisterMessages(nm); }
        else if (regCmm != null) UnregisterMessages();

        // Ayarlar: Play sırasında Inspector'dan değiştirilirse de uygulanır
        if (Time.unscaledTime >= nextTuningRefresh)
        {
            nextTuningRefresh = Time.unscaledTime + 0.5f;
            tuning = CrimeWordSettings.Active.Resolve();
        }

        if (!SupportedPlatform)
        {
            if (running) StopWorker();
            return;
        }

        // İndirme başarısız olduysa (ör. o an internet yoktu) yarım dakikada bir yeniden dene
        if (Time.unscaledTime >= nextRetry)
        {
            nextRetry = Time.unscaledTime + 30f;
            RetryFailed();
        }

        // Oyundayken ve sesli sohbet varken modelleri hazırla (izin verildiyse indir)
        if (inSession) PrepareModels();
        else askConsent = false;
        UpdateConsentCursor();

        bool enabled = DetectionEnabled;
        if (!running && enabled && AnyReadyForMode()) StartWorker();
        if (running && (!enabled || !AnyReadyForMode())) StopWorker();

        // Bulunan kelimeler → sunucuya (her söyleyiş; sadece çok kısa taşkın koruması)
        CrimeWordSettings.Values v = tuning;
        while (found.TryDequeue(out Found f))
        {
            string word = f.word;
            // Kelimenin söylendiği andaki ses (kesin sonuç ~0.5-1 sn sonra geldiği için "son 1.2 sn" yerine)
            if (f.level >= 0f)
            {
                float t = Mathf.Max(0.15f, typicalLevel);
                shownLoud = Mathf.Clamp01(Mathf.InverseLerp(t * 0.55f, t * 1.45f, f.level));
            }
            else shownLoud = RecentLoudness();
            VoiceChat.BannedFlashUntil = Time.unscaledTime + 2.5f; // mikrofon kutusu kırmızı yanar
            if (inSession && Time.unscaledTime >= nextSend)
            {
                nextSend = Time.unscaledTime + v.clientSendMinInterval;
                SendToServer(nm, word, shownLoud);
            }
        }
    }

    private static bool SupportedPlatform
    {
        get
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return true;
#else
            return false;
#endif
        }
    }

    private bool AnyReadyForMode()
    {
        for (int i = 0; i < 2; i++) if (Wants(i) && langs[i].state == ModelState.Ready) return true;
        return false;
    }

    // =====================================================================
    //  Modeller: StreamingAssets'te varsa oradan, yoksa (izinle) indir → geçici klasöre aç → taşı → yükle
    // =====================================================================

    private void PrepareModels()
    {
        if (!EnsureNative()) return;

        bool needConsent = false;
        for (int i = 0; i < 2; i++)
        {
            LangModel l = langs[i];
            if (!Wants(i)) continue;

            switch (l.state)
            {
                case ModelState.Missing:
                    bool? consent = DownloadConsent;
                    if (consent == false) break; // algılama kapalı: diske de bakma
                    // Disk kontrolü (4-8 Exists) her karede değil, birkaç sn'de bir (onay beklenirken her kare çağrılıyordu)
                    if (Time.unscaledTime < l.nextDirCheck) { if (consent == null) needConsent = true; break; }
                    l.nextDirCheck = Time.unscaledTime + 3f;
                    string dir = FindModelDir(l.name, out bool unverified);
                    if (dir != null) { BeginLoad(l, dir, unverified); break; }
                    if (consent == true) BeginDownload(l);
                    else needConsent = true; // sor
                    break;

                case ModelState.Downloading:
                    if (l.req == null) { l.state = ModelState.Failed; break; }
                    if (DownloadConsent == false)
                    {
                        // Esc menüsünden kapatıldı: süren indirmeyi iptal et (yarım dosya silinir: removeFileOnAbort)
                        l.req.Abort();
                        l.req.Dispose();
                        l.req = null;
                        l.state = ModelState.Missing;
                        break;
                    }
                    l.progress = l.req.downloadProgress;
                    if (!l.req.isDone) break;
                    if (l.req.result != UnityWebRequest.Result.Success)
                    {
                        l.error = l.req.error;
                        l.state = ModelState.Failed;
                        Debug.LogWarning($"[CrimeTalk] {l.name} indirilemedi: {l.req.error}. Elle koymak için: StreamingAssets/Vosk/{l.name}");
                    }
                    else BeginExtract(l);
                    l.req.Dispose();
                    l.req = null;
                    break;
            }
        }
        askConsent = needConsent;
    }

    /// <param name="unverified">true: persistentDataPath'te "tamamlandı" işareti OLMAYAN ama çekirdek dosyaları tam bir
    /// model (eski sürümün indirdiği). Yüklemeyi dener; başarılı olursa işaret yazılır, olmazsa geri çekilmeyle yeniden
    /// denenir (klasör silinmez; bkz. OnLoadFailed).</param>
    private string FindModelDir(string name, out bool unverified)
    {
        unverified = false;
        // StreamingAssets: geliştiricinin elle koyduğu model (tamamlandı işareti aranmaz).
        // persistentDataPath: oyunun indirdiği model ("tamamlandı" işaretli ya da çekirdek dosyaları tam olan).
        try { string d = Path.Combine(streamingRoot, name); if (IsModelDir(d, false)) return d; } catch (Exception) { }
        try
        {
            string d = Path.Combine(persistentRoot, name);
            if (File.Exists(Path.Combine(d, LoadingMarker)))
            {
                // Önceki yükleme süreci çökertti (ya da yükleme sırasında oyun kapatıldı): kendiliğinden yükleme.
                // İzin varsa yeniden indirilir (açma eski klasörü değiştirir); yoksa elle müdahaleye bırakılır.
                if (!loadingWarned.Contains(name))
                {
                    loadingWarned.Add(name);
                    Debug.LogWarning($"[CrimeTalk] {name}: önceki yükleme yarım kaldı ({LoadingMarker} var); model otomatik yüklenmeyecek. Klasör: {d}");
                }
                return null;
            }
            if (IsModelDir(d, true)) return d;
            if (HasCoreFiles(d)) { unverified = true; return d; }
        }
        catch (Exception) { }
        return null;
    }

    private readonly HashSet<string> loadingWarned = new HashSet<string>();

    /// <summary>
    /// Vosk küçük modelinin çekirdek dosyaları tam mı (iki düzen de: yeni "am/ conf/ graph/" ve eski düz klasör; ör.
    /// en-us-0.15: am/final.mdl, conf/mfcc.conf, graph/HCLr.fst+Gr.fst, ivector/final.ie; tr-0.3: final.mdl, mfcc.conf,
    /// HCLr.fst+Gr.fst, ivector/final.ie). Büyük dosyalar boş / çok küçük olmamalı (yarım açılmış zip).
    /// </summary>
    internal static bool HasCoreFiles(string dir)
    {
        if (!Directory.Exists(dir)) return false;
        bool Big(params string[] rel)
        {
            foreach (string r in rel)
            {
                var f = new FileInfo(Path.Combine(dir, r));
                if (f.Exists && f.Length > 100 * 1024) return true;
            }
            return false;
        }
        bool Any(params string[] rel)
        {
            foreach (string r in rel) if (File.Exists(Path.Combine(dir, r))) return true;
            return false;
        }
        // Grafik: "lookahead" (HCLr + Gr, kelimeler Gr.fst içinde; words.txt YOK — iki küçük model de böyle) ya da
        // klasik HCLG (o zaman words.txt şart)
        bool lookahead = ((Big(Path.Combine("graph", "HCLr.fst")) && Big(Path.Combine("graph", "Gr.fst")))
                          || (Big("HCLr.fst") && Big("Gr.fst")))
                         && Any(Path.Combine("graph", "disambig_tid.int"), "disambig_tid.int");
        bool hclg = Big(Path.Combine("graph", "HCLG.fst"), "HCLG.fst")
                    && Any(Path.Combine("graph", "words.txt"), "words.txt");
        return Big(Path.Combine("am", "final.mdl"), "final.mdl")
               && Any(Path.Combine("conf", "mfcc.conf"), "mfcc.conf")
               && (lookahead || hclg)
               && Any(Path.Combine("graph", "phones", "word_boundary.int"), "word_boundary.int") // kelime zamanları (SetWords)
               && Big(Path.Combine("ivector", "final.ie"));
    }

    private static bool IsModelContent(string dir) =>
        Directory.Exists(dir) && (Directory.Exists(Path.Combine(dir, "am")) || File.Exists(Path.Combine(dir, "final.mdl")));

    private static bool IsModelDir(string dir, bool requireMarker) =>
        IsModelContent(dir) && (!requireMarker || File.Exists(Path.Combine(dir, CompleteMarker)));

    private void BeginDownload(LangModel l)
    {
        try { Directory.CreateDirectory(persistentRoot); }
        catch (Exception e) { l.error = e.Message; l.state = ModelState.Failed; return; }

        l.zipPath = Path.Combine(persistentRoot, l.name + ".zip");
        l.req = new UnityWebRequest(ModelUrlBase + l.name + ".zip", UnityWebRequest.kHttpVerbGET)
        {
            downloadHandler = new DownloadHandlerFile(l.zipPath) { removeFileOnAbort = true }
        };
        l.req.SendWebRequest();
        l.state = ModelState.Downloading;
        l.progress = 0f;
        Debug.Log($"[CrimeTalk] Konuşma modeli indiriliyor: {l.name}");
    }

    /// <summary>
    /// Zip'i GEÇİCİ bir klasöre açar ("&lt;ad&gt;.partial"), içeriği doğrular, "tamamlandı" işareti yazar ve ancak ondan
    /// sonra asıl klasöre taşır: oyun açma sırasında kapanırsa yarım model asla "geçerli" sayılmaz.
    /// </summary>
    private void BeginExtract(LangModel l)
    {
        l.state = ModelState.Extracting;
        string zip = l.zipPath, root = persistentRoot, name = l.name;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string tmp = Path.Combine(root, name + ".partial");
            try
            {
                string target = Path.Combine(root, name);
                TryDeleteDir(tmp);
                Directory.CreateDirectory(tmp);
                ExtractZipSafe(zip, tmp);

                // Model zip'te "<ad>/" klasörü altında (normal) ya da doğrudan kökte olabilir
                string extracted = Path.Combine(tmp, name);
                if (!IsModelContent(extracted))
                {
                    if (IsModelContent(tmp)) extracted = tmp;
                    else throw new Exception("model klasörü bulunamadı");
                }
                File.WriteAllText(Path.Combine(extracted, CompleteMarker), DateTime.UtcNow.ToString("o"));

                TryDeleteDir(target); // önceki sürümün yarım kalmış (işaretsiz) kopyası
                Directory.Move(extracted, target);
                if (extracted != tmp) TryDeleteDir(tmp);
                try { File.Delete(zip); } catch (Exception) { }
                LoadModelBlocking(l, target, true);
            }
            catch (Exception ex)
            {
                TryDeleteDir(tmp);
                OnLoadFailed(l, ex);
            }
        });
    }

    /// <summary>Zip'i dest altına açar. Zip-slip: her girdinin tam yolu "dest + dizin ayırıcı" ile başlamalı
    /// ("VoskModels-evil" gibi kardeş klasörler de reddedilir).</summary>
    internal static void ExtractZipSafe(string zip, string dest)
    {
        string destFull = Path.GetFullPath(dest);
        if (!destFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)) destFull += Path.DirectorySeparatorChar;
        using (FileStream fs = File.OpenRead(zip))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            foreach (ZipArchiveEntry e in archive.Entries)
            {
                string full = Path.GetFullPath(Path.Combine(destFull, e.FullName));
                if (!full.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("güvensiz zip girdisi: " + e.FullName);
                if (string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(full); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                using (Stream src = e.Open())
                using (FileStream dst = File.Create(full))
                    src.CopyTo(dst);
            }
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
    }

    private void BeginLoad(LangModel l, string dir, bool unverified)
    {
        // Çökme koruması sadece oyunun indirdiği (persistent) modeller için: StreamingAssets salt okunur olabilir
        bool crashGuard = dir.StartsWith(persistentRoot, StringComparison.OrdinalIgnoreCase);
        l.state = ModelState.Loading;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                bool loaded = LoadModelBlocking(l, dir, crashGuard);
                // Eski sürümün işaretsiz (ama sağlam) modeli: GERÇEKTEN yüklendiyse işaretle (bileşen yok edildiyse değil)
                if (loaded && unverified)
                {
                    try { File.WriteAllText(Path.Combine(dir, CompleteMarker), DateTime.UtcNow.ToString("o")); } catch (Exception) { }
                }
            }
            catch (Exception ex) { OnLoadFailed(l, ex); }
        });
    }

    /// <summary>
    /// İş parçacığında: modeli ve tanıyıcıyı oluşturur (birkaç saniye sürebilir). Yüklendiyse true; bileşen bu arada yok
    /// edildiyse false (kaynaklar serbest bırakılır). crashGuard: yükleme süresince ".loading" işareti tutulur; native çökme
    /// olursa işaret kalır ve bir sonraki açılışta bu model kendiliğinden yüklenmez (çökme döngüsü olmasın).
    /// </summary>
    private bool LoadModelBlocking(LangModel l, string dir, bool crashGuard)
    {
        string loading = Path.Combine(dir, LoadingMarker);
        if (crashGuard) { try { File.WriteAllText(loading, DateTime.UtcNow.ToString("o")); } catch (Exception) { } }
        try { return LoadModelCore(l, dir); }
        finally
        {
            // Yönetilen hata da olsa işaret kalkar (sadece süreç çökerse kalır)
            if (crashGuard) { try { File.Delete(loading); } catch (Exception) { } }
        }
    }

    /// <summary>
    /// Vosk (Kaldi) model yolunu dar karakterli (char*) açar; Windows'ta bu çoğunlukla sistemin ANSI kod sayfası demektir.
    /// Yol ASCII değilse (ör. "C:\Users\Müjde\...") UTF-8 baytları yanlış okunabilir: önce 8.3 kısa yolu (ASCII ise),
    /// olmazsa UTF-8, en son ANSI kod sayfasını dener.
    /// </summary>
    private static IEnumerable<byte[]> PathCandidates(string dir)
    {
        if (IsAscii(dir)) { yield return Utf8(dir); yield break; }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string shortPath = null;
        try
        {
            var sb = new StringBuilder(1024);
            if (Native.GetShortPathNameW(dir, sb, sb.Capacity) > 0) shortPath = sb.ToString();
        }
        catch (Exception) { }
        if (!string.IsNullOrEmpty(shortPath) && IsAscii(shortPath)) yield return Utf8(shortPath);
#endif
        yield return Utf8(dir);
        byte[] ansi = null;
        try
        {
            Encoding enc = Encoding.GetEncoding(Native.GetACP());
            byte[] b = enc.GetBytes(dir);
            ansi = new byte[b.Length + 1];
            Array.Copy(b, ansi, b.Length);
        }
        catch (Exception) { }
        if (ansi != null) yield return ansi;
    }

    private static bool IsAscii(string s)
    {
        foreach (char c in s) if (c > 127) return false;
        return true;
    }

    private bool LoadModelCore(LangModel l, string dir)
    {
        l.state = ModelState.Loading;
        l.path = dir;
        IntPtr model = IntPtr.Zero;
        foreach (byte[] p in PathCandidates(dir))
        {
            model = Native.vosk_model_new(p);
            if (model != IntPtr.Zero) break;
        }
        if (model == IntPtr.Zero)
            throw new Exception("model yüklenemedi: " + dir + (IsAscii(dir) ? "" : " (yolda ASCII dışı karakter var)"));
        IntPtr rec = Native.vosk_recognizer_new(model, VoiceChat.Rate);
        if (rec == IntPtr.Zero) { Native.vosk_model_free(model); throw new Exception("tanıyıcı oluşturulamadı"); }
        // Kelime listeli tanıyıcı (olmazsa sadece serbest tanıyıcıyla devam)
        IntPtr grm = IntPtr.Zero;
        try { grm = Native.vosk_recognizer_new_grm(model, VoiceChat.Rate, Utf8(GrammarJson(l.index))); }
        catch (Exception) { grm = IntPtr.Zero; }
        // Kelime başına güven (conf) + başlangıç/bitiş zamanı: yanlış alarm filtresinin temeli
        Native.vosk_recognizer_set_words(rec, 1);
        if (grm != IntPtr.Zero) Native.vosk_recognizer_set_words(grm, 1);
        lock (l)
        {
            if (destroyed)
            {
                // Yükleme sürerken oyun kapandı / bileşen yok edildi: sızdırma
                if (grm != IntPtr.Zero) Native.vosk_recognizer_free(grm);
                Native.vosk_recognizer_free(rec);
                Native.vosk_model_free(model);
                return false;
            }
            // Yeni tanıyıcının zaman ekseni 0'dan başlar: iş parçacığı durumunu sıfırla (yeniden yüklemede eskisi kalmasın).
            // Durum Ready olmadan iş parçacığı bu alanlara dokunmaz.
            l.fed = 0;
            l.freeCovered = 0;
            l.eHead = l.eCount = 0;
            l.recentFree.Clear();
            l.pending.Clear();
            l.freeTrack.Reset();
            l.grmTrack.Reset();
            l.freeSeen.Clear();
            l.grmSeen.Clear();
            l.model = model;
            l.rec = rec;
            l.grm = grm;
            l.loadFailures = 0;
            Interlocked.Exchange(ref l.retryAtTicks, 0);
            l.state = ModelState.Ready;
            return true;
        }
    }

    /// <summary>
    /// Yükleme / açma başarısız: klasörü SİLME (yol karakterleri gibi çevresel bir neden olabilir). Üstel geri çekilme:
    /// 1. hata → 30 sn sonra, 2. → 5 dk sonra yeniden dene, 3. → bırak (günlüğe yaz; elle müdahale ya da yeni indirme).
    /// </summary>
    private static void OnLoadFailed(LangModel l, Exception ex)
    {
        l.loadFailures++;
        long wait = l.loadFailures == 1 ? TimeSpan.FromSeconds(30).Ticks
                  : l.loadFailures == 2 ? TimeSpan.FromMinutes(5).Ticks
                  : long.MaxValue / 2;
        Interlocked.Exchange(ref l.retryAtTicks, DateTime.UtcNow.Ticks + wait);
        l.error = ex.Message;
        l.state = ModelState.Failed;
        if (l.loadFailures >= 3)
            Debug.LogWarning($"[CrimeTalk] {l.name} 3 kez yüklenemedi ({ex.Message}); yeniden denenmeyecek. " +
                             $"Klasörü silip oyunu yeniden açarsanız model yeniden indirilir: {l.path}");
    }

    // =====================================================================
    //  Yerel kütüphane (libvosk.dll + bağımlılıkları)
    // =====================================================================

    private bool EnsureNative()
    {
        if (nativeTried) return nativeOk;
        nativeTried = true;
        try
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Bağımlılıkları sırayla önceden yükle: Windows aynı klasörde aramayabilir
            foreach (string dll in new[] { "libwinpthread-1.dll", "libgcc_s_seh-1.dll", "libstdc++-6.dll", "libvosk.dll" })
            {
                string p = Path.Combine(pluginDir, dll);
                if (File.Exists(p)) Native.LoadLibraryW(p);
            }
#endif
            Native.vosk_set_log_level(-1);
            nativeOk = true;
        }
        catch (Exception e)
        {
            nativeOk = false;
            nativeError = e.GetType().Name;
            Debug.LogWarning("[CrimeTalk] Konuşma tanıma kütüphanesi yüklenemedi (Assets/Plugins/x86_64/libvosk.dll): " + e.Message);
        }
        return nativeOk;
    }

    private static byte[] Utf8(string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        Array.Resize(ref b, b.Length + 1);
        return b;
    }

    private static string FromUtf8(IntPtr p)
    {
        if (p == IntPtr.Zero) return "";
        int len = 0;
        while (Marshal.ReadByte(p, len) != 0) len++;
        byte[] b = new byte[len];
        Marshal.Copy(p, b, 0, len);
        return Encoding.UTF8.GetString(b);
    }

    private static class Native
    {
        private const string Lib = "libvosk";

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetShortPathNameW(string longPath, StringBuilder shortPath, int bufferLength);

        [DllImport("kernel32")]
        public static extern int GetACP();

        [DllImport(Lib)] public static extern void vosk_set_log_level(int level);
        [DllImport(Lib)] public static extern IntPtr vosk_model_new(byte[] path);
        [DllImport(Lib)] public static extern void vosk_model_free(IntPtr model);
        [DllImport(Lib)] public static extern IntPtr vosk_recognizer_new(IntPtr model, float sampleRate);
        [DllImport(Lib)] public static extern IntPtr vosk_recognizer_new_grm(IntPtr model, float sampleRate, byte[] grammar);
        [DllImport(Lib)] public static extern void vosk_recognizer_free(IntPtr rec);
        [DllImport(Lib)] public static extern void vosk_recognizer_set_words(IntPtr rec, int words);
        [DllImport(Lib)] public static extern int vosk_recognizer_accept_waveform_s(IntPtr rec, short[] data, int length);
        [DllImport(Lib)] public static extern IntPtr vosk_recognizer_result(IntPtr rec);
        [DllImport(Lib)] public static extern IntPtr vosk_recognizer_partial_result(IntPtr rec);
        [DllImport(Lib)] public static extern IntPtr vosk_recognizer_final_result(IntPtr rec);
        [DllImport(Lib)] public static extern void vosk_recognizer_reset(IntPtr rec);
    }

    // =====================================================================
    //  Tanıma iş parçacığı
    // =====================================================================

    private void StartWorker()
    {
        // Eski iş parçacığı (ör. uzun bir native çağrıda) hâlâ yaşıyorsa yenisini BAŞLATMA: iki iş parçacığı aynı
        // tanıyıcıyı kullanırsa çöker. Update bir sonraki karede yeniden dener.
        if (worker != null)
        {
            if (worker.IsAlive) return;
            worker = null;
        }
        while (audio.TryDequeue(out _)) { }
        int gen = Interlocked.Increment(ref workerGen);
        running = true;
        worker = new Thread(() => WorkerLoop(gen)) { IsBackground = true, Name = "CrimeTalk-ASR" };
        worker.Start();
    }

    /// <summary>İş parçacığını durdurur. 500 ms'de bitmezse referansı tutulur (StartWorker o bitene kadar yenisini açmaz).</summary>
    private void StopWorker()
    {
        running = false;
        Interlocked.Increment(ref workerGen); // eski iş parçacığı bir sonraki turda kesin çıkar
        wake.Set();
        if (worker != null && worker.IsAlive) worker.Join(500);
        if (worker != null && !worker.IsAlive) worker = null;
    }

    private bool WorkerFinished => worker == null || !worker.IsAlive;

    private double StreamNow => globalFed / (double)VoiceChat.Rate;

    /// <summary>
    /// İKİ YOL:
    ///  A) ANINDA (ara/partial sonuç, CrimeWordSettings.partialEnabled): her 40 ms'de iki tanıyıcının ara metni okunur.
    ///     Suç kelimesi art arda "partialStableUpdates" güncelleme ve "partialStableSeconds" sn görünürse (son kelimeyse ya
    ///     da çok kısaysa + "partialTailExtraSeconds") aday olur. Kelime listeli tanıyıcının adayı ancak serbest tanıyıcı da
    ///     benzer bir şey duyduysa, serbest tanıyıcının adayı (partialRequireAgreement açıksa) kelime listeli tanıyıcı da
    ///     benzer bir suç kelimesi duyduysa sayılır. Ses kapısı her iki yolda da uygulanır.
    ///  B) KESİN (cümle sonu): kelime başına güven eşiği, kelime listeli ↔ serbest çapraz doğrulama, ses kapısı,
    ///     isteğe bağlı dil hakemliği (varsayılan KAPALI: dil karıştıran oyuncunun kelimesi kaybolmasın).
    /// Aynı söyleyiş iki yoldan / iki tanıyıcıdan gelirse ReportGuard bir kez sayar; farklı söyleyişler hep bildirilir.
    /// </summary>
    private void WorkerLoop(int gen)
    {
        bool inUtterance = false;
        while (running && gen == Volatile.Read(ref workerGen))
        {
            CrimeWordSettings.Values v = tuning;
            if (!audio.TryDequeue(out AudioFrame frame))
            {
                // Konuşma bitti (0.8 sn ses gelmedi): cümleyi kapat, bekleyen adayları karara bağla
                long idle = DateTime.UtcNow.Ticks - Interlocked.Read(ref lastAudioTicks);
                if (inUtterance && idle > TimeSpan.TicksPerMillisecond * 800)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        LangModel l = langs[i];
                        if (l.state != ModelState.Ready || !Wants(i)) continue;
                        try
                        {
                            if (l.grm != IntPtr.Zero) TakeGrammar(l, Native.vosk_recognizer_final_result(l.grm), v);
                            TakeFree(l, Native.vosk_recognizer_final_result(l.rec), v);
                        }
                        catch (Exception) { l.state = ModelState.Failed; l.error = "recognizer"; }
                        l.freeTrack.Reset();
                        l.grmTrack.Reset();
                    }
                    for (int i = 0; i < 2; i++) if (langs[i].state == ModelState.Ready) { try { Resolve(i, true, v); } catch (Exception) { langs[i].pending.Clear(); } }
                    // Söyleyiş bitti: sonraki söyleyiş ne zaman olursa olsun AYRI sayılır. (Ses saati yalnızca ses gittiği
                    // anlarda ilerler; aradaki sessizlik eksende yok, iki söyleyiş 0.4 sn arayla görünebilirdi.)
                    // Uyum jetonları da temizlenir: önceki cümlenin kelimeleri yeni cümleyi onaylamasın.
                    for (int i = 0; i < 2; i++)
                        if (langs[i].state == ModelState.Ready) { langs[i].freeSeen.Clear(); langs[i].grmSeen.Clear(); } // yüklenmekte olana dokunma
                    utteranceIndex++;
                    inUtterance = false;
                }
                wake.WaitOne(200);
                continue;
            }

            inUtterance = true;
            short[] pcm = frame.pcm;
            globalFed += pcm.Length;
            double now = StreamNow;
            for (int i = 0; i < 2; i++)
            {
                LangModel l = langs[i];
                if (l.state != ModelState.Ready || !Wants(i)) continue;
                try
                {
                    l.fed += pcm.Length;
                    l.offset = globalFed - l.fed;
                    RecordEnergy(l, pcm.Length, frame.level, frame.echo);

                    // Kelime listeli tanıyıcı: sadece hırsızlık kelimelerini arar (kısa / hızlı söylenenleri de yakalar)
                    string grmPartial = null;
                    if (l.grm != IntPtr.Zero)
                    {
                        if (Native.vosk_recognizer_accept_waveform_s(l.grm, pcm, pcm.Length) != 0)
                        {
                            TakeGrammar(l, Native.vosk_recognizer_result(l.grm), v);
                            l.grmTrack.Reset();
                        }
                        else if (v.partialEnabled) grmPartial = VoskJson.ParsePartial(FromUtf8(Native.vosk_recognizer_partial_result(l.grm)));
                    }

                    string freePartial = null;
                    if (Native.vosk_recognizer_accept_waveform_s(l.rec, pcm, pcm.Length) != 0)
                    {
                        TakeFree(l, Native.vosk_recognizer_result(l.rec), v); // cümle bitti (kısa sessizlik)
                        l.freeTrack.Reset();
                    }
                    else if (v.partialEnabled) freePartial = VoskJson.ParsePartial(FromUtf8(Native.vosk_recognizer_partial_result(l.rec)));

                    // Yönetilen koddaki (ara sonuç izleme) bir hata dili kapatmasın: sadece bu cümlenin ara durumunu sıfırla
                    if (v.partialEnabled)
                    {
                        try { ProcessPartials(l, freePartial, grmPartial, now, v); }
                        catch (Exception) { l.freeTrack.Reset(); l.grmTrack.Reset(); }
                    }
                }
                catch (Exception)
                {
                    l.state = ModelState.Failed;
                    l.error = "recognizer";
                }
            }
            for (int i = 0; i < 2; i++) if (langs[i].state == ModelState.Ready) { try { Resolve(i, false, v); } catch (Exception) { langs[i].pending.Clear(); } }
        }
    }

    // ---------------- A) Anında algılama (ara sonuçlar) ----------------

    private const double AgreementWindow = 1.5; // sn: diğer tanıyıcının bu kadar yakın zamanda duyduğu kelimeler uyum sayılır

    private void ProcessPartials(LangModel l, string freeText, string grmText, double now, CrimeWordSettings.Values v)
    {
        string[] freeTokens = PartialTracker.Tokenize(freeText);
        string[] grmTokens = PartialTracker.Tokenize(grmText);
        l.freeSeen.Add(freeTokens, now, true);
        l.grmSeen.Add(grmTokens, now, false);

        // Aday ancak kapılardan geçince "ateşlendi" sayılır; uyum ya da ses kapısında elenirse sonraki güncellemelerde
        // (kelime ara sonuçta kaldıkça, cümle bitene kadar) yeniden denenir.
        if (freeText != null)
            foreach (PartialHit h in l.freeTrack.Update(freeTokens, l.index, now, v.partialStableUpdates, v.partialStableSeconds, v.partialTailExtraSeconds))
            {
                if (v.partialRequireAgreement && l.grm != IntPtr.Zero &&
                    !l.grmSeen.Agrees(h.word, now, AgreementWindow, v.minSimilarity, l.index, true)) continue;
                if (ReportPartial(l, h, KindFreePartial, now, v)) l.freeTrack.MarkFired(h.key);
            }

        if (grmText != null)
            foreach (PartialHit h in l.grmTrack.Update(grmTokens, l.index, now, v.partialStableUpdates, v.partialStableSeconds, v.partialTailExtraSeconds))
            {
                // Kelime listeli tanıyıcı her sesi listeye zorlar: serbest tanıyıcı da benzer bir şey duymuş olmalı
                if (!l.freeSeen.Agrees(h.word, now, AgreementWindow, v.minSimilarity, l.index, false)) continue;
                if (ReportPartial(l, h, KindGrammarPartial, now, v)) l.grmTrack.MarkFired(h.key);
            }
    }

    /// <summary>Ses kapısından geçerse bildirir (ya da ReportGuard aynı söyleyiş diye yutar) ve true döner: aday "işlendi".</summary>
    private bool ReportPartial(LangModel l, PartialHit h, int kind, double now, CrimeWordSettings.Values v)
    {
        // Ses kapısı: kelimenin ilk göründüğü andan ~0.6 sn öncesinden şimdiye kadarki en yüksek ses
        double off = l.OffsetSec;
        var span = new VoskWord { word = h.word, start = h.firstSeen - off - 0.6, end = now - off };
        float level = WordLevel(l, span, out bool echo);
        if (!LoudEnough(level, echo, v)) return false;
        Report(l.index, kind, h.group, h.firstSeen, h.word, level, v);
        return true; // guard yutsa da işlendi (yeniden denenirse aynı söyleyiş ikinci kez sayılırdı)
    }

    // ---------------- B) Kesin sonuçlar ----------------

    private static void RecordEnergy(LangModel l, int samples, float level, bool echo)
    {
        int k = l.eHead;
        l.eStart[k] = l.fed - samples;
        l.eLen[k] = samples;
        l.eLevel[k] = level;
        l.eEcho[k] = echo;
        l.eHead = (k + 1) % LangModel.Ring;
        if (l.eCount < LangModel.Ring) l.eCount++;
    }

    /// <summary>Kelimenin söylendiği aralıktaki en yüksek ses seviyesi (-1: bilinmiyor) ve o anda sızma riski olup olmadığı.</summary>
    private static float WordLevel(LangModel l, VoskWord w, out bool echo)
    {
        echo = false;
        long a = (long)((w.start - 0.08) * VoiceChat.Rate), b = (long)((w.end + 0.08) * VoiceChat.Rate);
        float best = -1f;
        for (int n = 0; n < l.eCount; n++)
        {
            int k = (l.eHead - 1 - n + LangModel.Ring) % LangModel.Ring;
            long s = l.eStart[k], e = s + l.eLen[k];
            if (e < a) break; // daha eskiler zaten aralığın dışında
            if (s > b) continue;
            if (l.eLevel[k] > best) best = l.eLevel[k];
            if (l.eEcho[k]) echo = true;
        }
        return best;
    }

    private bool LoudEnough(float level, bool echo, CrimeWordSettings.Values v)
    {
        if (level < 0f) return true; // zaman eşleşmedi: bu kapıyı uygulama (diğer filtreler yine çalışır)
        float typical = Math.Max(0.06f, typicalForWorker);
        if (level < v.minWordLevel) return false;
        if (level < typical * v.minRelativeLevel) return false;
        if (echo && level < typical * v.echoRelativeLevel) return false;
        return true;
    }

    private static float NeededConf(VoskWord w, float baseConf, CrimeWordSettings.Values v)
    {
        float c = baseConf;
        if (CrimeWords.Squeeze(CrimeWords.Fold(w.word)).Length <= 4) c += v.shortWordExtraConf;
        return Math.Min(0.99f, c);
    }

    /// <summary>Vosk sonucunu güvenle ayrıştırır: ayrıştırma hatası boş liste döner (tanıyıcı "Failed" sayılmaz).</summary>
    private static List<VoskWord> SafeWords(IntPtr json)
    {
        try { return VoskJson.ParseWords(FromUtf8(json)); }
        catch (Exception) { return new List<VoskWord>(); }
    }

    /// <summary>Kelime listeli tanıyıcının kesin sonucu: güveni yeterli suç kelimeleri doğrulama sırasına girer.</summary>
    private void TakeGrammar(LangModel l, IntPtr json, CrimeWordSettings.Values v)
    {
        foreach (VoskWord w in SafeWords(json))
        {
            if (w.word == "[unk]" || !CrimeWords.IsCrimeWord(w.word, l.index)) continue;
            if (w.conf < NeededConf(w, v.grammarMinConf, v)) continue;
            AddPending(l, w, true, v);
        }
    }

    /// <summary>Serbest tanıyıcının kesin sonucu: kelimeleri hatırla (doğrulama + dil hakemliği için), suç kelimelerini sıraya al.</summary>
    private void TakeFree(LangModel l, IntPtr json, CrimeWordSettings.Values v)
    {
        List<VoskWord> words = SafeWords(json);
        var tokens = new string[words.Count];
        for (int n = 0; n < words.Count; n++)
        {
            VoskWord w = words[n];
            tokens[n] = w.word;
            l.recentFree.Add(w);
            if (CrimeWords.IsCrimeWord(w.word, l.index) && w.conf >= NeededConf(w, v.freeMinConf, v)) AddPending(l, w, false, v);
        }
        l.freeSeen.Add(tokens, StreamNow, true); // kesinleşen kelimeler de ara sonuç uyumunda kullanılır
        l.freeCovered = l.fed / (double)VoiceChat.Rate;
        double keepFrom = l.freeCovered - 20.0;
        l.recentFree.RemoveAll(x => x.end < keepFrom);
    }

    private void AddPending(LangModel l, VoskWord w, bool grammar, CrimeWordSettings.Values v)
    {
        float level = WordLevel(l, w, out bool echo);
        if (!LoudEnough(level, echo, v)) return;
        if (l.pending.Count >= 32) l.pending.RemoveAt(0);
        l.pending.Add(new Hit { w = w, grammar = grammar, level = level });
    }

    /// <summary>Bekleyen adaylar: kendi dilinin serbest tanıyıcısı (dil hakemliği açıksa diğer dilinki de) o anı kesinleştirince karar verilir.</summary>
    private void Resolve(int lang, bool force, CrimeWordSettings.Values v)
    {
        LangModel l = langs[lang], other = langs[1 - lang];
        bool otherOn = v.languageArbitration && other.state == ModelState.Ready && Wants(1 - lang);
        for (int n = 0; n < l.pending.Count; n++)
        {
            Hit h = l.pending[n];
            bool ready = force || (l.freeCovered >= h.w.end - 0.01 && (!otherOn || other.freeCovered >= h.w.end - 0.01));
            if (!ready) continue;
            l.pending.RemoveAt(n--);

            List<VoskWord> own = VoskJson.Overlapping(l.recentFree, h.w, 0.25);
            if (h.grammar)
            {
                // Zorlanmış eşleşme mi? Serbest tanıyıcı aynı anda benzer bir şey duymuş olmalı
                if (VoskJson.BestSimilarity(own, h.w.word, out VoskWord alt) < VoskJson.RequiredSimilarity(h.w.word, v.minSimilarity)) continue;
                // Serbest tanıyıcı oraya EMİNCE başka (suç olmayan) bir kelime yazdıysa (aldım / sahne / çalışalım / face)
                // kelime listeli tanıyıcı sesi zorlamıştır: sayma
                if (alt.word != null && alt.conf >= v.freeOverrideConf && !CrimeWords.IsCrimeWord(alt.word, lang)
                    && !CrimeWords.IsHomophone(alt.word)) continue;
            }
            if (otherOn)
            {
                // Dil hakemliği (isteğe bağlı, varsayılan kapalı)
                List<VoskWord> theirs = VoskJson.Overlapping(other.recentFree, h.w, 0.25);
                float ownConf = VoskJson.MeanConf(own), otherConf = VoskJson.MeanConf(theirs);
                if (theirs.Count > 0 && otherConf > ownConf + v.languageMargin) continue; // konuşma diğer dilde
            }
            string group = CrimeWords.GroupOf(h.w.word, lang) ?? CrimeWords.Fold(h.w.word);
            Report(lang, h.grammar ? KindGrammarFinal : KindFreeFinal, group, h.w.end + l.OffsetSec, h.w.word, h.level, v);
        }
    }

    /// <summary>Tek çıkış noktası: aynı söyleyiş (ara + kesin, iki tanıyıcı, iki dil) bir kez; farklı söyleyişler her seferinde.</summary>
    private void Report(int lang, int kind, string group, double time, string word, float level, CrimeWordSettings.Values v)
    {
        if (!guard.TryAdd(lang, lang * 4 + kind, group, time, v.doubleCountSeconds, utteranceIndex)) return;
        found.Enqueue(new Found { word = word, level = level });
    }

    // =====================================================================
    //  Ağ: bulunan kelime → sunucu
    // =====================================================================

    private void RegisterMessages(NetworkManager nm)
    {
        UnregisterMessages();
        regCmm = nm.CustomMessagingManager;
        if (nm.IsServer) regCmm.RegisterNamedMessageHandler(MsgName, OnCrimeTalkMessage);
    }

    private void UnregisterMessages()
    {
        if (regCmm != null)
        {
            try { regCmm.UnregisterNamedMessageHandler(MsgName); } catch (Exception) { }
        }
        regCmm = null;
    }

    private void SendToServer(NetworkManager nm, string word, float loudness)
    {
        byte loud = (byte)Mathf.RoundToInt(Mathf.Clamp01(loudness) * 255f);
        if (nm.IsServer)
        {
            CrimeTalk.ServerHeard(nm.LocalClientId, word, loud);
            return;
        }
        using (var w = new FastBufferWriter(128, Allocator.Temp))
        {
            w.WriteValueSafe(word.Length > 24 ? word.Substring(0, 24) : word);
            w.WriteValueSafe(loud);
            regCmm.SendNamedMessage(MsgName, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
    }

    private void OnCrimeTalkMessage(ulong sender, FastBufferReader reader)
    {
        try
        {
            reader.ReadValueSafe(out string word);
            reader.ReadValueSafe(out byte loud);
            CrimeTalk.ServerHeard(sender, word, loud);
        }
        catch (Exception) { }
    }

    // =====================================================================
    //  Arayüz
    // =====================================================================

    private bool ConsentVisible => askConsent && inSession && !SessionMenuUI.IsPaused;

    /// <summary>İzin penceresi açıkken fare imleci serbest (oyuncu tıklayabilsin); kapanınca eski haline döner.</summary>
    private void UpdateConsentCursor()
    {
        if (ConsentVisible)
        {
            if (Cursor.lockState == CursorLockMode.Locked) cursorWasLocked = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (cursorWasLocked)
        {
            cursorWasLocked = false;
            if (!SessionMenuUI.IsPaused && !WorkbenchUI.IsOpen && !BriefingUI.IsOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    private void AnswerConsent(bool yes)
    {
        DownloadConsent = yes;
        askConsent = false;
        UpdateConsentCursor();
        if (!yes) Debug.Log("[CrimeTalk] Model indirme reddedildi: sesli kelime algılama kapalı (StreamingAssets/Vosk'a model konursa yine çalışır).");
    }

    private void DrawConsent()
    {
        GUI.depth = -100;
        UIUtil.BeginCenteredPanel(460f, 210f);
        GUILayout.Label(Loc.T("crimetalk.consent.title"), UIUtil.RowStyle);
        GUILayout.Space(6f);
        GUILayout.Label(Loc.T("crimetalk.consent.body"), UIUtil.SmallStyle);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(Loc.T("crimetalk.consent.yes"), UIUtil.ButtonStyle, GUILayout.Height(32f))) AnswerConsent(true);
        GUILayout.Space(10f);
        if (GUILayout.Button(Loc.T("crimetalk.consent.no"), UIUtil.ButtonStyle, GUILayout.Height(32f))) AnswerConsent(false);
        GUILayout.EndHorizontal();
        UIUtil.EndCenteredPanel();
    }

    private void OnGUI()
    {
        if (ConsentVisible && !WorkbenchUI.IsOpen && !BriefingUI.IsOpen)
        {
            DrawConsent();
            return;
        }
        if (PlayerStealth.Local == null || SessionMenuUI.IsPaused) return;
        if (WorkbenchUI.IsOpen || BriefingUI.IsOpen) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        // Algılanan kelime yazıyla gösterilmez: sadece VoiceChat'in alttaki kırmızı ses kutusu yanar (BannedFlashUntil).
        // Burada yalnızca model indirilirken / yüklenirken durum satırı çıkar.
        string line = StatusLine(true);
        if (string.IsNullOrEmpty(line)) return;
        Color c = new Color(1f, 1f, 1f, 0.75f);

        float w = Mathf.Min(420f, style.CalcSize(new GUIContent(line)).x + 24f); // sağ alttaki çanta ipuçlarıyla çakışmasın
        Rect r = new Rect((Screen.width - w) * 0.5f, Screen.height - 64f - 28f, w, 22f);
        UIUtil.DrawRect(r, new Color(0.05f, 0.06f, 0.08f, 0.7f));
        style.normal.textColor = c;
        GUI.Label(r, line, style);
    }

    /// <summary>Model durumu. onlyBusy: sadece indirirken / yüklerken yazı döner (oyun içi gösterim).</summary>
    private string StatusLine(bool onlyBusy)
    {
        if (!SupportedPlatform) return onlyBusy ? null : Loc.T("crimetalk.status.unsupported");
        if (nativeTried && !nativeOk) return onlyBusy ? null : Loc.T("crimetalk.status.nolib");

        var parts = new List<string>();
        for (int i = 0; i < 2; i++)
        {
            if (!Wants(i)) continue;
            LangModel l = langs[i];
            string lang = i == 0 ? "TR" : "EN";
            switch (l.state)
            {
                case ModelState.Downloading: parts.Add(Loc.T("crimetalk.status.downloading", lang, Mathf.RoundToInt(l.progress * 100f))); break;
                case ModelState.Extracting:
                case ModelState.Loading: parts.Add(Loc.T("crimetalk.status.loading", lang)); break;
                case ModelState.Failed: if (!onlyBusy) parts.Add(Loc.T("crimetalk.status.failed", lang)); break;
                case ModelState.Ready: if (!onlyBusy) parts.Add(Loc.T("crimetalk.status.ready", lang)); break;
                case ModelState.Missing: if (!onlyBusy) parts.Add(Loc.T("crimetalk.status.waiting", lang)); break;
            }
        }
        return parts.Count == 0 ? null : string.Join("   ", parts);
    }

    /// <summary>Başarısız indirmeleri yeniden dene.</summary>
    private void RetryFailed()
    {
        foreach (LangModel l in langs)
        {
            if (l.state != ModelState.Failed) continue;
            // Yükleme hatalarında üstel geri çekilme (30 sn → 5 dk → bırak); indirme hataları eskisi gibi 30 sn'de bir
            if (l.loadFailures >= 3 || DateTime.UtcNow.Ticks < Interlocked.Read(ref l.retryAtTicks)) continue;
            // Tanıma sırasında hata (model yüklüydü): tanıyıcıları serbest bırakıp baştan yükle. "Failed"ı yalnızca iş
            // parçacığı kendi catch'inde koyar ve sonra bu dile bir daha dokunmaz (durum Ready değil): serbest bırakmak güvenli.
            if (l.model != IntPtr.Zero) FreeNative(l);
            l.state = ModelState.Missing;
            l.error = "";
            l.nextDirCheck = 0f;
        }
    }
}


// ===== SAF (Unity'den bağımsız) yardımcılar: aşağıdaki sınıflar konsol testinde de derlenir =====

/// <summary>
/// Hırsızlıkla ilgili kelimeler (Türkçe + İngilizce). Kelime küçük harfe çevrilir, Türkçe harfler sadeleştirilir
/// (ç→c, ş→s, ğ→g, ı/İ/I→i, ö→o, ü→u). Eşleşme KELİME BAZLIDIR ve HARF TEKRARINA DAYANIKLIDIR: kelime ve kök aynı harf
/// "koşularına" bölünür (çaaaal → c,a×4,l); her koşu kökteki harfle aynı ve en az kökteki kadar uzun olmalı
/// ("çaaaal" = çal, "steeeal" = steal, ama "steel" ≠ steal, "lot" ≠ loot). Kelimenin başı köklerden biri olmalı
/// (Türkçe ekler için) ya da kelime tam listede olmalı; Exclude ile başlayanlar sayılmaz.
/// Dil: 0 = Türkçe tanıyıcının çıktısı (Türkçe kökler), 1 = İngilizce (İngilizce kökler), -1 = ikisi de.
/// Her kök bir "gruba" aittir (çal / çaldım / çalıntı → "cal"): aynı söyleyişin ara sonuçta "çal" → "çaldım" diye
/// değişmesi tek kelime sayılır.
/// </summary>
public static class CrimeWords
{
    private struct Root { public string text, group; }

    private static Root R(string text, string group) => new Root { text = text, group = group };

    // Türkçe kökler (önek). "cal": çal, çalın, çalar, çalan, çaldır, çaldım, çalalım, çalıyorum, çalmak, çalıntı, çalındı...
    private static readonly Root[] Turkish =
    {
        R("cal", "cal"),
        R("hirsiz", "hirsiz"),
        R("soygun", "soy"), R("soyal", "soy"), R("soyac", "soy"), R("soydu", "soy"), R("soyuyo", "soy"),
        R("degistir", "degistir"),
        R("sahte", "sahte"),
        R("asird", "asir"), R("asiral", "asir"), R("asirac", "asir"), R("asiriyo", "asir"),
        R("arakla", "arakla"),
        R("yuruttu", "yurut"), R("yurutel", "yurut"),
    };

    // Türkçe: ilgisiz kelimeler (önek). çalış*, çalgı, çalkala, çalım, çalık, çalpara, çalçene, çalılık/çalılar, soydaş, soyadı.
    private static readonly string[] TurkishExclude =
    {
        "calis", "calg", "calk", "calim", "calik", "calp", "calc", "calil", "soydas", "soyad",
    };

    // Türkçe: tek başına sayılmayan tam kelimeler (çalı = bitki)
    private static readonly string[] TurkishExcludeExact = { "cali" };

    // İngilizce kökler (önek): steal/steals/stealing, stole/stolen, swapped/swapping, fake/faked, forger/forgery/forged,
    // heist, robbed/robber/robbery/robbing, thief, thieves/thieving, loot/looted/looting, nicked.
    private static readonly Root[] English =
    {
        R("steal", "steal"), R("stole", "steal"),
        R("swapp", "swap"),
        R("fake", "fake"),
        R("forger", "forge"), R("forged", "forge"), R("forging", "forge"),
        R("heist", "heist"),
        R("robb", "rob"),
        R("thief", "thief"), R("thiev", "thief"),
        R("loot", "loot"),
        R("nicked", "nick"),
    };

    // İngilizce: tam kelime olarak sayılanlar (önek olsa robot/robe/robin/swapnil... karışırdı)
    private static readonly Root[] EnglishExact = { R("swap", "swap"), R("swaps", "swap"), R("rob", "rob"), R("robs", "rob"), R("forge", "forge") };

    private static readonly string[] EnglishExclude = { "stealth" };

    /// <summary>Tek bir kelime hırsızlık kelimesi mi (dil bilinmiyorsa iki dilin kökleri de denenir).</summary>
    public static bool IsCrimeWord(string raw) => GroupOf(raw, -1) != null;

    /// <summary>Tek bir kelime, verilen dilin tanıyıcısının çıktısı olarak hırsızlık kelimesi mi.</summary>
    public static bool IsCrimeWord(string raw, int lang) => GroupOf(raw, lang) != null;

    /// <summary>Kelimenin suç grubu ("cal", "steal" ...) ya da suç kelimesi değilse null.</summary>
    public static string GroupOf(string raw, int lang)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        string w = Fold(raw.Trim(' ', '\t', ',', '.', '!', '?', '\'', '"'));
        if (w.Length < 3 || w == "[unk]") return null; // 1-2 harfli kelimeler çok sık karışır
        Run[] runs = Runs(w);
        if (lang != 1)
        {
            if (!AnyPrefix(runs, TurkishExclude) && !AnyExact(runs, TurkishExcludeExact))
                foreach (Root r in Turkish) if (RunMatch(runs, Runs(r.text), true)) return r.group;
        }
        if (lang != 0)
        {
            if (!AnyPrefix(runs, EnglishExclude))
            {
                foreach (Root r in English) if (RunMatch(runs, Runs(r.text), true)) return r.group;
                foreach (Root r in EnglishExact) if (RunMatch(runs, Runs(r.text), false)) return r.group;
            }
        }
        return null;
    }

    // Sesçe suç kelimesiyle aynı olan, serbest tanıyıcının sık yazdığı kelimeler (steal ↔ steel): doğrulamada çelişki sayılmaz
    private static readonly string[] Homophones = { "steel", "steele" };

    public static bool IsHomophone(string raw)
    {
        string w = Fold(raw ?? "");
        foreach (string h in Homophones) if (w == h) return true;
        return false;
    }

    /// <summary>Metinde hırsızlık kelimesi varsa o kelimeyi (orijinal haliyle) döner, yoksa null.</summary>
    public static string FindIn(string text, int lang = -1)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string[] words = text.Split(new[] { ' ', '\t', ',', '.', '!', '?', '\'' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string raw in words) if (IsCrimeWord(raw, lang)) return raw;
        return null;
    }

    // ---------------- Harf koşuları (uzatmaya dayanıklı eşleşme) ----------------

    private struct Run { public char c; public int n; }

    private static Run[] Runs(string s)
    {
        var list = new List<Run>(s.Length);
        foreach (char ch in s)
        {
            if (list.Count > 0 && list[list.Count - 1].c == ch) { Run r = list[list.Count - 1]; r.n++; list[list.Count - 1] = r; }
            else list.Add(new Run { c = ch, n = 1 });
        }
        return list.ToArray();
    }

    /// <summary>prefix: kelime kökle BAŞLAR; değilse kelime köke EŞİT olmalı (aynı koşu sayısı). Her koşu: aynı harf, kelimede en az kökteki kadar.</summary>
    private static bool RunMatch(Run[] word, Run[] root, bool prefix)
    {
        if (word.Length < root.Length) return false;
        if (!prefix && word.Length != root.Length) return false;
        for (int i = 0; i < root.Length; i++)
            if (word[i].c != root[i].c || word[i].n < root[i].n) return false;
        return true;
    }

    private static bool AnyPrefix(Run[] word, string[] list)
    {
        foreach (string p in list) if (RunMatch(word, Runs(p), true)) return true;
        return false;
    }

    private static bool AnyExact(Run[] word, string[] list)
    {
        foreach (string p in list) if (RunMatch(word, Runs(p), false)) return true;
        return false;
    }

    /// <summary>Art arda tekrar eden harfleri teke indirir ("caaaal" → "cal", "steeeal" → "steal").</summary>
    public static string Squeeze(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        var sb = new StringBuilder(s.Length);
        char prev = '\0';
        foreach (char ch in s) { if (ch != prev) sb.Append(ch); prev = ch; }
        return sb.ToString();
    }

    public static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            switch (ch)
            {
                case 'ç': case 'Ç': sb.Append('c'); break;
                case 'ş': case 'Ş': sb.Append('s'); break;
                case 'ğ': case 'Ğ': sb.Append('g'); break;
                case 'ı': case 'I': case 'İ': sb.Append('i'); break;
                case 'ö': case 'Ö': sb.Append('o'); break;
                case 'ü': case 'Ü': sb.Append('u'); break;
                case 'â': case 'Â': sb.Append('a'); break;
                case '̇': break; // "i̇" birleşik nokta
                default: sb.Append(char.ToLowerInvariant(ch)); break;
            }
        }
        return sb.ToString();
    }
}

/// <summary>Vosk'un kelime başına sonucu (SetWords açıkken): kelime, güven ve ses akışındaki zamanı (sn).</summary>
public struct VoskWord
{
    public string word;
    public float conf;
    public double start, end;
}

/// <summary>Ara sonuçta kararlı hale gelen suç kelimesi.</summary>
public struct PartialHit
{
    public string word;      // son görülen hali ("çal" → "çaldım")
    public string group;
    public double firstSeen; // ortak ses saatinde ilk görüldüğü an (sn)
    public string key;       // izleyicideki anahtar ("cal#0"): bildirilince MarkFired(key)
}

/// <summary>
/// Bir tanıyıcının ARA (partial) sonuçlarında suç kelimelerinin kararlılığını izler. Anahtar = grup + aynı gruptaki
/// sıra ("cal#0", "cal#1"): aynı kelime ara sonuçta "çal" → "çaldım" diye değişse de kararlılık sürer; cümle içinde iki kez
/// söylenen kelime iki ayrı aday olur. Bir aday bir cümlede bir kez ateşlenir (Reset'e kadar).
/// </summary>
public sealed class PartialTracker
{
    private sealed class Entry { public string word; public int count; public double first; public bool seen, tail; }

    private readonly Dictionary<string, Entry> live = new Dictionary<string, Entry>();
    private readonly HashSet<string> fired = new HashSet<string>();
    private readonly Dictionary<string, double> droppedAt = new Dictionary<string, double>();

    /// <summary>Bildirilmiş bir kelime ara sonuçtan bu kadar sn'den uzun kaybolup dönerse yeniden aday olabilir.</summary>
    public const double RefireGap = 1.0;
    private readonly Dictionary<string, int> occ = new Dictionary<string, int>();
    private readonly List<string> drop = new List<string>();

    public static string[] Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text)) return new string[0];
        return text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public void Reset()
    {
        live.Clear();
        fired.Clear();
        droppedAt.Clear();
    }

    /// <param name="needUpdates">art arda en az kaç güncellemede görünmeli</param>
    /// <param name="needSeconds">ilk görüldüğünden beri en az kaç sn</param>
    /// <param name="tailExtra">kelime ara sonucun son kelimesiyse (hâlâ uzayabilir) ya da ≤3 harfse eklenen sn</param>
    public List<PartialHit> Update(string[] tokens, int lang, double now, int needUpdates, double needSeconds, double tailExtra)
    {
        var hits = new List<PartialHit>();
        occ.Clear();
        foreach (Entry e in live.Values) e.seen = false;

        int last = tokens.Length - 1;
        while (last >= 0 && tokens[last] == "[unk]") last--;
        for (int i = 0; i < tokens.Length; i++)
        {
            string g = CrimeWords.GroupOf(tokens[i], lang);
            if (g == null) continue;
            occ.TryGetValue(g, out int k);
            occ[g] = k + 1;
            string key = g + "#" + k;
            if (!live.TryGetValue(key, out Entry e))
            {
                e = new Entry { first = now };
                live[key] = e;
                // Kaybolup RefireGap'ten uzun süre sonra aynı anahtarla dönen kelime yeni bir söyleyiştir ("cal#0" ilk
                // "çal" düştükten sonra ikinci "çal"a geçebilir): bastırma kalkar. Kısa titreşimde (ara sonuç "çal" →
                // "çalış" → "çal") bastırma sürer (aynı izleyicinin ikinci bildirimini ReportGuard yutmaz).
                if (fired.Contains(key) && droppedAt.TryGetValue(key, out double dt) && now - dt > RefireGap) fired.Remove(key);
            }
            droppedAt.Remove(key);
            e.count++;
            e.word = tokens[i];
            e.seen = true;
            e.tail = i == last || CrimeWords.Squeeze(CrimeWords.Fold(tokens[i])).Length <= 3;
        }

        drop.Clear();
        foreach (KeyValuePair<string, Entry> kv in live)
        {
            Entry e = kv.Value;
            if (!e.seen) { drop.Add(kv.Key); droppedAt[kv.Key] = now; continue; } // kayboldu (ara sonuç değişti): kararlılık sıfırlanır
            if (fired.Contains(kv.Key)) continue;
            double need = needSeconds + (e.tail ? tailExtra : 0.0);
            // Aday: çağıran ancak bildirince MarkFired der; elenirse (uyum / ses kapısı) sonraki güncellemede yine döner
            if (e.count >= Math.Max(1, needUpdates) && now - e.first >= need - 1e-9)
                hits.Add(new PartialHit { word = e.word, group = CrimeWords.GroupOf(e.word, lang), firstSeen = e.first, key = kv.Key });
        }
        foreach (string k in drop) live.Remove(k);
        return hits;
    }

    /// <summary>Aday bildirildi: bu cümlede (Reset'e kadar) bir daha döndürülmez.</summary>
    public void MarkFired(string key)
    {
        if (key != null) fired.Add(key);
    }
}

/// <summary>
/// Bir tanıyıcının son birkaç saniyede duyduğu kelimeler (ve ardışık 2-3 kelimenin birleşimi: "çal dım" → "çaldım"),
/// diğer tanıyıcının adayını doğrulamak için.
/// </summary>
public sealed class RecentTokens
{
    private readonly Dictionary<string, double> seen = new Dictionary<string, double>();
    private readonly List<string> old = new List<string>();

    public void Add(string[] tokens, double now, bool joins)
    {
        if (tokens == null) return;
        for (int a = 0; a < tokens.Length; a++)
        {
            if (tokens[a] == "[unk]") continue;
            string joined = "";
            for (int b = a; b < tokens.Length && b < a + (joins ? 3 : 1); b++)
            {
                if (tokens[b] == "[unk]") break;
                joined += CrimeWords.Fold(tokens[b]);
                seen[joined] = now;
            }
        }
        if (seen.Count > 256) Purge(now, 3.0);
    }

    /// <summary>Cümle bitti: önceki cümlenin kelimeleri yeni cümlenin adayını onaylamasın.</summary>
    public void Clear() => seen.Clear();

    private void Purge(double now, double maxAge)
    {
        old.Clear();
        foreach (KeyValuePair<string, double> kv in seen) if (now - kv.Value > maxAge) old.Add(kv.Key);
        foreach (string k in old) seen.Remove(k);
    }

    /// <summary>Son "window" sn içinde hedefe benzer (≥ minSim) bir şey duyuldu mu. crimeOnly: sadece suç kelimeleri sayılır.</summary>
    public bool Agrees(string target, double now, double window, float minSim, int lang, bool crimeOnly)
    {
        string t = CrimeWords.Fold(target);
        string tg = CrimeWords.GroupOf(target, lang);
        minSim = VoskJson.RequiredSimilarity(target, minSim);
        foreach (KeyValuePair<string, double> kv in seen)
        {
            if (now - kv.Value > window) continue;
            if (crimeOnly)
            {
                string g = CrimeWords.GroupOf(kv.Key, lang);
                if (g == null) continue;
                if (g == tg) return true;
            }
            if (VoskJson.Similarity(kv.Key, t) >= minSim) return true;
        }
        return false;
    }
}

/// <summary>
/// Çift sayım koruması: her tanıyıcı (dil × {serbest/kelime listeli} × {ara/kesin}) her söyleyişi en fazla bir kez bildirir;
/// FARKLI bir tanıyıcıdan AYNI gruptaki (aynı kelime kökü) kelime yakın zamanda gelirse aynı söyleyiştir ve sayılmaz (her
/// kayıt her tanıyıcıdan en fazla bir kopyayı yutar: "çal çal" iki ayrı söyleyiş olarak kalır). Farklı gruplar hiçbir zaman
/// birbirini yutmaz (diller arası da). window = 0: koruma yok.
///  - Aynı aşama (ara↔ara, kesin↔kesin): |fark| ≤ window.
///  - Ara ↔ kesin: ara sonuç kesin sonucun bitişinden önce (en fazla 0.1 sn sonra) ve en fazla CrossStageWindow sn önce
///    görülmüş olmalı (uzatılmış "çaaaal" ~1-2 sn; aralıksız konuşmada 10 sn önceki ara kayıt yeni kesini yutamaz).
///  - Bir tanıyıcının kesin sonucu işlenince, AYNI tanıyıcının o andan önceki eşleşmemiş ara kayıtları o kesin tanıyıcıya
///    kapanır (kesin sonuçlar zaman sırasıyla gelir; eski ara kayıt artık daha sonraki bir kesini yutamaz).
/// SÖYLEYİŞ (utt): ses saati yalnızca ses giderken ilerler, sessizlik eksende yoktur; FARKLI söyleyişlerin kayıtları hiç eşleşmez.
/// tracker = dil*4 + tür (0 serbest ara, 1 kelime listeli ara, 2 serbest kesin, 3 kelime listeli kesin).
/// </summary>
public sealed class ReportGuard
{
    public const double CrossStageWindow = 3.5;

    private sealed class Entry { public int lang, tracker, utt; public string group; public double t; public readonly HashSet<int> absorbed = new HashSet<int>(); }

    private readonly List<Entry> list = new List<Entry>();

    private static bool IsPartial(int tracker) => tracker % 4 < 2;

    public bool TryAdd(int lang, int tracker, string group, double t, double window, int utt)
    {
        list.RemoveAll(e => e.utt != utt || Math.Abs(t - e.t) > 60.0); // eski söyleyişler artık eşleşemez
        Entry best = null;
        if (window > 0)
        {
            double bestD = double.MaxValue;
            foreach (Entry e in list)
            {
                if (e.tracker == tracker || e.absorbed.Contains(tracker) || e.group != group) continue;
                double d;
                if (IsPartial(e.tracker) == IsPartial(tracker))
                {
                    d = Math.Abs(e.t - t);
                    if (d > window) continue;
                }
                else
                {
                    // ara zamanı ≤ kesin bitişi + 0.1 ve en fazla CrossStageWindow önce
                    double partialT = IsPartial(e.tracker) ? e.t : t, finalT = IsPartial(e.tracker) ? t : e.t;
                    d = finalT - partialT;
                    if (d < -0.1 || d > CrossStageWindow) continue;
                    d = Math.Abs(d);
                }
                if (d < bestD) { best = e; bestD = d; }
            }
        }

        bool added = best == null;
        if (best != null) best.absorbed.Add(tracker);
        else list.Add(new Entry { lang = lang, tracker = tracker, group = group, t = t, utt = utt });

        // Kesin sonuç işlendi: aynı tanıyıcının bundan önceki eşleşmemiş ara kayıtları bu kesin tanıyıcıya kapanır
        if (!IsPartial(tracker))
        {
            int partialTracker = tracker - 2;
            double upTo = best != null && best.tracker == partialTracker ? best.t : t + 0.1;
            foreach (Entry e in list)
                if (e != best && e.tracker == partialTracker && e.group == group && e.t <= upTo) e.absorbed.Add(tracker);
        }
        return added;
    }
}

/// <summary>Vosk JSON sonucu ayrıştırma + doğrulama yardımcıları (Unity'den bağımsız; test edilebilir).</summary>
public static class VoskJson
{
    /// <summary>{"partial" : "metin"} → metin (yoksa "").</summary>
    public static string ParsePartial(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        int p = json.IndexOf("\"partial\"", StringComparison.Ordinal);
        if (p < 0) return "";
        int i = json.IndexOf(':', p + 9);
        if (i < 0) return "";
        i++;
        return ReadString(json, ref i) ?? "";
    }

    /// <summary>{"result":[{"conf":..,"end":..,"start":..,"word":".."},...],"text":".."} → kelimeler. Hatalı JSON'da boş liste.</summary>
    public static List<VoskWord> ParseWords(string json)
    {
        var list = new List<VoskWord>();
        if (string.IsNullOrEmpty(json)) return list;
        int r = json.IndexOf("\"result\"", StringComparison.Ordinal);
        if (r < 0) return list;
        int i = json.IndexOf('[', r);
        if (i < 0) return list;
        i++;
        while (i < json.Length)
        {
            int open = json.IndexOf('{', i), close = json.IndexOf(']', i);
            if (open < 0 || (close >= 0 && close < open)) break;
            var w = new VoskWord { conf = 1f };
            i = open + 1;
            while (i < json.Length && json[i] != '}')
            {
                string key = ReadString(json, ref i);
                if (key == null) break;
                int colon = json.IndexOf(':', i);
                if (colon < 0) return list;
                i = colon + 1;
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i < json.Length && json[i] == '"')
                {
                    string v = ReadString(json, ref i);
                    if (key == "word") w.word = v;
                }
                else
                {
                    int s = i;
                    while (i < json.Length && json[i] != ',' && json[i] != '}') i++;
                    double.TryParse(json.Substring(s, i - s).Trim(), System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out double d);
                    if (key == "conf") w.conf = (float)d;
                    else if (key == "start") w.start = d;
                    else if (key == "end") w.end = d;
                }
                while (i < json.Length && (json[i] == ',' || char.IsWhiteSpace(json[i]))) i++;
            }
            if (!string.IsNullOrEmpty(w.word)) list.Add(w);
            i++;
        }
        return list;
    }

    // i bir sonraki '"' karakterine kadar ilerler, dizeyi okur (kaçışlar dahil), i kapanış tırnağından sonrasını gösterir.
    private static string ReadString(string json, ref int i)
    {
        int q = json.IndexOf('"', i);
        if (q < 0) return null;
        int brace = json.IndexOf('}', i);
        if (brace >= 0 && brace < q) { i = brace; return null; }
        var sb = new StringBuilder();
        i = q + 1;
        while (i < json.Length)
        {
            char c = json[i++];
            if (c == '"') return sb.ToString();
            if (c == '\\' && i < json.Length)
            {
                char e = json[i++];
                if (e == 'u' && i + 4 <= json.Length)
                {
                    // Bozuk kaçış (\uZZZZ) istisna atmasın: atla
                    if (int.TryParse(json.Substring(i, 4), System.Globalization.NumberStyles.HexNumber,
                                     System.Globalization.CultureInfo.InvariantCulture, out int code))
                        sb.Append((char)code);
                    i += 4;
                }
                else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Hedef kelimeyle zaman olarak çakışan (± pay sn) kelimeler, zamana göre sıralı.</summary>
    public static List<VoskWord> Overlapping(List<VoskWord> words, VoskWord target, double pad)
    {
        var res = new List<VoskWord>();
        foreach (VoskWord w in words)
            if (w.start < target.end + pad && w.end > target.start - pad && w.word != "[unk]") res.Add(w);
        res.Sort((a, b) => a.start.CompareTo(b.start));
        return res;
    }

    public static float MeanConf(List<VoskWord> words)
    {
        if (words.Count == 0) return 0f;
        float s = 0f;
        foreach (VoskWord w in words) s += w.conf;
        return s / words.Count;
    }

    /// <summary>
    /// Kelime listeli tanıyıcının bulduğu kelimeye, serbest tanıyıcının aynı anda duyduğu kelimelerden (tek tek ya da
    /// en fazla 3'ü birleşik: "çal alım" → "çalalım") en çok benzeyenin benzerliği (0-1, harf düzeyinde).
    /// </summary>
    /// <param name="single">En iyi eşleşme TEK bir kelimeyse o kelime (serbest tanıyıcının "aslında bunu duydum" iddiası), yoksa word = null.</param>
    public static float BestSimilarity(List<VoskWord> free, string target, out VoskWord single)
    {
        string t = CrimeWords.Fold(target);
        float best = 0f;
        single = default;
        for (int a = 0; a < free.Count; a++)
        {
            string joined = "";
            for (int b = a; b < free.Count && b < a + 3; b++)
            {
                joined += CrimeWords.Fold(free[b].word);
                float s = Similarity(joined, t);
                if (s > best)
                {
                    best = s;
                    single = b == a ? free[a] : default;
                }
            }
        }
        return best;
    }

    /// <summary>Kısa kelimelerde (≤4 harf: çal, rob, fake) tek harf farkı bile büyük oran tutar ("kal" ~ "çal" = 0.67):
    /// bunlar için en az 0.8 (pratikte birebir aynı; uzatılmış harfler sıkıştırılır) istenir.</summary>
    public static float RequiredSimilarity(string target, float minSim) =>
        CrimeWords.Squeeze(CrimeWords.Fold(target ?? "")).Length <= 4 ? Math.Max(minSim, 0.8f) : minSim;

    /// <summary>Harf benzerliği (0-1): 1 - Levenshtein / uzun olanın uzunluğu; ayrıca uzatılmış harfler sıkıştırılarak
    /// ("caaaal" ~ "cal") karşılaştırılır, ikisinden yüksek olanı döner.</summary>
    public static float Similarity(string a, string b)
    {
        float raw = Lev(a, b);
        string sa = CrimeWords.Squeeze(a), sb = CrimeWords.Squeeze(b);
        if (sa.Length == a.Length && sb.Length == b.Length) return raw;
        return Math.Max(raw, Lev(sa, sb));
    }

    private static float Lev(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1f;
        int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            int[] tmp = prev; prev = cur; cur = tmp;
        }
        return 1f - prev[b.Length] / (float)Math.Max(a.Length, b.Length);
    }
}
