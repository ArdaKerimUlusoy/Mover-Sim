using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// İSTEMCİDE "HAYALET" EŞYALARI GİZLER. Müzeye sahnede elle konmuş eski masa ganimetleri (in-scene NetworkObject) sunucuda
/// ilk görevde kaldırılıp masalara yenileri konuyor (MuseumSession.ServerRefreshLoot). Host'ta eskiler kapanıyor ama arkadaşın
/// bilgisayarında ağa hiç bağlanmamış (spawn edilmemiş) eski kopyalar sahnede kalıyor ve masalar yeni yerlerine taşındığı için
/// HAVADA asılı görünüyordu. Bağlı istemcide spawn edilmemiş sahne eşyaları gizlenir; oturumdan çıkınca geri açılır.
/// Yalnızca istemcide, saniyede bir, birkaç eşya üzerinde çalışır (ucuz). Sahneye bir şey eklemek gerekmez.
/// </summary>
public class WorldItemGhosts : MonoBehaviour
{
    private static WorldItemGhosts instance;
    private static readonly List<GameObject> hidden = new List<GameObject>();
    private float nextSweep;
    private float connectedSince = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("WorldItemGhosts");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<WorldItemGhosts>();
    }

    /// <summary>İstemci: ağda kaldırılan sahne eşyası (WorldItem.OnNetworkDespawn) gizlenir ve oturum bitince geri açılır.</summary>
    public static void ClientHide(GameObject go)
    {
        if (go == null || !go.activeSelf) return;
        go.SetActive(false);
        if (!hidden.Contains(go)) hidden.Add(go);
    }

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool client = nm != null && nm.IsListening && nm.IsConnectedClient && !nm.IsServer && !nm.ShutdownInProgress;
        if (!client)
        {
            connectedSince = -1f;
            if (hidden.Count > 0) Restore();
            return;
        }
        if (connectedSince < 0f) connectedSince = Time.unscaledTime;
        // Sahne eşitlemesi bitsin (kendi oyuncumuz doğsun) ve biraz beklensin; sonra saniyede bir tara
        if (!GameFlow.LocalSpawned || Time.unscaledTime - connectedSince < 2f || Time.unscaledTime < nextSweep) return;
        nextSweep = Time.unscaledTime + 1f;
        foreach (WorldItem wi in FindObjectsByType<WorldItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (wi == null || wi.IsSpawned) continue;
            GameObject go = wi.gameObject;
            // Yalnızca sahneye elle konmuş kopyalar (çalışma anında üretilen önizleme / ikon kopyaları "(Clone)" adlıdır)
            if (go.name.EndsWith("(Clone)") || go.scene.name == "DontDestroyOnLoad") continue;
            if (wi.GetComponent<NetworkObject>() == null) continue;
            ClientHide(go);
        }
    }

    private static void Restore()
    {
        for (int i = 0; i < hidden.Count; i++) if (hidden[i] != null) hidden[i].SetActive(true);
        hidden.Clear();
    }
}
