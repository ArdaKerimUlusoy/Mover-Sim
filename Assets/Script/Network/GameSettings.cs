using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Oyuncunun kendi bilgisayarında saklanan ayarlar: ses, fare (hassasiyet, Y ekseni), görüntü (pencere, çözünürlük,
/// kalite, dikey senkron, FPS sınırı, görüş alanı). Ayarlar ekranı (SettingsUI) bunları değiştirir; değişiklik anında uygulanır.
/// </summary>
public static class GameSettings
{
    private const string VolumeKey = "MoverSim_Volume";
    private const string SensitivityKey = "MoverSim_Sensitivity";
    private const string InvertKey = "MoverSim_InvertY";
    private const string QualityKey = "MoverSim_Quality";
    private const string VSyncKey = "MoverSim_VSync";
    private const string FpsKey = "MoverSim_FpsCap";
    private const string FovKey = "MoverSim_Fov";
    private const string FullKey = "MoverSim_Fullscreen";
    private const string ResWKey = "MoverSim_ResW", ResHKey = "MoverSim_ResH";

    public const float MinSensitivity = 0.2f;
    public const float MaxSensitivity = 3f;
    public const float MinFov = 60f, MaxFov = 100f;
    /// <summary>FPS sınırı seçenekleri (0 = sınırsız).</summary>
    public static readonly int[] FrameCaps = { 30, 60, 120, 144, 0 };

    private static bool loaded;
    private static float masterVolume = 1f;
    private static float mouseSensitivity = 1f;
    private static bool invertY;
    private static int quality = -1;
    private static bool vsync = true;
    private static int fpsCap;
    private static float fov = -1f;

    /// <summary>Genel ses seviyesi (0–1).</summary>
    public static float MasterVolume
    {
        get { Load(); return masterVolume; }
        set
        {
            Load();
            float v = Mathf.Clamp01(value);
            if (Mathf.Approximately(v, masterVolume)) return;
            masterVolume = v;
            AudioListener.volume = masterVolume;
            PlayerPrefs.SetFloat(VolumeKey, masterVolume);
        }
    }

    /// <summary>Fare hassasiyeti çarpanı (1 = varsayılan).</summary>
    public static float MouseSensitivity
    {
        get { Load(); return mouseSensitivity; }
        set
        {
            Load();
            float v = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            if (Mathf.Approximately(v, mouseSensitivity)) return;
            mouseSensitivity = v;
            PlayerPrefs.SetFloat(SensitivityKey, mouseSensitivity);
        }
    }

    /// <summary>Fareyle yukarı / aşağı bakış ters mi?</summary>
    public static bool InvertY
    {
        get { Load(); return invertY; }
        set { Load(); if (invertY == value) return; invertY = value; PlayerPrefs.SetInt(InvertKey, value ? 1 : 0); }
    }

    /// <summary>Grafik kalitesi (QualitySettings.names sırası).</summary>
    public static int QualityLevel
    {
        get { Load(); return quality < 0 ? QualitySettings.GetQualityLevel() : quality; }
        set
        {
            Load();
            int n = QualitySettings.names != null ? QualitySettings.names.Length : 1;
            int v = Mathf.Clamp(value, 0, Mathf.Max(0, n - 1));
            if (v == quality) return;
            quality = v;
            QualitySettings.SetQualityLevel(v, true);
            ApplyFrameSettings(); // kalite seviyesi vSync'i değiştirebilir: seçimimiz geçerli kalsın
            PlayerPrefs.SetInt(QualityKey, v);
        }
    }

    /// <summary>Dikey senkron (açıkken FPS sınırı ekranın yenileme hızıdır).</summary>
    public static bool VSync
    {
        get { Load(); return vsync; }
        set { Load(); if (vsync == value) return; vsync = value; ApplyFrameSettings(); PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0); }
    }

    /// <summary>FPS sınırı (0 = sınırsız). Yalnızca dikey senkron kapalıyken geçerli.</summary>
    public static int FrameCap
    {
        get { Load(); return fpsCap; }
        set { Load(); if (fpsCap == value) return; fpsCap = Mathf.Max(0, value); ApplyFrameSettings(); PlayerPrefs.SetInt(FpsKey, fpsCap); }
    }

    /// <summary>Görüş alanı (derece). Kayıt yoksa oyuncu kamerasının kendi değeri gösterilir ve değiştirilmez.</summary>
    public static float FieldOfView
    {
        get
        {
            Load();
            if (fov > 0f) return fov;
            Camera c = Camera.main;
            return c != null ? Mathf.Clamp(c.fieldOfView, MinFov, MaxFov) : 70f;
        }
        set
        {
            Load();
            float v = Mathf.Clamp(value, MinFov, MaxFov);
            if (fov > 0f && Mathf.Approximately(v, fov)) return;
            if (fov <= 0f && Mathf.Approximately(v, FieldOfView)) return; // kaydırıcıya dokunulmadı
            fov = v;
            PlayerPrefs.SetFloat(FovKey, fov);
        }
    }

    /// <summary>Oyuncu bir görüş alanı seçti mi? (Seçmediyse kameranın kendi değeri kullanılır.)</summary>
    public static bool HasCustomFov { get { Load(); return fov > 0f; } }

    /// <summary>Tam ekran (pencereli tam ekran) mı, pencere mi?</summary>
    public static bool Fullscreen
    {
        get => Screen.fullScreenMode != FullScreenMode.Windowed;
        set
        {
            if (Fullscreen == value) return;
            Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            PlayerPrefs.SetInt(FullKey, value ? 1 : 0);
        }
    }

    /// <summary>Seçilebilir çözünürlükler (yenileme hızları birleştirilmiş, küçükten büyüğe).</summary>
    public static Resolution[] AvailableResolutions()
    {
        var list = new List<Resolution>();
        var seen = new HashSet<long>();
        Resolution[] all = Screen.resolutions;
        for (int i = all.Length - 1; i >= 0; i--) // en yüksek yenileme hızı önce gelsin
        {
            long k = ((long)all[i].width << 20) | (long)all[i].height;
            if (all[i].width < 800 || !seen.Add(k)) continue;
            list.Add(all[i]);
        }
        list.Sort((a, b) => a.width != b.width ? a.width.CompareTo(b.width) : a.height.CompareTo(b.height));
        return list.ToArray();
    }

    /// <summary>Şu anki pencere boyutuna en yakın çözünürlüğün sırası.</summary>
    public static int ResolutionIndex(Resolution[] list)
    {
        int best = 0;
        long bestD = long.MaxValue;
        for (int i = 0; i < list.Length; i++)
        {
            long d = Mathf.Abs(list[i].width - Screen.width) + (long)Mathf.Abs(list[i].height - Screen.height);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    public static void SetResolution(Resolution r)
    {
        Screen.SetResolution(r.width, r.height, Screen.fullScreenMode);
        PlayerPrefs.SetInt(ResWKey, r.width);
        PlayerPrefs.SetInt(ResHKey, r.height);
    }

    public static void Save() => PlayerPrefs.Save();

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        mouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), MinSensitivity, MaxSensitivity);
        invertY = PlayerPrefs.GetInt(InvertKey, 0) == 1;
        quality = PlayerPrefs.GetInt(QualityKey, -1);
        vsync = PlayerPrefs.GetInt(VSyncKey, 1) == 1;
        fpsCap = Mathf.Max(0, PlayerPrefs.GetInt(FpsKey, 0));
        fov = PlayerPrefs.GetFloat(FovKey, -1f);
    }

    private static void ApplyFrameSettings()
    {
        QualitySettings.vSyncCount = vsync ? 1 : 0;
        Application.targetFrameRate = vsync || fpsCap <= 0 ? -1 : fpsCap;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyOnStart()
    {
        loaded = false;
        Load();
        AudioListener.volume = masterVolume;
        if (quality >= 0 && QualitySettings.names != null && quality < QualitySettings.names.Length) QualitySettings.SetQualityLevel(quality, true);
        ApplyFrameSettings();
        if (PlayerPrefs.HasKey(FullKey))
        {
            bool full = PlayerPrefs.GetInt(FullKey, 1) == 1;
            FullScreenMode want = full ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.fullScreenMode != want) Screen.fullScreenMode = want;
        }
        int w = PlayerPrefs.GetInt(ResWKey, 0), h = PlayerPrefs.GetInt(ResHKey, 0);
        if (w >= 800 && h >= 600 && (Screen.width != w || Screen.height != h) && !Application.isEditor)
            Screen.SetResolution(w, h, Screen.fullScreenMode);
    }
}
