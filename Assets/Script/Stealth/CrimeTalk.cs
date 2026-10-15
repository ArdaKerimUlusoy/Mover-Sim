using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sunucu: bir oyuncu sesli sohbette hırsızlık kelimesi söyledi (VoiceKeywordListener bulur).
///  - Yakındaki güvenlikler duyar. Duyma mesafesi SES YÜKSEKLİĞİNE göre: fısıltı 3–6 m (arkadaşının kulağına
///    söylersen güvenlik duymaz), normal ~15 m, bağırma ~22 m. Arada duvar varsa bunun %40'ı.
///  - Duyan güvenlik konuşanın yanındaki esere yürüyüp İNCELER. Yakında DEĞİŞTİRİLMİŞ (sahte) bir eser varsa önce ona gider
///    ve sahteyi fark etme şansı artar (fark ederse oraya varınca alarm). Yoksa konuşanın yakınındaki esere / yerine bakar.
///  - Konuşan oyuncu bir süre "duyuldu" sayılır: o sürede güvenlik onu görürse şüphelenir (PlayerStealth.ServerMarkOverheard).
///  - Ekranda bildirim yazısı çıkmaz. Kimse duymazsa hiçbir şey olmaz.
/// </summary>
public static class CrimeTalk
{
    public const float WhisperRange = 3f;    // fısıltı (arkadaşının kulağına)
    public const float NormalRange = 15f;    // normal konuşma: görüş mesafesindeki güvenlik duyar
    public const float ShoutRange = 22f;     // bağırma
    public const float WallFactor = 0.4f;    // duvar arkasından

    /// <summary>Ses yüksekliğine (0 fısıltı – 0.5 normal – 1 bağırma) göre güvenliğin duyabileceği mesafe.
    /// Fısıltı (0.25 altı) 3–6 m; normal konuşmadan itibaren 12 m'den başlar.</summary>
    public static float HearRangeFor(float loudness01)
    {
        float l = Mathf.Clamp01(loudness01);
        if (l < 0.25f) return Mathf.Lerp(WhisperRange, 6f, l / 0.25f);
        if (l < 0.5f) return Mathf.Lerp(12f, NormalRange, (l - 0.25f) / 0.25f);
        return Mathf.Lerp(NormalRange, ShoutRange, (l - 0.5f) / 0.5f);
    }
    /// <summary>Konuşanın bu kadar yakınındaki eser "konuşulan eser" sayılır (güvenlik ona gider); yoksa güvenlik
    /// doğrudan KONUŞANIN yanına gelir (oyuncu güvenliğin duyduğunu görsün).</summary>
    public const float ArtworkSearchRadius = 6f;
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

        bool log = CrimeWordSettings.Active.logRecognition;
        int heard = 0, guards = 0;
        foreach (GuardAI g in SecurityManager.Guards)
        {
            if (g == null || !g.Online) continue;
            guards++;
            Vector3 ear = g.transform.position + Vector3.up * 0.7f;
            float dist = Vector3.Distance(ear, mouth);
            if (dist > hearRange)
            {
                if (log) Debug.Log($"[CrimeTalk] Güvenlik '{g.name}' duymadı: {dist:0.0} m uzakta, ses ancak {hearRange:0.0} m gidiyor (ses yüksekliği {loudness / 255f:0.00})");
                continue;
            }
            if (dist > hearRange * WallFactor && Blocked(mouth, ear, speaker.transform, g.transform, out Collider wall))
            {
                if (log) Debug.Log($"[CrimeTalk] Güvenlik '{g.name}' duymadı: arada '{(wall != null ? wall.name : "?")}' var ({dist:0.0} m; duvar arkasından en fazla {hearRange * WallFactor:0.0} m)");
                continue;
            }

            if (g.ServerHearCrimeTalk(speaker, target))
            {
                heard++;
                if (log) Debug.Log($"[CrimeTalk] Güvenlik '{g.name}' DUYDU ({dist:0.0} m) → {(target != null ? "esere gidiyor: " + target.name : "konuşanın yerine bakıyor")}");
            }
        }
        if (log && guards == 0) Debug.Log("[CrimeTalk] Kelime sunucuya ulaştı ama aktif güvenlik yok (bu katta / görevde).");

        if (heard == 0) return;

        speaker.ServerMarkOverheard(OverheardSeconds);
        // Ekrana "Bir güvenlik ne dediğini duydu!" yazısı artık gösterilmez: oyuncu güvenliğin tepkisinden anlar
        // (sesli sohbetin mikrofon kutusu algılanınca zaten kırmızı yanar).
    }

    /// <summary>
    /// Arada ses kesen bir şey (duvar, kapı) var mı? Sadece BOY HİZASINI aşan engeller sayılır: kaide, vitrin, saksı,
    /// bank, masa gibi alçak eşyalar sesi kesmez (eskiden ışın bel/omuz hizasından geçtiği için bunlar "duvar" sayılıyor,
    /// aynı odadaki güvenlik bile duymuyordu).
    /// </summary>
    private static bool Blocked(Vector3 from, Vector3 to, Transform speaker, Transform listener, out Collider blocker)
    {
        blocker = null;
        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len < 0.01f) return false;
        float lineTop = Mathf.Max(from.y, to.y);
        RaycastHit[] hits = Physics.RaycastAll(from, dir / len, len, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(speaker) || t.IsChildOf(listener)) continue;
            if (h.collider.GetComponentInParent<PlayerStealth>() != null) continue; // başka oyuncular sesi kesmez
            if (h.collider.GetComponentInParent<GuardAI>() != null) continue;
            Bounds bb = h.collider.bounds;
            if (bb.max.y < lineTop + 0.35f && bb.size.y < 1.8f) continue; // alçak eşya: üstünden ses geçer
            blocker = h.collider;
            return true;
        }
        return false;
    }
}
