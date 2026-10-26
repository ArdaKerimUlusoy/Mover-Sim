using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Odadaki güvenlik kamerası (her odada en az bir tane). MuseumLayout her haritada müze düzenine göre
/// çalışma anında oluşturur; yeri haritadan haritaya değişir.
/// Ağ nesnesi DEĞİLDİR: herkes aynı tohumdan aynı yere kurar, dönüş açısı sunucu saatinden hesaplanır
/// (herkeste aynı), oyuncuları görme / şüphe / ihbar ise sadece sunucuda çalışır.
/// Görevi SecurityCamera ile aynıdır (sahnedeki eski sabit kameralar artık kullanılmıyor).
/// </summary>
public class RoomCamera : MonoBehaviour
{
    public int requiredLevel = 1;
    public float sweepAngle = 80f;
    public float sweepSpeed = 0.45f;
    public float viewRange = 10f;
    public float viewAngle = 50f;
    public float suspicionRate = 0.7f;
    public float reportCooldown = 4f;
    public float phaseOffset;

    private Transform head;
    private Transform lensDot;
    private VisionConeVisual cone;
    private float baseYaw;
    private float nextReport;
    private bool shownOnline = true;
    private Material bodyMat, lensMat;

    /// <summary>Sahnedeki bütün oda kameraları (hapis kurtarma gibi "biri gördü mü?" kontrolleri için).</summary>
    public static readonly System.Collections.Generic.List<RoomCamera> All = new System.Collections.Generic.List<RoomCamera>();
    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }

    /// <summary>Adıyla oda kamerası (boya spreyi RPC'si adı taşır).</summary>
    public static RoomCamera Find(string cameraName)
    {
        foreach (RoomCamera c in All) if (c != null && c.name == cameraName) return c;
        return null;
    }

    /// <summary>Lensine boya sıkıldı mı (kör)?</summary>
    public bool Blinded => SecurityManager.Instance != null && SecurityManager.Instance.IsCameraBlinded(name);

    /// <summary>Lensin dünya konumu (boya spreyi buraya nişan alır).</summary>
    public Vector3 LensPosition => lensDot != null ? lensDot.position : (head != null ? head.position : transform.position);

    private Transform paintBlob;
    private Material paintMat;
    private void OnDisable() => All.Remove(this);

    /// <summary>Sunucu: bu kamera oyuncuyu şu an görüyor mu? (Kapalıysa / bu görevde yoksa görmez.)</summary>
    public bool ServerCanSee(PlayerStealth p)
    {
        if (p == null || !p.IsSpawned || !MuseumSession.IsLevelActive(requiredLevel)) return false;
        if (SecurityManager.Instance != null && SecurityManager.Instance.CamerasDisabled) return false;
        if (Blinded || SprayPaint.IsBlinded(this)) return false; // lensine boya topu isabet etti
        return Vision.CanSee(transform.position, Forward, viewRange, viewAngle, p);
    }

    /// <summary>Kamerayı kurar: duvara takılı kol + dönen kafa + yerdeki görüş konisi.</summary>
    public static RoomCamera Create(Transform parent, string name, Vector3 localPos, float yaw, int level, float range, Color bodyColor)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        RoomCamera rc = go.AddComponent<RoomCamera>();
        rc.requiredLevel = level;
        rc.viewRange = range;
        rc.baseYaw = go.transform.eulerAngles.y;
        rc.phaseOffset = (ArtworkLook.Hash(name) & 1023) / 1023f * Mathf.PI * 2f;
        rc.Build(bodyColor);
        return rc;
    }

    private void Build(Color bodyColor)
    {
        bodyMat = ArtworkLook.MakeLit(bodyColor);
        lensMat = ArtworkLook.MakeLit(new Color(0.9f, 0.15f, 0.12f));

        // Duvara takılı kol
        Piece(transform, "Mount", PrimitiveType.Cube, new Vector3(0f, 0.12f, -0.15f), new Vector3(0.12f, 0.3f, 0.12f), bodyMat);

        head = new GameObject("Head").transform;
        head.SetParent(transform, false);
        Piece(head, "Body", PrimitiveType.Cube, new Vector3(0f, 0f, 0.05f), new Vector3(0.24f, 0.2f, 0.46f), bodyMat);
        Piece(head, "Hood", PrimitiveType.Cube, new Vector3(0f, 0.12f, 0.1f), new Vector3(0.28f, 0.04f, 0.5f), bodyMat);
        lensDot = Piece(head, "Lens", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.29f), new Vector3(0.13f, 0.02f, 0.13f), lensMat);
        lensDot.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // Lense sıkılan boya (kör edilince görünür)
        paintMat = ArtworkLook.MakeLit(new Color(0.15f, 0.75f, 0.25f));
        paintBlob = Piece(head, "PaintBlob", PrimitiveType.Sphere, new Vector3(0f, 0f, 0.3f), new Vector3(0.26f, 0.24f, 0.12f), paintMat);
        paintBlob.gameObject.SetActive(false);

        GameObject coneGo = new GameObject("VisionCone");
        coneGo.transform.SetParent(transform, false);
        coneGo.AddComponent<MeshFilter>();
        coneGo.AddComponent<MeshRenderer>();
        cone = coneGo.AddComponent<VisionConeVisual>();
    }

    private static Transform Piece(Transform parent, string n, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = n;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        return go.transform;
    }

    private Vector3 Forward
    {
        get
        {
            NetworkManager nm = NetworkManager.Singleton;
            float t = nm != null && nm.IsListening ? nm.ServerTime.TimeAsFloat : Time.time;
            float yaw = baseYaw + Mathf.Sin(t * sweepSpeed + phaseOffset) * sweepAngle * 0.5f;
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }
    }

    private void Update()
    {
        bool online = MuseumSession.IsLevelActive(requiredLevel);
        if (online != shownOnline)
        {
            shownOnline = online;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = online;
        }
        if (!online) return;

        Vector3 fwd = Forward;
        if (head != null) head.rotation = Quaternion.LookRotation(fwd + Vector3.down * 0.45f);

        bool alerted = false;
        bool blinded = Blinded;
        if (paintBlob != null && paintBlob.gameObject.activeSelf != blinded) paintBlob.gameObject.SetActive(blinded);
        bool off = blinded || SprayPaint.IsBlinded(this) ||                                     // lensi boyalı (sprey / fırça)
                   (SecurityManager.Instance != null && SecurityManager.Instance.CamerasDisabled); // kapatıldı
        NetworkManager nm = NetworkManager.Singleton;
        if (!off && nm != null && nm.IsListening && nm.IsServer)
        {
            foreach (PlayerStealth p in PlayerStealth.All)
            {
                if (p == null || !p.IsSpawned) continue;
                if (!Vision.CanSee(transform.position, fwd, viewRange, viewAngle, p)) continue;

                p.ServerReportSeen(suspicionRate, transform.position);
                if (p.SuspiciousLevel > 0f)
                {
                    alerted = true;
                    if (Time.time >= nextReport && SecurityManager.Instance != null)
                    {
                        nextReport = Time.time + reportCooldown;
                        SecurityManager.Instance.ServerReportSighting(p.transform.position);
                    }
                }
            }
        }

        if (cone != null)
        {
            bool alarm = SecurityManager.Instance != null && SecurityManager.Instance.AlarmActive;
            Color c = off ? new Color(0.3f, 0.3f, 0.35f, 0.05f)
                    : alarm ? new Color(1f, 0.2f, 0.15f, 0.28f)
                    : alerted ? new Color(1f, 0.6f, 0.15f, 0.28f)
                    : new Color(0.4f, 0.8f, 1f, 0.18f);
            cone.Set(viewRange, viewAngle, fwd, c);
        }

        // Kırmızı kayıt ışığı yanıp söner
        if (lensDot != null) lensDot.gameObject.SetActive(!off && Mathf.Repeat(Time.time + phaseOffset, 1.2f) < 0.9f);
    }

    private void OnDestroy()
    {
        if (bodyMat != null) Destroy(bodyMat);
        if (lensMat != null) Destroy(lensMat);
        if (paintMat != null) Destroy(paintMat);
    }
}
