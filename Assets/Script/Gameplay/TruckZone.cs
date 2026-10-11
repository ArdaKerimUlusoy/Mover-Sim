using UnityEngine;

/// <summary>
/// Kamyonun içi (GDD Bölüm 5: "ekip kamyona dönerse gece tamamlanır").
/// Görev, kota dolduktan sonra TÜM oyuncular bu alandayken tamamlanır (MuseumSession).
/// Bir BoxCollider'ın sınırlarını kullanır. Sahnede yoksa "herkes kamyonda" sayılır.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class TruckZone : MonoBehaviour
{
    public static TruckZone Instance { get; private set; }

    private BoxCollider box;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    private void OnEnable() => Instance = this;
    private void OnDisable() { if (Instance == this) Instance = null; }

    public bool Contains(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos) - box.center;
        Vector3 half = box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y + 1f && Mathf.Abs(local.z) <= half.z;
    }

    /// <summary>Kamyondaki oyuncu sayısı ve toplam oyuncu sayısı (oyuncu konumları zaten senkron: her bilgisayar aynı sonucu bulur).</summary>
    public static void Count(out int inside, out int total)
    {
        inside = 0;
        total = 0;
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p == null || !p.IsSpawned) continue;
            total++;
            if (Instance == null || Instance.Contains(p.transform.position)) inside++;
        }
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
