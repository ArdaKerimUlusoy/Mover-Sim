using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sunucu: bir oyuncu sesli sohbette hırsızlık kelimesi söyledi (VoiceKeywordListener bulur).
///  - Yakındaki güvenlikler duyar. Duyma mesafesi SES YÜKSEKLİĞİNE göre: fısıltı ~2.5 m (arkadaşının kulağına
///    söylersen güvenlik duymaz), normal ~6 m, bağırma ~10 m. Arada duvar varsa bunun %40'ı.
///  - Duyan güvenlik konuşanın yanındaki esere yürüyüp İNCELER. Yakında DEĞİŞTİRİLMİŞ (sahte) bir eser varsa önce ona gider
///    ve sahteyi fark etme şansı artar (fark ederse oraya varınca alarm). Yoksa konuşanın yakınındaki esere / yerine bakar.
///  - Konuşan oyuncu bir süre "duyuldu" sayılır: o sürede güvenlik onu görürse şüphelenir (PlayerStealth.ServerMarkOverheard).
///  - Konuşan kişiye "Bir güvenlik ne dediğini duydu!" bildirimi gider. Kimse duymazsa hiçbir şey olmaz.
/// </summary>
public static class CrimeTalk
{
    public const float WhisperRange = 2.5f;  // fısıltı
    public const float ShoutRange = 10f;     // bağırma
    public const float WallFactor = 0.4f;    // duvar arkasından

    /// <summary>Ses yüksekliğine (0 fısıltı – 1 bağırma) göre güvenliğin duyabileceği mesafe.</summary>
    public static float HearRangeFor(float loudness01) => Mathf.Lerp(WhisperRange, ShoutRange, Mathf.Clamp01(loudness01));
    public const float ArtworkSearchRadius = 12f;
    public const float OverheardSeconds = 25f;
    /// <summary>Aynı oyuncudan iki "duyuldu" arasında en az (sn): CrimeWordSettings.serverMinInterval (eskiden sabit 4 sn).</summary>
    public static float ServerCooldown => CrimeWordSettings.Active.serverMinInterval;

    private static readonly Dictionary<ulong, float> lastHeard = new Dictionary<ulong, float>();

    public const int MaxWordLength = 24;

    public static void ServerHeard(ulong clientId, string word, byte loudness = 128)
    {
        float hearRange = HearRangeFor(loudness / 255f);
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null) return;

        // Sunucu doğrulaması: uzak istemci son birkaç sn içinde GERÇEKTEN ses göndermiş olmalı (değiştirilmiş istemcinin
        // sessizce "çal" bildirimi yollamasına karşı). Host'un kendi bildirimi kendi makinesinde üretilir: muaf.
        if (clientId != nm.LocalClientId)
        {
            if (!VoiceChat.ServerLastVoice.TryGetValue(clientId, out float lastVoice) ||
                Time.unscaledTime - lastVoice > CrimeWordSettings.Active.serverVoiceRecentSeconds) return;
        }
        if (string.IsNullOrEmpty(word)) word = "?";
        else if (word.Length > MaxWordLength) word = word.Substring(0, MaxWordLength);

        if (lastHeard.TryGetValue(clientId, out float t) && Time.time - t < ServerCooldown) return;
        lastHeard[clientId] = Time.time;

        PlayerStealth speaker = client.PlayerObject.GetComponent<PlayerStealth>();
        if (speaker == null || !speaker.IsSpawned || speaker.IsJailed) return;

        Vector3 mouth = speaker.transform.position + Vector3.up * 0.6f;

        // Konuşanın yakınındaki eser: değiştirilmiş (sahte) olan öncelikli, yoksa en yakını
        ArtworkDisplay target = null;
        float best = float.MaxValue;
        bool bestSwapped = false;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || !d.IsSpawned || !d.IsActiveForMission) continue;
            float dist = Vector3.Distance(d.transform.position, speaker.transform.position);
            if (dist > ArtworkSearchRadius) continue;
            bool swapped = d.IsSwapped;
            if ((swapped && !bestSwapped) || (swapped == bestSwapped && dist < best))
            {
                target = d;
                best = dist;
                bestSwapped = swapped;
            }
        }

        int heard = 0;
        foreach (GuardAI g in SecurityManager.Guards)
        {
            if (g == null || !g.Online) continue;
            Vector3 ear = g.transform.position + Vector3.up * 0.7f;
            float dist = Vector3.Distance(ear, mouth);
            if (dist > hearRange) continue;
            if (dist > hearRange * WallFactor && Blocked(mouth, ear, speaker.transform, g.transform)) continue;

            if (g.ServerHearCrimeTalk(speaker, target)) heard++;
        }

        if (heard == 0) return;

        speaker.ServerMarkOverheard(OverheardSeconds);
        PlayerInventory inv = speaker.GetComponent<PlayerInventory>();
        if (inv != null) inv.ServerNotify("crimetalk.heard", word);
    }

    /// <summary>Arada ses kesen bir şey (duvar, kapı) var mı?</summary>
    private static bool Blocked(Vector3 from, Vector3 to, Transform speaker, Transform listener)
    {
        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len < 0.01f) return false;
        RaycastHit[] hits = Physics.RaycastAll(from, dir / len, len, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(speaker) || t.IsChildOf(listener)) continue;
            if (h.collider.GetComponentInParent<PlayerStealth>() != null) continue; // başka oyuncular sesi kesmez
            if (h.collider.GetComponentInParent<GuardAI>() != null) continue;
            return true;
        }
        return false;
    }
}
