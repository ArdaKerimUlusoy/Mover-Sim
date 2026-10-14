using UnityEngine;

/// <summary>
/// Kamyonun içi (GDD Bölüm 5: "ekip kamyona dönerse gece tamamlanır").
/// Kamyonu kimse sürmez: içindeki biri arka kapıyı kapatınca kamyon gider (MuseumSession / TruckEscapeUI).
/// Kapı, HAPİSTE OLMAYAN ve bağlı bütün oyuncular bu alandayken kapanabilir (hapistekiler beklenmez, dolaptakiler dışarıda sayılır).
/// Bir BoxCollider'ın sınırlarını kullanır. Sahnede yoksa "herkes kamyonda" sayılır.
/// Arka kapının görseli (TruckDoor) ve çöp kutusu (TrashBin) çalışma anında buradan kurulur: sahneye bir şey eklemek gerekmez.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class TruckZone : MonoBehaviour
{
    public static TruckZone Instance { get; private set; }

    /// <summary>Kamyondaki durum özeti (oyuncu konumları ve hapis durumu her bilgisayarda senkron: herkes aynı sonucu bulur).</summary>
    public struct Status
    {
        public int inside;        // hapiste olmayan ve kamyonda olanlar
        public int total;         // hapiste olmayan bağlı oyuncular (beklenmesi gerekenler)
        public int jailed;        // hapistekiler (beklenmez)
        public int outsideCount;  // beklenmesi gereken ama dışarıda olanlar
        public string firstOutside; // bunlardan birinin adı (uyarı için)

        /// <summary>Kapı kapanabilir mi? En az bir kişi var ve hapiste olmayan herkes içeride.</summary>
        public bool EveryoneIn => total > 0 && outsideCount == 0;
    }

    private BoxCollider box;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        TrashBin.CreateFor(this); // kötü sahteleri atmak için çöp kutusu (her bilgisayarda aynı yerde)
        TruckDoor.CreateFor(this); // arka kapı (iki kanat), yalnızca görsel
    }

    private void OnEnable() => Instance = this;
    private void OnDisable() { if (Instance == this) Instance = null; }

    public bool Contains(Vector3 worldPos)
    {
        if (box == null) box = GetComponent<BoxCollider>();
        Vector3 local = transform.InverseTransformPoint(worldPos) - box.center;
        Vector3 half = box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y + 1f && Mathf.Abs(local.z) <= half.z;
    }

    /// <summary>Bu oyuncu kamyonun içinde mi? Dolapta saklanan kamyon dışında sayılır.</summary>
    public static bool IsInside(PlayerNetworkSetup p)
    {
        if (p == null) return false;
        PlayerStealth st = p.GetComponent<PlayerStealth>();
        if (st != null && st.IsHidden) return false;
        return Instance == null || Instance.Contains(p.transform.position);
    }

    private static Status cached;
    private static int cachedFrame = -1;

    /// <summary>Karede bir kez hesaplanır (HUD, K tuşu ve sunucu aynı karede birkaç kez çağırabilir).</summary>
    public static Status GetStatus()
    {
        if (cachedFrame == Time.frameCount) return cached;
        cachedFrame = Time.frameCount;
        cached = Compute();
        return cached;
    }

    private static Status Compute()
    {
        Status s = default;
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p == null || !p.IsSpawned) continue;
            PlayerStealth st = p.GetComponent<PlayerStealth>();
            if (st != null && st.IsJailed) { s.jailed++; continue; } // hapistekiler beklenmez
            s.total++;
            if (IsInside(p)) s.inside++;
            else
            {
                s.outsideCount++;
                if (s.firstOutside == null) s.firstOutside = p.PlayerName;
            }
        }
        return s;
    }

    private void OnDrawGizmos()
    {
        BoxCollider b = GetComponent<BoxCollider>();
        if (b == null) return;
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.2f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(b.center, b.size);
    }
}
