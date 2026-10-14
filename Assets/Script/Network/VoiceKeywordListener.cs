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
/// SESLİ SOHBETTE HIRSIZLIK KELİMELERİ: konuşurken "çaldım", "değiştirdim", "sahte", "soygun" / "I stole it", "swapped",
/// "fake", "heist" gibi bir şey söylersen yakındaki güvenlikler DUYAR: söylediğin yerin yanındaki esere (değiştirdiğin
/// bir eser varsa önce ona) gidip inceler, sahteyi fark etme şansı artar ve bir süre sana şüpheyle bakarlar (CrimeTalk).
///
///  - Konuşma tanıma: Vosk (çevrimdışı, ücretsiz). Türkçe + İngilizce küçük modeller ilk kullanımda kendiliğinden indirilir
///    (~80 MB, bir kez). Elle koymak için: Assets/StreamingAssets/Vosk/vosk-model-small-tr-0.3 (ve ...-en-us-0.15).
///  - Sadece SESİN GERÇEKTEN GİTTİĞİ anlar dinlenir (V basılıyken ya da açık mikrofonda eşik geçilince): VoiceChat'in
///    gönderdiği 16 kHz ses buraya da gelir. Tanıma ayrı bir iş parçacığında, oyuncunun kendi bilgisayarında yapılır;
///    ağdan sadece bulunan kelime sunucuya gider.
///  - Esc → Ayarlar → Sesli sohbet altında: Kapalı / Türkçe + İngilizce / Sadece Türkçe / Sadece İngilizce.
///  - Şu an yalnızca Windows'ta çalışır (Assets/Plugins/x86_64/libvosk.dll). Diğer sistemlerde sessizce kapalı kalır.
/// Sahneye bir şey eklemeye gerek yok: oyun açılınca kendiliğinden oluşur.
/// </summary>
public class VoiceKeywordListener : MonoBehaviour
{
    public static VoiceKeywordListener Instance { get; private set; }

    public enum Mode { Off = 0, Both = 1, Turkish = 2, English = 3 }

    private const string KeyMode = "SS_CrimeWords_Mode";
    private const string MsgName = "SS_CrimeTalk";
    private const float ClientCooldown = 4f;

    private static readonly string[] ModelNames = { "vosk-model-small-tr-0.3", "vosk-model-small-en-us-0.15" };
    private const string ModelUrlBase = "https://alphacephei.com/vosk/models/";

    // PlayerPrefs sadece ana iş parçacığında okunabilir: tanıma iş parçacığı bu kopyayı kullanır
    private static volatile int modeCache = -1;

    public static Mode CurrentMode
    {
        get
        {
            if (modeCache < 0) modeCache = Mathf.Clamp(PlayerPrefs.GetInt(KeyMode, (int)Mode.Both), 0, 3);
            return (Mode)modeCache;
        }
        set
        {
            modeCache = (int)value;
            PlayerPrefs.SetInt(KeyMode, (int)value);
        }
    }

    private static bool Wants(int lang)
    {
        Mode m = (Mode)Mathf.Max(0, modeCache);
        return m == Mode.Both || (lang == 0 && m == Mode.Turkish) || (lang == 1 && m == Mode.English);
    }

    // ---------------- Durum ----------------

    private enum ModelState { Missing, Downloading, Extracting, Loading, Ready, Failed }

    private class LangModel
    {
        public string name;
        public volatile ModelState state = ModelState.Missing;
        public float progress;
        public string error = "";
        public string path;
        public IntPtr model = IntPtr.Zero;
        public IntPtr rec = IntPtr.Zero;
        public UnityWebRequest req;
        public string zipPath;
        public readonly HashSet<string> said = new HashSet<string>(); // bu cümlede zaten bildirilenler
    }

    private readonly LangModel[] langs = new LangModel[2];
    private string persistentRoot, streamingRoot, pluginDir;
    private bool nativeOk, nativeTried;
    private string nativeError = "";

    // Ses: ana iş parçacığı → tanıma iş parçacığı
    private readonly ConcurrentQueue<short[]> audio = new ConcurrentQueue<short[]>();
    private readonly ConcurrentQueue<string> found = new ConcurrentQueue<string>();
    private readonly AutoResetEvent wake = new AutoResetEvent(false);
    private Thread worker;
    private volatile bool running;
    private long lastAudioTicks;

    // Ağ
    private CustomMessagingManager regCmm;
    private float nextSend;

    // Ekranda (sadece kendine): algılanan kelime
    private string shownWord = "";
    private float shownLoud;
    private float shownUntil;
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
        for (int i = 0; i < 2; i++) langs[i] = new LangModel { name = ModelNames[i] };
        VoiceChat.LocalSpeechFrame += OnSpeechFrame;
    }

    private void OnDestroy()
    {
        VoiceChat.LocalSpeechFrame -= OnSpeechFrame;
        UnregisterMessages();
        StopWorker();
        foreach (LangModel l in langs) if (l != null && l.req != null) l.req.Dispose();
        if (Instance == this) Instance = null;
    }

    // =====================================================================
    //  Ses girişi (VoiceChat gönderdiği her 40 ms'lik paketi buraya da verir)
    // =====================================================================

    // Son ~1.6 sn'nin ses yüksekliği (40 ms'lik paketler): kelimenin fısıltıyla mı bağırarak mı söylendiği
    private readonly float[] recentLevels = new float[40];
    private readonly float[] recentTimes = new float[40];
    private int recentHead;

    /// <summary>Son ~1.2 sn'deki en yüksek ses (0 = fısıltı, 1 = bağırma).</summary>
    private float RecentLoudness()
    {
        float best = 0f, now = Time.unscaledTime;
        for (int i = 0; i < recentLevels.Length; i++)
            if (now - recentTimes[i] < 1.2f && recentLevels[i] > best) best = recentLevels[i];
        // VoiceChat'in mikrofon seviyesiyle aynı ölçek; 0.3 altı fısıltı, 0.9 üstü bağırma
        return Mathf.InverseLerp(0.3f, 0.9f, best);
    }

    private void OnSpeechFrame(float[] samples, int count)
    {
        // Ses yüksekliği (her zaman kaydedilir; tanıma kapalıyken de ucuz)
        double sum = 0;
        for (int i = 0; i < count; i++) sum += samples[i] * samples[i];
        float rms = count > 0 ? (float)Math.Sqrt(sum / count) : 0f;
        recentLevels[recentHead] = Mathf.Clamp01(Mathf.Sqrt(rms) * 2.2f);
        recentTimes[recentHead] = Time.unscaledTime;
        recentHead = (recentHead + 1) % recentLevels.Length;

        if (!running || CurrentMode == Mode.Off) return;
        var pcm = new short[count];
        for (int i = 0; i < count; i++) pcm[i] = (short)Mathf.Clamp(samples[i] * 32767f, -32768f, 32767f);
        if (audio.Count > 150) audio.TryDequeue(out _); // iş parçacığı yetişemiyorsa en eskiyi at
        audio.Enqueue(pcm);
        Interlocked.Exchange(ref lastAudioTicks, DateTime.UtcNow.Ticks);
        wake.Set();
    }

    // =====================================================================
    //  Ana döngü
    // =====================================================================

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool inSession = nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient) && nm.CustomMessagingManager != null;

        if (inSession) { if (regCmm != nm.CustomMessagingManager) RegisterMessages(nm); }
        else if (regCmm != null) UnregisterMessages();

        if (CurrentMode == Mode.Off || !SupportedPlatform)
        {
            if (running) StopWorker();
            return;
        }

        // Oyundayken ve sesli sohbet varken modelleri hazırla (indirilmemişse indir)
        if (inSession) PrepareModels();

        if (!running && AnyReadyForMode()) StartWorker();
        if (running && !AnyReadyForMode()) StopWorker();

        // Bulunan kelimeler → sunucuya
        while (found.TryDequeue(out string word))
        {
            shownWord = word;
            shownLoud = RecentLoudness();
            shownUntil = Time.unscaledTime + 3f;
            VoiceChat.BannedFlashUntil = Time.unscaledTime + 2.5f; // mikrofon kutusu kırmızı yanar
            if (inSession && Time.unscaledTime >= nextSend)
            {
                nextSend = Time.unscaledTime + ClientCooldown;
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
    //  Modeller: StreamingAssets'te varsa oradan, yoksa indir → aç → yükle
    // =====================================================================

    private void PrepareModels()
    {
        if (!EnsureNative()) return;

        for (int i = 0; i < 2; i++)
        {
            LangModel l = langs[i];
            if (!Wants(i)) continue;

            switch (l.state)
            {
                case ModelState.Missing:
                    string dir = FindModelDir(l.name);
                    if (dir != null) BeginLoad(l, dir);
                    else BeginDownload(l);
                    break;

                case ModelState.Downloading:
                    if (l.req == null) { l.state = ModelState.Failed; break; }
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
    }

    private string FindModelDir(string name)
    {
        foreach (string root in new[] { streamingRoot, persistentRoot })
        {
            try
            {
                string dir = Path.Combine(root, name);
                if (IsModelDir(dir)) return dir;
            }
            catch (Exception) { }
        }
        return null;
    }

    private static bool IsModelDir(string dir) =>
        Directory.Exists(dir) && (Directory.Exists(Path.Combine(dir, "am")) || File.Exists(Path.Combine(dir, "final.mdl")));

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

    private void BeginExtract(LangModel l)
    {
        l.state = ModelState.Extracting;
        string zip = l.zipPath, root = persistentRoot, name = l.name;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                string target = Path.Combine(root, name);
                using (FileStream fs = File.OpenRead(zip))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    string rootFull = Path.GetFullPath(root);
                    foreach (ZipArchiveEntry e in archive.Entries)
                    {
                        string full = Path.GetFullPath(Path.Combine(root, e.FullName));
                        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue; // güvenlik
                        if (string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(full); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(full));
                        using (Stream src = e.Open())
                        using (FileStream dst = File.Create(full))
                            src.CopyTo(dst);
                    }
                }
                try { File.Delete(zip); } catch (Exception) { }
                if (!IsModelDir(target)) throw new Exception("model klasörü bulunamadı");
                LoadModelBlocking(l, target);
            }
            catch (Exception ex)
            {
                l.error = ex.Message;
                l.state = ModelState.Failed;
            }
        });
    }

    private void BeginLoad(LangModel l, string dir)
    {
        l.state = ModelState.Loading;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { LoadModelBlocking(l, dir); }
            catch (Exception ex) { l.error = ex.Message; l.state = ModelState.Failed; }
        });
    }

    /// <summary>İş parçacığında: modeli ve tanıyıcıyı oluşturur (birkaç saniye sürebilir).</summary>
    private void LoadModelBlocking(LangModel l, string dir)
    {
        l.state = ModelState.Loading;
        l.path = dir;
        IntPtr model = Native.vosk_model_new(Utf8(dir));
        if (model == IntPtr.Zero) throw new Exception("model yüklenemedi: " + dir);
        IntPtr rec = Native.vosk_recognizer_new(model, VoiceChat.Rate);
        if (rec == IntPtr.Zero) { Native.vosk_model_free(model); throw new Exception("tanıyıcı oluşturulamadı"); }
        l.model = model;
        l.rec = rec;
        l.state = ModelState.Ready;
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

        [DllImport(Lib)] public static extern void vosk_set_log_level(int level);
        [DllImport(Lib)] public static extern IntPtr vosk_model_new(byte[] path);
        [DllImport(Lib)] public static extern void vosk_model_free(IntPtr model);
        [DllImport(Lib)] public static extern IntPtr vosk_recognizer_new(IntPtr model, float sampleRate);
        [DllImport(Lib)] public static extern void vosk_recognizer_free(IntPtr rec);
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
        while (audio.TryDequeue(out _)) { }
        running = true;
        worker = new Thread(WorkerLoop) { IsBackground = true, Name = "CrimeTalk-ASR" };
        worker.Start();
    }

    private void StopWorker()
    {
        running = false;
        wake.Set();
        if (worker != null && worker.IsAlive) worker.Join(500);
        worker = null;
    }

    private void WorkerLoop()
    {
        bool inUtterance = false;
        int partialCounter = 0;
        while (running)
        {
            if (!audio.TryDequeue(out short[] pcm))
            {
                // Konuşma bitti (0.8 sn ses gelmedi): cümleyi kapat
                long idle = DateTime.UtcNow.Ticks - Interlocked.Read(ref lastAudioTicks);
                if (inUtterance && idle > TimeSpan.TicksPerMillisecond * 800)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        LangModel l = langs[i];
                        if (l.state != ModelState.Ready || !Wants(i)) continue;
                        Scan(l, Native.vosk_recognizer_final_result(l.rec), "text");
                        l.said.Clear();
                    }
                    inUtterance = false;
                }
                wake.WaitOne(200);
                continue;
            }

            inUtterance = true;
            partialCounter++;
            for (int i = 0; i < 2; i++)
            {
                LangModel l = langs[i];
                if (l.state != ModelState.Ready || !Wants(i)) continue;
                try
                {
                    if (Native.vosk_recognizer_accept_waveform_s(l.rec, pcm, pcm.Length) != 0)
                    {
                        Scan(l, Native.vosk_recognizer_result(l.rec), "text"); // cümle bitti
                        l.said.Clear();
                    }
                    else if (partialCounter % 2 == 0) // ~80 ms'de bir ara sonuç: kelime söylenir söylenmez yakalansın
                    {
                        Scan(l, Native.vosk_recognizer_partial_result(l.rec), "partial"); // ~160 ms'de bir ara sonuç
                    }
                }
                catch (Exception)
                {
                    l.state = ModelState.Failed;
                    l.error = "recognizer";
                }
            }
        }
    }

    /// <summary>Vosk'un JSON çıktısından metni alır, hırsızlık kelimesi varsa bildirir.</summary>
    private void Scan(LangModel l, IntPtr json, string field)
    {
        string text = ExtractField(FromUtf8(json), field);
        if (string.IsNullOrEmpty(text)) return;
        string word = CrimeWords.FindIn(text);
        if (word == null || l.said.Contains(word)) return;
        l.said.Add(word);
        found.Enqueue(word);
    }

    private static string ExtractField(string json, string field)
    {
        string key = "\"" + field + "\"";
        int k = json.IndexOf(key, StringComparison.Ordinal);
        if (k < 0) return "";
        int colon = json.IndexOf(':', k + key.Length);
        int q1 = colon >= 0 ? json.IndexOf('"', colon + 1) : -1;
        int q2 = q1 >= 0 ? json.IndexOf('"', q1 + 1) : -1;
        return q1 >= 0 && q2 > q1 ? json.Substring(q1 + 1, q2 - q1 - 1) : "";
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

    private void OnGUI()
    {
        if (CurrentMode == Mode.Off || PlayerStealth.Local == null || SessionMenuUI.IsPaused) return;
        if (WorkbenchUI.IsOpen || BriefingUI.IsOpen) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        string line = null;
        Color c = Color.white;
        if (Time.unscaledTime < shownUntil)
        {
            string how = Loc.T(shownLoud < 0.25f ? "crimetalk.whisper" : shownLoud > 0.75f ? "crimetalk.loud" : "crimetalk.normal",
                               Mathf.RoundToInt(CrimeTalk.HearRangeFor(shownLoud)));
            line = Loc.T("crimetalk.detected", shownWord) + "  " + how;
            c = new Color(1f, 0.38f, 0.33f);
        }
        else
        {
            string s = StatusLine(true);
            if (!string.IsNullOrEmpty(s)) { line = s; c = new Color(1f, 1f, 1f, 0.75f); }
        }
        if (line == null) return;

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

    /// <summary>Esc menüsü (VoiceChat.DrawSettings'in sonunda çağrılır).</summary>
    public static void DrawSettings(GUIStyle small, GUIStyle button)
    {
        GUILayout.Space(6f);
        GUILayout.Label(Loc.T("crimetalk.section"), small);

        Mode m = CurrentMode;
        GUILayout.BeginHorizontal();
        Color old = GUI.backgroundColor;
        foreach (Mode opt in new[] { Mode.Off, Mode.Both, Mode.Turkish, Mode.English })
        {
            GUI.backgroundColor = m == opt ? new Color(1f, 0.8f, 0.3f) : old;
            if (GUILayout.Button(Loc.T("crimetalk.mode." + opt.ToString().ToLowerInvariant()), button, GUILayout.Height(26f)))
            {
                CurrentMode = opt;
                if (Instance != null) Instance.RetryFailed();
            }
        }
        GUI.backgroundColor = old;
        GUILayout.EndHorizontal();

        if (Instance != null && CurrentMode != Mode.Off)
        {
            string s = Instance.StatusLine(false);
            if (!string.IsNullOrEmpty(s)) GUILayout.Label(s, small);
        }
        GUILayout.Label(Loc.T("crimetalk.help"), small);
    }

    /// <summary>Ayar değişince başarısız indirmeleri yeniden dene.</summary>
    private void RetryFailed()
    {
        foreach (LangModel l in langs) if (l.state == ModelState.Failed && l.model == IntPtr.Zero) { l.state = ModelState.Missing; l.error = ""; }
    }
}

/// <summary>
/// Hırsızlıkla ilgili kelimeler (Türkçe + İngilizce). Metin küçük harfe çevrilir ve Türkçe harfler sadeleştirilir
/// (ç→c, ş→s, ğ→g, ı→i, ö→o, ü→u); her kelimenin BAŞI aşağıdaki köklerden biriyle eşleşirse sayılır.
/// Yeni kelime eklemek için listelere kök ekleyin (sadeleştirilmiş haliyle).
/// </summary>
public static class CrimeWords
{
    // Türkçe kökler: çaldım/çaldık/çalalım/çalacağım/çalıyorum/çalmak, çalıntı, hırsız, soygun/soyalım/soyduk,
    // değiştirdim/değiştirelim..., sahte, aşırdım, arakladım, kaçırdık (eser), yürüttüm
    private static readonly string[] Turkish =
    {
        "cald", "calal", "calaca", "caliyo", "calmak", "calmay", "calinti", "calin",
        "hirsiz", "soygun", "soyal", "soyac", "soyd", "soyuyo",
        "degistird", "degistirel", "degistirec", "degistiriyo", "degistirmek",
        "sahte", "asird", "asiral", "arakla", "yuruttu",
    };

    // Türkçede tek başına sayılan kelimeler
    private static readonly string[] TurkishExact = { "cal", "calalim", "degistir" };

    // İngilizce kökler: steal/stealing/stole/stolen, swap/swapped, fake, forged/forgery, heist, rob/robbed/robbery,
    // thief/thieves, loot/looted, nicked, pinched
    private static readonly string[] English =
    {
        "steal", "stole", "swapp", "fake", "forger", "forged", "heist", "robb", "robber", "thief", "thieves", "loot", "nicked",
    };

    private static readonly string[] EnglishExact = { "rob", "swap", "swaps" };

    // Benzer ama ilgisiz kelimeler (çalışmak, çalgı...)
    private static readonly string[] Exclude = { "calis", "calg", "fakir", "stealth" };

    /// <summary>Metinde hırsızlık kelimesi varsa o kelimeyi (orijinal haliyle) döner, yoksa null.</summary>
    public static string FindIn(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string[] words = text.Split(new[] { ' ', '\t', ',', '.', '!', '?', '\'' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string raw in words)
        {
            string w = Fold(raw);
            if (w.Length < 3) continue;
            if (Starts(w, Exclude)) continue;
            if (Starts(w, Turkish) || Starts(w, English) || Exact(w, TurkishExact) || Exact(w, EnglishExact)) return raw;
        }
        return null;
    }

    private static bool Starts(string w, string[] list)
    {
        foreach (string p in list) if (w.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool Exact(string w, string[] list)
    {
        foreach (string p in list) if (w == p) return true;
        return false;
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
