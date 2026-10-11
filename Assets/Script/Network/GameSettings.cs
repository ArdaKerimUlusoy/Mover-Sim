using UnityEngine;

/// <summary>
/// Oyuncunun kendi bilgisayarında saklanan ayarlar (ses, fare hassasiyeti).
/// Esc menüsündeki kaydırıcılar bunları değiştirir.
/// </summary>
public static class GameSettings
{
    private const string VolumeKey = "MoverSim_Volume";
    private const string SensitivityKey = "MoverSim_Sensitivity";

    public const float MinSensitivity = 0.2f;
    public const float MaxSensitivity = 3f;

    private static bool loaded;
    private static float masterVolume = 1f;
    private static float mouseSensitivity = 1f;

    /// <summary>Genel ses seviyesi (0–1).</summary>
    public static float MasterVolume
    {
        get { Load(); return masterVolume; }
        set
        {
            Load();
            masterVolume = Mathf.Clamp01(value);
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
            mouseSensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            PlayerPrefs.SetFloat(SensitivityKey, mouseSensitivity);
        }
    }

    public static void Save() => PlayerPrefs.Save();

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        mouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), MinSensitivity, MaxSensitivity);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyOnStart()
    {
        loaded = false;
        Load();
        AudioListener.volume = masterVolume;
    }
}
