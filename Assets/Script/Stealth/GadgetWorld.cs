using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Hırsız aletlerinin dünyası (saf MonoBehaviour, kendiliğinden kurulur; ağ nesnesi eklemeye gerek yok).
///  - SUNUCU: yerdeki muz kabuklarının listesi; üstüne basan güvenliği / ziyaretçiyi kaydırır, kabuk yok olur.
///  - HERKES: bozuk para uçuşu ve muz kabuğu görselleri (RPC'ler PlayerStealth.Gadgets.cs içinde).
/// </summary>
public class GadgetWorld : MonoBehaviour
{
    public const float SlipSeconds = 2.5f;
    private const float SlipRadius = 0.8f;
    private const float BananaLifetime = 120f;

    private static GadgetWorld inst;

    private sealed class Banana
    {
        public int id;
        public Vector3 pos;
        public float expire;
        public PlayerStealth owner;
    }

    private static readonly List<Banana> serverBananas = new List<Banana>();
    private static int nextId = 1;
    private float nextCheck;

    // İstemci görselleri
    private readonly Dictionary<int, GameObject> shown = new Dictionary<int, GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        inst = null;
        serverBananas.Clear();
        nextId = 1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() => Ensure();

    private static GadgetWorld Ensure()
    {
        if (inst != null) return inst;
        GameObject go = new GameObject("GadgetWorld");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<GadgetWorld>();
        return inst;
    }

    // ---------------- Sunucu ----------------

    /// <summary>Sunucu: yere muz kabuğu bırakır, herkese görseli yayınlar.</summary>
    public static void ServerAddBanana(PlayerStealth owner, Vector3 pos)
    {
        Ensure();
        Banana b = new Banana { id = nextId++, pos = pos, expire = Time.time + BananaLifetime, owner = owner };
        serverBananas.Add(b);
        if (owner != null) owner.ServerBananaVisual(b.id, pos, 1);
    }

    /// <summary>Sunucu: bütün kabukları kaldırır (görev sıfırlanırken).</summary>
    public static void ServerClearAll()
    {
        if (serverBananas.Count == 0) return;
        PlayerStealth any = null;
        foreach (Banana b in serverBananas) if (b.owner != null && b.owner.IsSpawned) { any = b.owner; break; }
        if (any == null) foreach (PlayerStealth p in PlayerStealth.All) if (p != null && p.IsSpawned) { any = p; break; }
        serverBananas.Clear();
        if (any != null) any.ServerBananaVisual(0, Vector3.zero, 2);
    }

    private static PlayerStealth Broadcaster(Banana b)
    {
        if (b.owner != null && b.owner.IsSpawned) return b.owner;
        foreach (PlayerStealth p in PlayerStealth.All) if (p != null && p.IsSpawned) return p;
        return null;
    }

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !nm.IsListening || serverBananas.Count == 0) return;
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.1f;

        for (int i = serverBananas.Count - 1; i >= 0; i--)
        {
            Banana b = serverBananas[i];
            bool remove = Time.time >= b.expire;

            if (!remove)
            {
                foreach (GuardAI g in SecurityManager.Guards)
                {
                    if (g == null || !g.Online) continue;
                    Vector3 d = g.transform.position - b.pos;
                    if (Mathf.Abs(d.y) > 1.5f) continue;
                    d.y = 0f;
                    if (d.magnitude > SlipRadius) continue;
                    if (g.ServerSlip(SlipSeconds)) { remove = true; break; }
                }
            }
            if (!remove && VisitorManager.Instance != null && VisitorManager.Instance.ServerSlipNear(b.pos, SlipRadius, SlipSeconds) > 0)
            {
                PlayerStealth p = Broadcaster(b);
                if (p != null && p.TryGetComponent(out PlayerInventory inv)) inv.ServerPlaySound(SoundId.Slip, b.pos);
                remove = true;
            }

            if (!remove) continue;
            serverBananas.RemoveAt(i);
            PlayerStealth caster = Broadcaster(b);
            if (caster != null) caster.ServerBananaVisual(b.id, b.pos, 0);
        }
    }

    // ---------------- İstemci görselleri ----------------

    /// <summary>Herkes: mode 1 = kabuk ekle, 0 = kaldır, 2 = hepsini kaldır.</summary>
    public static void ClientBanana(int id, Vector3 pos, int mode)
    {
        GadgetWorld w = Ensure();
        if (mode == 2)
        {
            foreach (GameObject go in w.shown.Values) if (go != null) Destroy(go);
            w.shown.Clear();
            return;
        }
        if (mode == 0)
        {
            if (w.shown.TryGetValue(id, out GameObject old)) { if (old != null) Destroy(old); w.shown.Remove(id); }
            return;
        }
        if (w.shown.ContainsKey(id)) return;
        w.shown[id] = BuildBanana(pos);
    }

    private static GameObject BuildBanana(Vector3 pos)
    {
        GameObject root = new GameObject("BananaPeel");
        root.transform.position = pos + Vector3.up * 0.04f;
        root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        Material yellow = ArtworkLook.MakeLit(new Color(1f, 0.88f, 0.15f));
        Material brown = ArtworkLook.MakeLit(new Color(0.35f, 0.24f, 0.08f));

        // Yassı yay: iki ucu hafif yukarı kıvrık iki kapsül + kahverengi sap
        for (int i = -1; i <= 1; i += 2)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            part.name = "Peel";
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = new Vector3(i * 0.11f, 0.02f, 0f);
            part.transform.localRotation = Quaternion.Euler(0f, 0f, 90f + i * 14f);
            part.transform.localScale = new Vector3(0.09f, 0.13f, 0.09f);
            part.GetComponent<Renderer>().sharedMaterial = yellow;
        }
        GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stem.name = "Stem";
        Destroy(stem.GetComponent<Collider>());
        stem.transform.SetParent(root.transform, false);
        stem.transform.localPosition = new Vector3(0.25f, 0.05f, 0f);
        stem.transform.localScale = new Vector3(0.05f, 0.04f, 0.04f);
        stem.GetComponent<Renderer>().sharedMaterial = brown;
        return root;
    }

    /// <summary>Herkes: bozuk para havada uçar, düşünce yumuşak ses.</summary>
    public static void ClientCoin(Vector3 from, Vector3 to, float time)
    {
        GadgetWorld w = Ensure();
        w.StartCoroutine(w.CoinFlight(from, to, Mathf.Max(0.15f, time)));
    }

    private IEnumerator CoinFlight(Vector3 from, Vector3 to, float time)
    {
        GameObject coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        coin.name = "Coin";
        Destroy(coin.GetComponent<Collider>());
        coin.transform.localScale = new Vector3(0.07f, 0.008f, 0.07f);
        coin.GetComponent<Renderer>().sharedMaterial = ArtworkLook.MakeLit(new Color(1f, 0.8f, 0.2f));

        float t = 0f;
        while (t < time && coin != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / time);
            Vector3 p = Vector3.Lerp(from, to, k);
            p.y += Mathf.Sin(k * Mathf.PI) * 1.2f; // yay
            coin.transform.position = p;
            coin.transform.Rotate(720f * Time.deltaTime, 0f, 360f * Time.deltaTime);
            yield return null;
        }
        if (coin != null)
        {
            GameAudio.PlayAt(SoundId.CoinLandSoft, to, 0.9f);
            coin.transform.position = to + Vector3.up * 0.01f;
            coin.transform.rotation = Quaternion.identity;
            Destroy(coin, 4f);
        }
    }
}
