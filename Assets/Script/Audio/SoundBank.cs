using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Oyundaki ses türleri. Ağdan numara olarak gönderilir; sıralamayı değiştirmeyin, sona ekleyin.</summary>
public enum SoundId : byte
{
    Footstep = 0,
    Pickup = 1,
    Drop = 2,
    BagOpen = 3,
    BagClose = 4,
    CoinThrow = 5,
    CoinLand = 6,
    ItemThud = 7,
    HoldComplete = 8,
    Steal = 9,
    UiClick = 10,
    DoorOpen = 11,
    DoorClose = 12,
    Sell = 13,
    Purchase = 14,
    Swap = 15,
    Alarm = 16,
    Ping = 17,
    Caught = 18,
    GuardAlert = 19,
    GuardHuh = 20,
    TruckEngine = 21,   // kamyon kalkış motoru
    PoliceSiren = 22,   // polis sireni (yumuşak, yükselip alçalan)
    TruckBrake = 23,    // sert fren / lastik sesi
}

/// <summary>
/// Gerçek ses dosyalarını bağlamak için (isteğe bağlı).
/// Oluşturma: Project → sağ tık → Create → Sahte Şaheser → Sound Bank, adı "SoundBank" olsun
/// ve Assets/Resources klasörüne koyun. Listede olmayan sesler için kodla üretilen geçici sesler çalar.
/// </summary>
[CreateAssetMenu(fileName = "SoundBank", menuName = "Sahte Şaheser/Sound Bank")]
public class SoundBank : ScriptableObject
{
    public const string ResourcePath = "SoundBank";

    [Serializable]
    public class Entry
    {
        public SoundId id;
        [Tooltip("Birden fazla verirseniz her seferinde rastgele biri çalar (ayak sesi gibi).")]
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float volume = 1f;
    }

    public List<Entry> entries = new List<Entry>();
}
