using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Sesli sohbet (ek bir servis / paket gerektirmez; oyunun kendi ağ bağlantısını — Relay / NGO — kullanır).
///
///  - Mikrofon 16 kHz'e indirilir, 40 ms'lik paketlere bölünür ve IMA-ADPCM ile ~4 kat sıkıştırılır (~8 KB/sn).
///  - İstemci paketi sunucuya yollar ("SS_VoiceUp"), sunucu konuşanın numarasıyla diğer herkese dağıtır ("SS_VoiceDown").
///    Host kendi sesini doğrudan herkese yollar, gelen sesleri de kendisi çalar.
///  - Her konuşan için kısa bir titreşim tamponu (jitter buffer) + akışlı AudioClip ile çalınır.
///  - Mod: Bas-konuş (V basılı) ya da sürekli açık (V = sustur / aç). Sürekli açıkta ses eşiği altındaki gürültü gönderilmez.
///  - MESAFE: yakındaki arkadaş net duyulur; uzaklaştıkça sesi azalır (biraz da boğuklaşır), duvar arkasından daha kısık
///    gelir, ProxSilent'ten sonra hiç duyulmaz. Ses geldiği yönden (sağ / sol) gelir.
///  - Ekranda: altta ortada mikrofon kutusu (durum + canlı ses dalgası), solda konuşanların listesi,
///    konuşan oyuncunun başının üstünde ses dalgası.
///  - Ayarlar (Esc menüsü): mod, gelen ses seviyesi, mikrofon yükseltme, ses eşiği, mikrofon seçimi. PlayerPrefs'te saklanır.
///
/// Sahneye bir şey eklemeye gerek yok: oyun açılınca kendiliğinden oluşur.
/// </summary>
public class VoiceChat : MonoBehaviour
{
    public const int Rate = 16000;
    private const int FrameSamples = 640;                 // 40 ms
    private const int PacketBytes = 4 + FrameSamples / 2; // durum başlığı (3) + bayrak (1, şimdilik boş) + 4 bit/örnek
    private const string UpMsg = "SS_VoiceUp";
    private const string DownMsg = "SS_VoiceDown";

    public static VoiceChat Instance { get; private set; }

    /// <summary>Kendi sesimiz gönderilirken her 40 ms'lik paket (16 kHz, -1..1). VoiceKeywordListener dinler.</summary>
    public static event System.Action<float[], int> LocalSpeechFrame;

    /// <summary>Yasaklı (hırsızlık) kelime söylendi: mikrofon kutusu bu ana kadar YEŞİL yerine KIRMIZI yanar.</summary>
    public static float BannedFlashUntil;
    private static bool BannedFlash => Time.unscaledTime < BannedFlashUntil;

    // Mesafe: bu mesafeye kadar tam ses, sonra azalır, ProxSilent'te tamamen kesilir
    private const float ProxClear = 5f;
    private const float ProxSilent = 35f;
    private const float WallVolume = 0.6f;    // arada duvar varsa ses çarpanı
    private const float WallMuffle = 0.35f;   // arada duvar varsa ek boğukluk

    // ---------------- Ayarlar ----------------

    private const string KeyOpenMic = "SS_Voice_OpenMic", KeyVolume = "SS_Voice_Volume", KeyGain = "SS_Voice_Gain2",
                         KeyGate = "SS_Voice_Gate", KeyDevice = "SS_Voice_Device";

    /// <summary>true: sürekli açık mikrofon, false: bas-konuş (V).</summary>
    public static bool OpenMic
    {
        get => PlayerPrefs.GetInt(KeyOpenMic, 0) == 1;
        set => PlayerPrefs.SetInt(KeyOpenMic, value ? 1 : 0);
    }

    /// <summary>Gelen seslerin seviyesi (0–2).</summary>
    public static float VoiceVolume
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(KeyVolume, 1f), 0f, 2f);
        set => PlayerPrefs.SetFloat(KeyVolume, Mathf.Clamp(value, 0f, 2f));
    }

    /// <summary>Mikrofon yükseltme (0.5–4).</summary>
    public static float MicGain
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(KeyGain, 1f), 0.5f, 4f);
        set => PlayerPrefs.SetFloat(KeyGain, Mathf.Clamp(value, 0.5f, 4f));
    }

    /// <summary>Sürekli açık modda bu seviyenin altındaki sesler gönderilmez (0–0.1, RMS).</summary>
    public static float Gate
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(KeyGate, 0.012f), 0f, 0.1f);
        set => PlayerPrefs.SetFloat(KeyGate, Mathf.Clamp(value, 0f, 0.1f));
    }

    /// <summary>Seçili mikrofonun adı ("" = varsayılan).</summary>
    public static string DeviceName
    {
        get => PlayerPrefs.GetString(KeyDevice, "");
        set => PlayerPrefs.SetString(KeyDevice, value ?? "");
    }

    // ---------------- Durum ----------------

    /// <summary>Şu an ses gönderiliyor mu?</summary>
    public bool Transmitting { get; private set; }

    /// <summary>Sürekli açık modda V ile susturuldu mu?</summary>
    public bool OpenMicMuted { get; private set; }

    /// <summary>Mikrofon girişinin anlık seviyesi (0–1, arayüz için).</summary>
    public float InputLevel { get; private set; }

    /// <summary>Bilgisayarda mikrofon var mı?</summary>
    public bool HasMicrophone { get { string[] d = MicDevices; return d != null && d.Length > 0; } }

    // Microphone.devices her çağrıda yeni bir dizi üretir ve işletim sistemine sorar; Update / OnGUI her karede soruyordu.
    // Liste 1.5 sn'de bir yenilenir (takılan / çıkarılan mikrofon en geç bu kadar sonra görülür). Sadece ana thread'den okunur.
    private const float MicDevicesRefresh = 1.5f;
    private static string[] micDevicesCache;
    private static float micDevicesNext = float.NegativeInfinity;

    private static string[] MicDevices
    {
        get
        {
            float now = Time.unscaledTime;
            if (micDevicesCache == null || now >= micDevicesNext || now < micDevicesNext - MicDevicesRefresh * 2f)
            {
                micDevicesCache = Microphone.devices;
                micDevicesNext = now + MicDevicesRefresh;
            }
            return micDevicesCache;
        }
    }

    private NetworkManager regNm;
    private CustomMessagingManager regCmm;

    // Yakalama
    private string micDevice;
    private AudioClip micClip;
    private int micRate;
    private int lastMicPos;
    private bool micRunning;
    private float[] micRead = new float[4096];
    private double resT = -1.0;
    private float lastIn, limGain = 1f;
    private float[] fir, firHist;
    private int firPos, firRate;
    private readonly float[] frame = new float[FrameSamples];
    private int frameFill;
    private float gateHold, pttHold;
    private bool wasTx, hasPrevFrame;
    private readonly float[] prevFrame = new float[FrameSamples];
    private readonly float[] swapFrame = new float[FrameSamples];
    private bool pttHeld;
    private int restartMicTimer;

    // Kodlayıcı durumu
    private int encPred, encIndex;
    private byte seq;
    private readonly byte[] packet = new byte[PacketBytes];

    // Arayüz: kendi ses dalgamız
    private const int HistoryBars = 26;
    private readonly float[] history = new float[HistoryBars];
    private readonly bool[] historyTx = new bool[HistoryBars];
    private int historyHead;
    private float levelSmooth;

    private readonly Dictionary<ulong, Remote> remotes = new Dictionary<ulong, Remote>();

    /// <summary>Şu an hoparlörden duyulan biri konuşuyor mu (sesi mikrofona geri sızabilir; VoiceKeywordListener yanlış alarmı önlemek için kullanır).</summary>
    public bool AnyRemoteAudibleSpeaking
    {
        get
        {
            foreach (Remote r in remotes.Values) if (r.Speaking && r.Audible) return true;
            return false;
        }
    }
    private readonly List<ulong> toRemove = new List<ulong>();
    private readonly List<ulong> targets = new List<ulong>();
    private float nextCleanup;
    private GUIStyle textStyle, smallStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("VoiceChat");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceChat>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        StopMic();
        Unregister();
        if (Instance == this) Instance = null;
    }

    // =====================================================================
    //  Döngü
    // =====================================================================

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool active = nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient) && nm.CustomMessagingManager != null;

        if (!active)
        {
            if (regCmm != null) Unregister();
            if (micRunning) StopMic();
            if (remotes.Count > 0) ClearRemotes();
            if (ServerLastVoice.Count > 0) ServerLastVoice.Clear(); // oturum kapandı
            Transmitting = false;
            return;
        }

        if (regCmm != nm.CustomMessagingManager) Register(nm);

        bool hasLocalPlayer = LocalPlayer() != null;
        if (hasLocalPlayer && !micRunning && HasMicrophone)
        {
            if (restartMicTimer > 0) restartMicTimer--;
            else StartMic();
        }
        if (!hasLocalPlayer && micRunning) StopMic();

        ReadInput();
        if (micRunning) Capture();
        else
        {
            Transmitting = false;
            InputLevel = Mathf.MoveTowards(InputLevel, 0f, Time.unscaledDeltaTime * 3f);
        }

        // Gelen seslerin seviyesi ve eski konuşanların temizlenmesi
        float vol = VoiceVolume;
        foreach (Remote r in remotes.Values) ApplyHearing(r, vol);
        if (Time.unscaledTime > nextCleanup)
        {
            nextCleanup = Time.unscaledTime + 2f;
            toRemove.Clear();
            foreach (KeyValuePair<ulong, Remote> kv in remotes)
                if (Time.unscaledTime - kv.Value.LastHeard > 30f) toRemove.Add(kv.Key);
            foreach (ulong id in toRemove) { remotes[id].Dispose(); remotes.Remove(id); }

            // Sunucu: kopan istemcilerin "son ses" kaydını sil
            if (nm.IsServer && ServerLastVoice.Count > 0)
            {
                toRemove.Clear();
                foreach (ulong id in ServerLastVoice.Keys) if (!nm.ConnectedClients.ContainsKey(id)) toRemove.Add(id);
                foreach (ulong id in toRemove) ServerLastVoice.Remove(id);
            }
        }
    }

    private void ReadInput()
    {
        Keyboard kb = Keyboard.current;
        bool typing = ChatUI.IsTyping;
        pttHeld = kb != null && !typing && kb.vKey.isPressed;
        if (OpenMic && kb != null && !typing && kb.vKey.wasPressedThisFrame && LocalPlayer() != null)
        {
            OpenMicMuted = !OpenMicMuted;
            GameAudio.Play2D(SoundId.UiClick, 0.5f);
        }
    }

    /// <summary>Bir konuşanı nasıl duyuyorum? Mesafe arttıkça kısık (ve biraz boğuk), duvar arkasından daha kısık, yönlü.</summary>
    private void ApplyHearing(Remote r, float vol)
    {
        PlayerNetworkSetup me = LocalPlayer();
        PlayerNetworkSetup p = FindPlayer(r.Id);
        if (me == null || p == null || !p.IsSpawned)
        {
            r.Audible = r.Speaking;
            r.Apply(vol, 0f, 0f, 0f, 0f, 0f);
            return;
        }

        Vector3 myHead = me.transform.position + Vector3.up * 1.6f;
        Vector3 hisHead = p.transform.position + Vector3.up * 1.6f;
        float d = Vector3.Distance(myHead, hisHead);

        // Arada duvar var mı? (saniyede ~5 kez bakılır)
        if (Time.unscaledTime >= r.NextWallCheck)
        {
            r.NextWallCheck = Time.unscaledTime + 0.2f;
            r.Walled = d > 1.5f && d < ProxSilent + 2f && Blocked(myHead, hisHead, me.transform, p.transform);
        }

        // Yakın ses
        float prox = d <= ProxClear ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (d - ProxClear) / (ProxSilent - ProxClear));
        float proxMuffle = Mathf.Clamp01((d - ProxClear) / (ProxSilent - ProxClear)) * 0.4f;
        if (r.Walled) { prox *= WallVolume; proxMuffle = Mathf.Min(1f, proxMuffle + WallMuffle); }

        // Ses yönü: kamera sağına göre
        float pan = 0f;
        Camera cam = Camera.main;
        if (cam != null && d > 0.5f)
        {
            Vector3 dir = (hisHead - cam.transform.position).normalized;
            pan = Mathf.Clamp(Vector3.Dot(cam.transform.right, dir), -1f, 1f) * 0.7f;
        }

        r.Audible = r.Speaking && prox > 0.02f;
        r.Apply(vol * prox, 0f, proxMuffle, 0f, 0f, pan);
    }

    private static readonly RaycastHit[] wallHits = new RaycastHit[8];

    /// <summary>İki baş arasında (oyuncular hariç) katı bir şey var mı?</summary>
    private static bool Blocked(Vector3 a, Vector3 b, Transform me, Transform him)
    {
        Vector3 dir = b - a;
        float len = dir.magnitude;
        if (len < 0.01f) return false;
        int n = Physics.RaycastNonAlloc(a, dir / len, wallHits, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Transform t = wallHits[i].collider.transform;
            if (t.IsChildOf(me) || t.IsChildOf(him)) continue;
            if (wallHits[i].collider.GetComponentInParent<PlayerNetworkSetup>() != null) continue;
            return true;
        }
        return false;
    }

    private static PlayerNetworkSetup LocalPlayer()
    {
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
            if (p != null && p.IsSpawned && p.IsOwner) return p;
        return null;
    }

    // =====================================================================
    //  Mikrofon
    // =====================================================================

    /// <summary>Mikrofonu yeniden başlat (ör. ayarlardan başka mikrofon seçilince).</summary>
    public void RestartMic()
    {
        StopMic();
        restartMicTimer = 2;
    }

    private void StartMic()
    {
        string[] devs = Microphone.devices; // başlatırken güncel liste (seyrek çağrılır); önbelleği de tazele
        micDevicesCache = devs;
        micDevicesNext = Time.unscaledTime + MicDevicesRefresh;
        if (devs == null || devs.Length == 0) return;
        string wanted = DeviceName;
        micDevice = null;
        foreach (string d in devs) if (d == wanted) { micDevice = d; break; }
        if (micDevice == null) micDevice = devs[0];

        Microphone.GetDeviceCaps(micDevice, out int minF, out int maxF);
        micRate = (minF == 0 && maxF == 0) ? Rate : Mathf.Clamp(Rate, minF, maxF);
        micClip = Microphone.Start(micDevice, true, 1, micRate);
        if (micClip == null) { restartMicTimer = 300; return; }
        micRate = micClip.frequency;
        lastMicPos = 0;
        resT = -1.0;
        lastIn = 0f;
        limGain = 1f;
        fir = null;
        frameFill = 0;
        micRunning = true;
    }

    private void StopMic()
    {
        if (micRunning && micDevice != null) Microphone.End(micDevice);
        micRunning = false;
        micClip = null;
        Transmitting = false;
    }

    private void Capture()
    {
        if (micClip == null || !Microphone.IsRecording(micDevice)) { StopMic(); restartMicTimer = 120; return; }
        int pos = Microphone.GetPosition(micDevice);
        if (pos < 0) return;
        int len = micClip.samples;
        int avail = (pos - lastMicPos + len) % len;
        if (avail <= 0) return;
        if (micRead.Length < avail) micRead = new float[Mathf.NextPowerOfTwo(avail)];

        // Döngülü klipten iki parça halinde oku (sonu + başı). Span: doğrudan micRead'in ilgili dilimine yazılır,
        // kare başına geçici dizi ayrılmaz (GetData(float[]) dizinin TAMAMINI doldurduğu için eskiden her seferinde new float[] gerekiyordu).
        int first = Mathf.Min(avail, len - lastMicPos);
        micClip.GetData(new System.Span<float>(micRead, 0, first), lastMicPos);
        if (avail > first) micClip.GetData(new System.Span<float>(micRead, first, avail - first), 0);
        lastMicPos = pos;
        Resample(micRead, avail);
    }

    /// <summary>Mikrofon hızından 16 kHz'e (doğrusal ara değer; indirirken önce basit alçak geçiren süzgeç).</summary>
    private void Resample(float[] x, int n)
    {
        double step = micRate / (double)Rate;
        if (step > 1.2)
        {
            // Örnekleme düşürmeden önce keskin alçak geçiren FIR (7 kHz): yüksek frekanslar sese "cızırtı" olarak katlanmasın
            if (fir == null || firRate != micRate) BuildFir();
            int taps = fir.Length;
            for (int i = 0; i < n; i++)
            {
                firHist[firPos] = x[i];
                float acc = 0f;
                int k = firPos;
                for (int t = 0; t < taps; t++)
                {
                    acc += fir[t] * firHist[k];
                    k = k == 0 ? taps - 1 : k - 1;
                }
                firPos = (firPos + 1) % taps;
                x[i] = acc;
            }
        }

        while (System.Math.Floor(resT) + 1 <= n - 1)
        {
            int i0 = (int)System.Math.Floor(resT);
            float frac = (float)(resT - i0);
            float a = i0 < 0 ? lastIn : x[i0];
            float b = x[i0 + 1];
            PushSample(a + (b - a) * frac);
            resT += step;
        }
        resT -= n;
        lastIn = x[n - 1];
    }

    /// <summary>Blackman pencereli sinc alçak geçiren süzgeç (kesim 7 kHz, 47 katsayı).</summary>
    private void BuildFir()
    {
        const int taps = 47;
        fir = new float[taps];
        firHist = new float[taps];
        firPos = 0;
        firRate = micRate;
        double fc = 7000.0 / micRate, sum = 0;
        int m = taps - 1;
        for (int i = 0; i < taps; i++)
        {
            double x = i - m / 2.0;
            double sinc = System.Math.Abs(x) < 1e-9 ? 2 * fc : System.Math.Sin(2 * System.Math.PI * fc * x) / (System.Math.PI * x);
            double w = 0.42 - 0.5 * System.Math.Cos(2 * System.Math.PI * i / m) + 0.08 * System.Math.Cos(4 * System.Math.PI * i / m);
            fir[i] = (float)(sinc * w);
            sum += fir[i];
        }
        for (int i = 0; i < taps; i++) fir[i] = (float)(fir[i] / sum);
    }

    private void PushSample(float s)
    {
        frame[frameFill++] = s;
        if (frameFill < FrameSamples) return;
        frameFill = 0;
        ProcessFrame();
    }

    private void ProcessFrame()
    {
        // Yükseltme + yumuşak sınırlayıcı: bağırınca ses kırpılıp cızırdamasın (kazanç paket boyunca kayarak değişir)
        float gain = MicGain;
        float peak = 0f;
        for (int i = 0; i < FrameSamples; i++) peak = Mathf.Max(peak, Mathf.Abs(frame[i] * gain));
        float targetLim = peak > 0.85f ? 0.85f / peak : 1f;
        float fromLim = limGain;
        limGain = targetLim < limGain ? targetLim : Mathf.Lerp(limGain, targetLim, 0.15f); // hızlı kıs, yavaş aç
        double sum = 0;
        for (int i = 0; i < FrameSamples; i++)
        {
            float lg = Mathf.Lerp(fromLim, limGain, i / (float)FrameSamples);
            float v = Mathf.Clamp(frame[i] * gain * lg, -1f, 1f);
            frame[i] = v;
            sum += v * v;
        }
        float rms = (float)System.Math.Sqrt(sum / FrameSamples);
        InputLevel = Mathf.Clamp01(Mathf.Sqrt(rms) * 2.2f);

        bool allowed = LocalPlayer() != null;
        float frameSec = FrameSamples / (float)Rate;
        bool tx;
        if (OpenMic)
        {
            // Eşik geçilince 0.7 sn açık kalır: kelime araları / sessiz heceler kesilmez
            if (rms > Gate) gateHold = 0.7f;
            else gateHold -= frameSec;
            tx = allowed && !OpenMicMuted && gateHold > 0f;
        }
        else
        {
            // V bırakıldıktan sonra 0.25 sn daha gönder: son hece yarım kalmasın
            if (pttHeld) pttHold = 0.25f;
            else pttHold -= frameSec;
            tx = allowed && pttHold > 0f;
        }
        packet[3] = 0;
        Transmitting = tx;

        // Arayüz geçmişi
        history[historyHead] = InputLevel;
        historyTx[historyHead] = tx;
        historyHead = (historyHead + 1) % HistoryBars;

        if (tx)
        {
            // Konuşma yeni başladıysa bir önceki 40 ms'yi de gönder: ilk hece kesilmez
            if (!wasTx && hasPrevFrame)
            {
                // Kelime algılayıcıya (sadece ona, ağa değil) daha uzun ön kayıt: "s", "ç", "ş" gibi yumuşak başlangıçlar
                // eşiğin altında kalıyor, 40 ms'lik ön kayıt yetmiyordu ("steal" → "teal/still", "çal" → "al").
                FeedListenerPreRoll();
                System.Array.Copy(frame, swapFrame, FrameSamples);
                System.Array.Copy(prevFrame, frame, FrameSamples);
                SendFrame();
                System.Array.Copy(swapFrame, frame, FrameSamples);
            }
            SendFrame();
        }
        wasTx = tx;
        if (tx) preRollCount = 0; // konuşma sürerken ön kayıt tutulmaz (bir sonraki başlangıçta eski konuşma tekrar verilmesin)
        else PushPreRoll();
        System.Array.Copy(frame, prevFrame, FrameSamples);
        hasPrevFrame = true;
    }

    // ---------------- Kelime algılayıcı ön kaydı ----------------
    private const int ListenerPreRollFrames = 6; // 6 × 40 ms = 240 ms (en yenisi zaten prevFrame olarak gönderilir)
    private readonly float[][] preRoll = new float[ListenerPreRollFrames][];
    private int preRollHead, preRollCount;

    private void PushPreRoll()
    {
        if (preRoll[preRollHead] == null) preRoll[preRollHead] = new float[FrameSamples];
        System.Array.Copy(frame, preRoll[preRollHead], FrameSamples);
        preRollHead = (preRollHead + 1) % ListenerPreRollFrames;
        if (preRollCount < ListenerPreRollFrames) preRollCount++;
    }

    /// <summary>Konuşma başlarken: ön kayıttaki (prevFrame hariç) eski paketleri eskiden yeniye yalnızca LocalSpeechFrame'e verir.</summary>
    private void FeedListenerPreRoll()
    {
        if (LocalSpeechFrame == null) { preRollCount = 0; return; }
        // En yeni kayıt = prevFrame (hemen ardından SendFrame ile zaten gidiyor): onu atla
        for (int n = preRollCount - 1; n >= 1; n--)
        {
            int k = (preRollHead - 1 - n + ListenerPreRollFrames * 2) % ListenerPreRollFrames;
            LocalSpeechFrame(preRoll[k], FrameSamples);
        }
        preRollCount = 0;
    }

    // =====================================================================
    //  Ağ
    // =====================================================================

    private void Register(NetworkManager nm)
    {
        Unregister();
        regNm = nm;
        regCmm = nm.CustomMessagingManager;
        regCmm.RegisterNamedMessageHandler(UpMsg, OnUp);
        regCmm.RegisterNamedMessageHandler(DownMsg, OnDown);
    }

    private void Unregister()
    {
        if (regCmm != null)
        {
            try
            {
                regCmm.UnregisterNamedMessageHandler(UpMsg);
                regCmm.UnregisterNamedMessageHandler(DownMsg);
            }
            catch (System.Exception) { }
        }
        regCmm = null;
        regNm = null;
    }

    private void SendFrame()
    {
        LocalSpeechFrame?.Invoke(frame, FrameSamples); // hırsızlık kelimesi dinleyicisi (VoiceKeywordListener)
        NetworkManager nm = regNm;
        if (nm == null || regCmm == null) return;
        EncodeFrame();
        seq++;

        if (nm.IsServer)
        {
            // Host: doğrudan herkese (kendisi hariç)
            SendDown(nm, nm.LocalClientId, packet, PacketBytes, ulong.MaxValue);
        }
        else
        {
            using (var w = new FastBufferWriter(PacketBytes + 8, Allocator.Temp))
            {
                w.WriteValueSafe(seq);
                w.WriteValueSafe((ushort)PacketBytes);
                w.WriteBytesSafe(packet, PacketBytes);
                regCmm.SendNamedMessage(UpMsg, NetworkManager.ServerClientId, w, NetworkDelivery.UnreliableSequenced);
            }
        }
    }

    /// <summary>Sunucu: konuşanın paketini (konuşan ve host hariç) bütün istemcilere dağıtır.</summary>
    private void SendDown(NetworkManager nm, ulong speaker, byte[] data, int len, ulong except)
    {
        targets.Clear();
        foreach (ulong id in nm.ConnectedClientsIds)
            if (id != speaker && id != nm.LocalClientId && id != except) targets.Add(id);
        if (targets.Count == 0) return;
        using (var w = new FastBufferWriter(len + 16, Allocator.Temp))
        {
            w.WriteValueSafe(speaker);
            w.WriteValueSafe((ushort)len);
            w.WriteBytesSafe(data, len);
            regCmm.SendNamedMessage(DownMsg, targets, w, NetworkDelivery.UnreliableSequenced);
        }
    }

    // Gelen paket tamponu: her paket için new byte[] yerine tek dizi. Güvenli çünkü OnUp / OnDown, SendDown ve Receive
    // hepsi ANA thread'de ve senkron çalışır: FastBufferWriter baytları kopyalar, DecodeFrame hemen çözer, VoicePlayback.Push
    // örnekleri kilit altında kendi halka tamponuna kopyalar. Ses thread'i (OnAudioFilterRead) bu diziye hiç dokunmaz.
    // Bu diziyi saklayan / başka thread'e veren bir kod eklenirse kopyası verilmeli.
    private readonly byte[] rxPacket = new byte[PacketBytes];

    /// <summary>Sunucuda: bir istemciden ses geldi → herkese dağıt, host ise kendisi de çal.</summary>
    /// <summary>Sunucu: her istemciden son ses paketinin geldiği an (Time.unscaledTime). Host'un kendi sesi buraya girmez.</summary>
    public static readonly Dictionary<ulong, float> ServerLastVoice = new Dictionary<ulong, float>();

    private void OnUp(ulong sender, FastBufferReader reader)
    {
        NetworkManager nm = regNm;
        if (nm == null || !nm.IsServer) return;
        if (!reader.TryBeginRead(3)) return;
        reader.ReadValue(out byte _);
        reader.ReadValue(out ushort len);
        if (len != PacketBytes || !reader.TryBeginRead(len)) return;
        byte[] data = rxPacket; // len == PacketBytes doğrulandı; yeniden kullanılan tampon
        reader.ReadBytes(ref data, len);
        ServerLastVoice[sender] = Time.unscaledTime; // CrimeTalk: bu istemci gerçekten ses gönderiyor mu (sahte bildirime karşı)

        SendDown(nm, sender, data, len, ulong.MaxValue);
        if (nm.IsHost && sender != nm.LocalClientId) Receive(sender, data);
    }

    /// <summary>İstemcide: sunucudan başka birinin sesi geldi.</summary>
    private void OnDown(ulong sender, FastBufferReader reader)
    {
        NetworkManager nm = regNm;
        if (nm == null || nm.IsServer) return;
        if (!reader.TryBeginRead(10)) return;
        reader.ReadValue(out ulong speaker);
        reader.ReadValue(out ushort len);
        if (len != PacketBytes || !reader.TryBeginRead(len)) return;
        byte[] data = rxPacket; // len == PacketBytes doğrulandı; yeniden kullanılan tampon
        reader.ReadBytes(ref data, len);
        if (speaker == nm.LocalClientId) return;
        Receive(speaker, data);
    }

    private readonly float[] decoded = new float[FrameSamples];

    private void Receive(ulong speaker, byte[] data)
    {
        if (!remotes.TryGetValue(speaker, out Remote r))
        {
            r = new Remote(speaker, transform);
            r.SetVolume(VoiceVolume);
            remotes[speaker] = r;
        }
        DecodeFrame(data, decoded);
        r.Push(decoded, FrameSamples);
    }

    private void ClearRemotes()
    {
        foreach (Remote r in remotes.Values) r.Dispose();
        remotes.Clear();
    }

    // =====================================================================
    //  IMA-ADPCM (4 bit / örnek). Her paket başında kodlayıcı durumu → kayıp paket sonrası kendini toparlar.
    // =====================================================================

    private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
    private static readonly int[] StepTable =
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
        130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060,
        1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
        7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    };

    private void EncodeFrame()
    {
        short p0 = (short)encPred;
        packet[0] = (byte)(p0 & 0xff);
        packet[1] = (byte)((p0 >> 8) & 0xff);
        packet[2] = (byte)encIndex;
        for (int i = 0; i < FrameSamples; i++)
        {
            int s = Mathf.Clamp(Mathf.RoundToInt(frame[i] * 32767f), -32768, 32767);
            int diff = s - encPred;
            int code = 0;
            if (diff < 0) { code = 8; diff = -diff; }
            int step = StepTable[encIndex];
            int delta = step >> 3;
            if (diff >= step) { code |= 4; diff -= step; delta += step; }
            step >>= 1;
            if (diff >= step) { code |= 2; diff -= step; delta += step; }
            step >>= 1;
            if (diff >= step) { code |= 1; delta += step; }
            encPred += (code & 8) != 0 ? -delta : delta;
            encPred = Mathf.Clamp(encPred, -32768, 32767);
            encIndex = Mathf.Clamp(encIndex + IndexTable[code], 0, 88);

            int b = 4 + (i >> 1);
            if ((i & 1) == 0) packet[b] = (byte)code;
            else packet[b] |= (byte)(code << 4);
        }
    }

    private static void DecodeFrame(byte[] data, float[] output)
    {
        int pred = (short)(data[0] | (data[1] << 8));
        int index = Mathf.Clamp(data[2], 0, 88);
        for (int i = 0; i < FrameSamples; i++)
        {
            int b = data[4 + (i >> 1)];
            int code = (i & 1) == 0 ? b & 0x0f : (b >> 4) & 0x0f;
            int step = StepTable[index];
            int delta = step >> 3;
            if ((code & 4) != 0) delta += step;
            if ((code & 2) != 0) delta += step >> 1;
            if ((code & 1) != 0) delta += step >> 2;
            pred += (code & 8) != 0 ? -delta : delta;
            pred = Mathf.Clamp(pred, -32768, 32767);
            index = Mathf.Clamp(index + IndexTable[code], 0, 88);
            output[i] = pred / 32768f;
        }
    }

    // =====================================================================
    //  Uzak konuşan: titreşim tamponu + akışlı AudioClip
    // =====================================================================

    private class Remote
    {
        public readonly ulong Id;
        public float LastHeard = -100f;
        public float Level;
        public bool Audible;          // şu an beni duyabiliyor muyum (liste / baş üstü simgesi için)
        public bool Walled;           // arada duvar var
        public float NextWallCheck;

        private readonly GameObject go;
        private readonly VoicePlayback playback;

        public Remote(ulong id, Transform parent)
        {
            Id = id;
            go = new GameObject("Voice_" + id);
            go.transform.SetParent(parent, false);
            playback = go.AddComponent<VoicePlayback>();
        }

        public bool Speaking => Time.unscaledTime - LastHeard < 0.25f;

        public void SetVolume(float v)
        {
            if (playback != null) playback.Gain = v;
        }

        public void SetRadio(float amount)
        {
            if (playback != null) playback.Radio = amount;
        }

        /// <summary>Ses seviyesi + boğukluk + yön.</summary>
        public void Apply(float volume, float radio, float muffle, float stat, float drop, float pan)
        {
            if (playback != null)
            {
                playback.Gain = volume;
                playback.Radio = radio;
                playback.Muffle = muffle;
                playback.Static = stat;
                playback.Dropout = drop;
                playback.Pan = pan;
            }
        }

        public void Push(float[] samples, int n)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += samples[i] * samples[i];
            float rms = (float)System.Math.Sqrt(sum / n);
            Level = Mathf.Clamp01(Mathf.Sqrt(rms) * 2.2f);
            LastHeard = Time.unscaledTime;
            if (playback != null) playback.Push(samples, n);
        }

        public void Dispose()
        {
            if (go != null) Object.Destroy(go);
        }
    }

    // =====================================================================
    //  Arayüz
    // =====================================================================

    private void OnGUI()
    {
        if (regCmm == null || LocalPlayer() == null) return;
        EnsureStyles();
        if (!WorkbenchUI.IsOpen && !BriefingUI.IsOpen) DrawMicWidget();
        DrawSpeakers();
    }

    private void EnsureStyles()
    {
        if (textStyle != null) return;
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
        textStyle.normal.textColor = Color.white;
        smallStyle = new GUIStyle(textStyle) { fontSize = 12, fontStyle = FontStyle.Normal, wordWrap = false };
        smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
    }

    private static readonly Color Green = new Color(0.35f, 0.95f, 0.5f);
    private static readonly Color Red = new Color(1f, 0.38f, 0.33f);

    /// <summary>Altta ortada: mikrofon simgesi, durum yazısı, canlı ses dalgası.</summary>
    private void DrawMicWidget()
    {
        bool noMic = !HasMicrophone || !micRunning;
        string state;
        Color live = BannedFlash ? Red : Green; // yasaklı kelime söylenince kırmızı
        if (noMic) state = Loc.T("voice.hud.nomic");
        else if (BannedFlash) state = Loc.T("voice.hud.banned");
        else if (Transmitting) state = Loc.T("voice.hud.talking");
        else if (OpenMic) state = Loc.T(OpenMicMuted ? "voice.hud.muted" : "voice.hud.open");
        else state = Loc.T("voice.hud.ptt");
        string mode = Loc.T(OpenMic ? "voice.mode.open" : "voice.mode.ptt");

        // Kutunun genişliği yazıya göre ayarlanır: yazı asla taşmaz / alt satıra kaymaz
        float textW = Mathf.Max(textStyle.CalcSize(new GUIContent(state)).x, smallStyle.CalcSize(new GUIContent(mode)).x);
        const float iconW = 44f, waveW = 130f, pad = 14f, h = 52f;
        float w = iconW + textW + pad + waveW + pad;
        Rect r = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 12f, w, h);
        // Yasaklı kelime: bütün kutu kırmızıya döner (hemen fark edilsin)
        UIUtil.DrawRect(r, BannedFlash ? new Color(0.55f, 0.06f, 0.06f, 0.92f) : new Color(0.05f, 0.06f, 0.08f, Transmitting ? 0.82f : 0.65f));
        if (Transmitting || BannedFlash) UIUtil.DrawRect(new Rect(r.x, r.y, r.width, BannedFlash ? 3f : 2f), live);

        bool slash = noMic || (OpenMic && OpenMicMuted);
        bool muted = noMic || (OpenMic ? OpenMicMuted : !Transmitting);
        Color iconC = slash ? Red : (Transmitting || BannedFlash) ? live : new Color(0.85f, 0.85f, 0.85f);
        DrawMicIcon(new Vector2(r.x + 24f, r.y + h * 0.5f), iconC, slash);

        GUI.Label(new Rect(r.x + iconW, r.y + 5f, textW + 4f, 24f), state, textStyle);
        GUI.Label(new Rect(r.x + iconW, r.y + 27f, textW + 4f, 20f), mode, smallStyle);

        // Ses dalgası (son ~1 sn): gönderilen kısım yeşil, gönderilmeyen soluk
        float bx = r.x + iconW + textW + pad, mid = r.y + h * 0.5f;
        float barW = waveW / HistoryBars;
        for (int i = 0; i < HistoryBars; i++)
        {
            int idx = (historyHead + i) % HistoryBars;
            float v = noMic ? 0f : history[idx];
            float bh = Mathf.Max(2f, v * (h - 14f));
            Color c = historyTx[idx] ? live : new Color(1f, 1f, 1f, muted ? 0.18f : 0.32f);
            UIUtil.DrawRect(new Rect(bx + i * barW + 1f, mid - bh * 0.5f, Mathf.Max(1f, barW - 2f), bh), c);
        }
    }

    /// <summary>Basit mikrofon çizimi (kapsül + sap + taban); kapalıysa üstünde çapraz çizgi.</summary>
    private static void DrawMicIcon(Vector2 c, Color col, bool slash)
    {
        UIUtil.DrawRect(new Rect(c.x - 5f, c.y - 13f, 10f, 15f), col);
        UIUtil.DrawRect(new Rect(c.x - 4f, c.y - 14f, 8f, 2f), col);
        UIUtil.DrawRect(new Rect(c.x - 9f, c.y - 2f, 2f, 6f), col);
        UIUtil.DrawRect(new Rect(c.x + 7f, c.y - 2f, 2f, 6f), col);
        UIUtil.DrawRect(new Rect(c.x - 9f, c.y + 4f, 18f, 2f), col);
        UIUtil.DrawRect(new Rect(c.x - 1f, c.y + 6f, 2f, 5f), col);
        UIUtil.DrawRect(new Rect(c.x - 6f, c.y + 11f, 12f, 2f), col);
        if (!slash) return;
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(-45f, c);
        UIUtil.DrawRect(new Rect(c.x - 14f, c.y - 1.5f, 28f, 3f), Red);
        GUI.matrix = m;
    }

    /// <summary>Solda konuşanların listesi + konuşanların başının üstünde ses dalgası.</summary>
    private void DrawSpeakers()
    {
        Camera cam = Camera.main;
        float y = Screen.height * 0.42f;
        foreach (Remote r in remotes.Values)
        {
            if (!r.Speaking || !r.Audible) continue; // uzakta (duyulmayan) yakın konuşmayı gösterme
            PlayerNetworkSetup p = FindPlayer(r.Id);
            string name = p != null ? p.PlayerName : "?";

            // Liste satırı
            float nameW = textStyle.CalcSize(new GUIContent(name)).x;
            Rect row = new Rect(12f, y, Mathf.Max(150f, 48f + nameW), 28f);
            UIUtil.DrawRect(row, new Color(0.05f, 0.06f, 0.08f, 0.7f));
            UIUtil.DrawRect(new Rect(row.x, row.y, 3f, row.height), p != null ? p.PlayerColor : Green);
            DrawWaves(new Vector2(row.x + 18f, row.y + 14f), r.Level, 0.8f, Green);
            GUI.Label(new Rect(row.x + 38f, row.y + 3f, nameW + 6f, 22f), name, textStyle);
            y += 32f;

            // Baş üstü simge
            if (cam == null || p == null) continue;
            PlayerStealth st = p.GetComponent<PlayerStealth>();
            if (st != null && st.IsHidden) continue;
            Vector3 sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 2.55f);
            if (sp.z <= 0f || sp.z > 40f) continue;
            DrawWaves(new Vector2(sp.x, Screen.height - sp.y), r.Level, 1.1f, Green);
        }
    }

    /// <summary>Ortadan dışa doğru 5 çubuk, ses seviyesine göre zıplar.</summary>
    private static void DrawWaves(Vector2 c, float level, float scale, Color col)
    {
        float t = Time.unscaledTime * 14f;
        for (int i = -2; i <= 2; i++)
        {
            float wobble = 0.55f + 0.45f * Mathf.Sin(t + i * 1.7f);
            float hgt = (4f + (18f - Mathf.Abs(i) * 4f) * Mathf.Clamp01(level * 1.3f) * wobble) * scale;
            UIUtil.DrawRect(new Rect(c.x + i * 5f * scale - 1.5f * scale, c.y - hgt * 0.5f, 3f * scale, hgt), col);
        }
    }

    private static PlayerNetworkSetup FindPlayer(ulong clientId)
    {
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
            if (p != null && p.OwnerClientId == clientId) return p;
        return null;
    }

    // =====================================================================
    //  Esc menüsü ayarları (SessionMenuUI çağırır)
    // =====================================================================

    public static void DrawSettings(GUIStyle section, GUIStyle small, GUIStyle button)
    {
        GUILayout.Label(Loc.T("voice.section"), section);

        bool open = OpenMic;
        GUILayout.BeginHorizontal();
        Color old = GUI.backgroundColor;
        if (!open) GUI.backgroundColor = new Color(1f, 0.8f, 0.3f);
        if (GUILayout.Button(Loc.T("voice.mode.ptt"), button, GUILayout.Height(28f))) OpenMic = false;
        GUI.backgroundColor = open ? new Color(1f, 0.8f, 0.3f) : old;
        if (GUILayout.Button(Loc.T("voice.mode.open"), button, GUILayout.Height(28f))) OpenMic = true;
        GUI.backgroundColor = old;
        GUILayout.EndHorizontal();

        // Mikrofon seçimi (tıklayınca sıradakine geçer). Önbellekli liste: menü açıkken her OnGUI'de cihaz sorgulanmasın.
        string[] devs = MicDevices;
        if (devs == null || devs.Length == 0) GUILayout.Label(Loc.T("voice.nodevice"), small);
        else
        {
            string cur = DeviceName;
            int idx = System.Array.IndexOf(devs, cur);
            string shown = idx >= 0 ? devs[idx] : devs[0];
            if (GUILayout.Button(Loc.T("voice.device", shown), button, GUILayout.Height(28f)) && devs.Length > 1)
            {
                DeviceName = devs[((idx < 0 ? 0 : idx) + 1) % devs.Length];
                if (Instance != null) Instance.RestartMic();
            }
        }

        GUILayout.Label(Loc.T("voice.volume", Mathf.RoundToInt(VoiceVolume * 100f)), small);
        float v = GUILayout.HorizontalSlider(VoiceVolume, 0f, 2f);
        if (!Mathf.Approximately(v, VoiceVolume)) VoiceVolume = v;

        GUILayout.Label(Loc.T("voice.gain", MicGain.ToString("0.0")), small);
        float g = GUILayout.HorizontalSlider(MicGain, 0.5f, 4f);
        if (!Mathf.Approximately(g, MicGain)) MicGain = g;

        // Eşik: canlı mikrofon seviyesinin üstünde gösterilir (çizgiyi konuşurken geçen, sessizken geçmeyen yere getir)
        GUILayout.Label(Loc.T("voice.gate"), small);
        Rect meter = GUILayoutUtility.GetRect(200f, 10f, GUILayout.ExpandWidth(true), GUILayout.Height(10f));
        float lvl = Instance != null ? Instance.InputLevel : 0f;
        UIUtil.DrawRect(meter, new Color(0f, 0f, 0f, 0.5f));
        UIUtil.DrawRect(new Rect(meter.x, meter.y, meter.width * lvl, meter.height), Green);
        float gateLvl = Mathf.Clamp01(Mathf.Sqrt(Gate) * 2.2f);
        UIUtil.DrawRect(new Rect(meter.x + meter.width * gateLvl - 1f, meter.y - 3f, 3f, meter.height + 6f), Red);
        float gt = GUILayout.HorizontalSlider(Gate, 0f, 0.1f);
        if (!Mathf.Approximately(gt, Gate)) Gate = gt;

    }
}
