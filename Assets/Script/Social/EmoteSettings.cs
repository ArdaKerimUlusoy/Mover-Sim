using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Emote çarkı ayarları (isteğe bağlı varlık). Oluşturmak için: Project → Assets/Resources klasöründe sağ tık →
/// Create → Sahte Şaheser → Emote Settings, adı "EmoteSettings" kalsın. Varlık yoksa buradaki varsayılanlar kullanılır.
/// </summary>
[CreateAssetMenu(fileName = "EmoteSettings", menuName = "Sahte Şaheser/Emote Settings")]
public class EmoteSettings : ScriptableObject
{
    [Header("Tuş")]
    [Tooltip("Basılı tutunca çark açılır, bırakınca seçili emote oynar.")]
    public Key wheelKey = Key.T;

    [Header("Çark açıkken")]
    [Tooltip("Açık: çark açıkken fare kamerayı döndürmez (fare yalnızca seçim yapar).")]
    public bool freezeLookWhileOpen = true;
    [Tooltip("Açık: çark açıkken WASD ile yürümeye devam edilir.")]
    public bool allowMoveWhileOpen = true;
    [Tooltip("Ekranda çarkın merkezi (0-1, sol alt 0,0). 0.5, 0.5 = tam orta.")]
    public Vector2 wheelCenter = new Vector2(0.5f, 0.5f);
    [Range(0.6f, 1.5f)] public float wheelScale = 1f;

    [Header("Emote sırasında")]
    [Tooltip("Kapalı: emote bitene kadar karakter yürüyemez.")]
    public bool allowMoveDuringEmote = true;
    [Tooltip("İki emote arası en az bekleme (sn). Sunucu da denetler (spam).")]
    [Min(0f)] public float cooldown = 1.5f;

    [Header("Ses (isteğe bağlı)")]
    [Tooltip("Emote sesleri, sıra EmoteId'ye göre (0 OK, 1 Orta parmak, 2 Başparmak yukarı, 3 Başparmak aşağı, 4 El sallama, 5 Gösterip gülme). " +
             "Boş olanlar için Resources/Emotes/<key> aranır; o da yoksa ses çalmaz.")]
    public AudioClip[] sounds = new AudioClip[0];
    [Range(0f, 1f)] public float soundVolume = 0.8f;

    private static EmoteSettings current;

    public static EmoteSettings Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<EmoteSettings>("EmoteSettings");
            if (current == null) { current = CreateInstance<EmoteSettings>(); current.hideFlags = HideFlags.DontSave; }
            return current;
        }
    }

    public AudioClip SoundFor(EmoteDef d)
    {
        if (d == null) return null;
        int i = (int)d.Id;
        if (sounds != null && i < sounds.Length && sounds[i] != null) return sounds[i];
        return Resources.Load<AudioClip>("Emotes/" + d.Key);
    }
}
