using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// KÖTÜ SAHTE FARK EDİLİR: benzerliği eşiğin (BadFakeThreshold) altındaki bir sahte kaideye konunca, birkaç saniye sonra (4–8 sn) onu gören
/// ilk ziyaretçi ya da güvenlik "bu sahte!" diye bağırır → ALARM. Kimse görmüyorsa görene kadar bekler.
/// Böylece kaba sahte koyup çıkmak kaçışı zorlaştırır. (İyi sahteler için güvenliklerin eski "yakından bakma" kontrolü sürer.)
/// Sunucuda çalışır, sahneye bir şey eklemeye gerek yok.
/// </summary>
public class FakeSpotter : MonoBehaviour
{
    public const byte NoticeBelow = ArtworkDisplay.BadFakeThreshold; // bu benzerliğin altında fark edilme şansı doğar

    /// <summary>Benzerliğe göre fark edilme olasılığı: eşikte %25, MinSwapScore'a doğru yükselip %100 olur (kesin alarm değil).</summary>
    public static float NoticeChance(byte similarity)
    {
        if (similarity >= NoticeBelow) return 0f;
        float t = Mathf.Clamp01((NoticeBelow - similarity) / (float)(NoticeBelow - ArtworkDisplay.MinSwapScore));
        return 0.25f + 0.75f * t;
    }
    private const float MinDelay = 4f, MaxDelay = 8f;
    private const float SightRange = 9f;

    private readonly Dictionary<ArtworkDisplay, float> pending = new Dictionary<ArtworkDisplay, float>(); // eser → en erken fark edilme anı
    private readonly HashSet<ArtworkDisplay> done = new HashSet<ArtworkDisplay>();
    private readonly List<ArtworkDisplay> tmp = new List<ArtworkDisplay>();
    private float nextCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("FakeSpotter");
        DontDestroyOnLoad(go);
        go.AddComponent<FakeSpotter>();
    }

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer) { pending.Clear(); done.Clear(); return; }
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;

        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null) continue;
            bool bad = d.IsSwapped && d.LeftSimilarity < NoticeBelow; // NoSimilarity (255) = sayılmaz
            if (!bad) { pending.Remove(d); done.Remove(d); continue; }
            if (done.Contains(d) || pending.ContainsKey(d)) continue;
            if (Random.value >= NoticeChance(d.LeftSimilarity)) { done.Add(d); continue; } // bu sefer kimse şüphelenmedi
            pending[d] = Time.time + Random.Range(MinDelay, MaxDelay);
        }

        SecurityManager sm = SecurityManager.Instance;
        if (sm == null || pending.Count == 0) return;
        tmp.Clear();
        foreach (KeyValuePair<ArtworkDisplay, float> kv in pending) if (Time.time >= kv.Value) tmp.Add(kv.Key);
        foreach (ArtworkDisplay d in tmp)
        {
            if (d == null || !d.IsSwapped) { pending.Remove(d); continue; }
            if (!SomeoneSees(d)) continue; // görene kadar bekle
            pending.Remove(d);
            done.Add(d);
            if (!sm.AlarmActive) sm.ServerTriggerAlarm(d.transform.position, "alarm.fakespotted", null);
        }
    }

    private static bool SomeoneSees(ArtworkDisplay d)
    {
        Vector3 p = d.transform.position + Vector3.up * (d.WallMounted ? 0f : 0.5f);
        VisitorManager vm = VisitorManager.Instance;
        if (vm != null && vm.ServerAnySees(p, SightRange, d.transform)) return true;
        foreach (GuardAI g in SecurityManager.Guards)
        {
            if (g == null || !g.Online) continue;
            if (Vision.CanSeePoint(g.transform.position + Vector3.up * 0.7f, g.transform.forward, SightRange, 110f, p, d.transform)) return true;
        }
        return false;
    }
}
