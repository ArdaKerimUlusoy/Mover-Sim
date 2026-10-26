using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BOYA TOPU — fırçayla fırlatılan, yerçekimli balistik boya topu (saf MonoBehaviour, ağ kodu yok).
/// Trace(): sunucu ve istemcilerin AYNI hesapla kullandığı raycast adımlı simülasyon (isabet SUNUCUDA belirlenir: SprayPaint.ServerThrow).
/// Launch(): sadece görsel — önceden hesaplanan yolu animasyonla oynatır, çarpınca duvara/zemine küçük leke bırakır.
/// </summary>
public class PaintProjectile : MonoBehaviour
{
    public const float Speed = 24f, Gravity = 9.81f;
    public const float MaxRange = 12f;      // yol uzunluğu sınırı (m)
    public const float LensRadius = 0.35f;  // lens isabet yarıçapı (m)
    public const float Cooldown = 1f;       // atışlar arası bekleme (sn)
    private const float Step = 0.02f;
    private const int MaxSplats = 40;
    private const float SplatSeconds = 12f;

    public struct TraceResult
    {
        public Vector3 point, normal;
        public float time;            // uçuş süresi (sn)
        public bool surface;          // duvara / zemine çarptı mı
        public RoomCamera camera;     // lense isabet ettiyse o kamera
    }

    private static readonly RaycastHit[] buf = new RaycastHit[16];
    private static readonly Queue<GameObject> splats = new Queue<GameObject>();

    /// <summary>Topun yolunu adım adım izler: ilk çarpılan yüzey ya da boyanabilir kameranın lensi (≤ LensRadius).</summary>
    public static TraceResult Trace(Vector3 origin, Vector3 vel, Transform ignoreRoot)
    {
        TraceResult res = default;
        Vector3 p = origin, v = vel;
        float t = 0f, dist = 0f;
        while (dist < MaxRange && t < 5f)
        {
            Vector3 np = p + v * Step + 0.5f * Gravity * Step * Step * Vector3.down;
            Vector3 seg = np - p;
            float len = seg.magnitude;
            if (len < 1e-5f) break;
            Vector3 d = seg / len;

            // En yakın yüzey (kendi gövden hariç, tetikleyiciler yok sayılır)
            bool hit = false;
            RaycastHit best = default;
            float bestD = len;
            int n = Physics.RaycastNonAlloc(p, d, buf, len, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = buf[i];
                if (h.distance > bestD) continue;
                if (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)) continue;
                bestD = h.distance;
                best = h;
                hit = true;
            }

            // Lens: bu adımın (çarpma noktasına kadar) doğru parçasına en yakın mesafe
            foreach (RoomCamera rc in RoomCamera.All)
            {
                if (rc == null || !SprayPaint.CanPaintCamera(rc)) continue;
                Vector3 lp = rc.LensPosition;
                float along = Mathf.Clamp(Vector3.Dot(lp - p, d), 0f, bestD);
                if ((p + d * along - lp).sqrMagnitude > LensRadius * LensRadius) continue;
                res.camera = rc;
                res.point = p + d * along;
                res.time = t + Step * (along / len);
                return res;
            }

            if (hit)
            {
                res.time = t + Step * (bestD / len);
                res.point = best.point;
                res.normal = best.normal;
                // Kamera gövdesine (lense yakın) çarptıysa lens sayılır
                RoomCamera rcHit = best.collider.GetComponentInParent<RoomCamera>();
                if (rcHit != null && SprayPaint.CanPaintCamera(rcHit) && (best.point - rcHit.LensPosition).magnitude <= LensRadius * 2f)
                {
                    res.camera = rcHit;
                    return res;
                }
                res.surface = best.collider.GetComponentInParent<PlayerStealth>() == null; // oyuncuya çarpınca leke yok
                return res;
            }

            p = np;
            v += Vector3.down * (Gravity * Step);
            t += Step;
            dist += len;
        }
        res.time = t;
        res.point = p;
        return res;
    }

    // ---------------- Görsel ----------------

    private Vector3 origin, vel0;
    private TraceResult trace;
    private int color;
    private float age;

    /// <summary>Yerel görsel: topu fırlat (renk = boya rengi indeksi). Çarpınca leke bırakır.</summary>
    public static void Launch(Vector3 origin, Vector3 vel, int color, Transform ignoreRoot)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "PaintBall";
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 0.14f;
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = SprayPaint.ColorMat(color);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        PaintProjectile pp = go.AddComponent<PaintProjectile>();
        pp.origin = origin;
        pp.vel0 = vel;
        pp.color = color;
        pp.trace = Trace(origin, vel, ignoreRoot);
        GameAudio.PlayAt(SoundId.CoinThrow, origin, 0.3f, 0.2f);
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= trace.time) { Finish(); return; }
        transform.position = origin + vel0 * age + 0.5f * Gravity * age * age * Vector3.down;
    }

    private void Finish()
    {
        if (trace.surface) Splat(trace.point, trace.normal, color);
        else if (trace.camera != null) GameAudio.PlayAt(SoundId.CoinLand, trace.point, 0.4f);
        Destroy(gameObject);
    }

    /// <summary>Duvara / zemine yassı boya lekesi + birkaç damla; birkaç sn sonra silinir.</summary>
    private static void Splat(Vector3 point, Vector3 normal, int color)
    {
        if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
        normal.Normalize();
        while (splats.Count >= MaxSplats)
        {
            GameObject old = splats.Dequeue();
            if (old != null) Destroy(old);
        }
        Vector3 upHint = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
        Quaternion rot = Quaternion.LookRotation(-normal, upHint);
        Material mat = SprayPaint.ColorMat(color);

        GameObject root = new GameObject("PaintSplat");
        root.transform.SetPositionAndRotation(point + normal * 0.012f, rot);
        AddBlob(root.transform, Vector3.zero, new Vector3(0.34f, 0.34f, 0.025f), mat);
        for (int k = 0; k < 5; k++)
        {
            float a = Random.value * Mathf.PI * 2f, rr = Random.Range(0.2f, 0.34f), s = Random.Range(0.05f, 0.11f);
            AddBlob(root.transform, new Vector3(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, 0f), new Vector3(s, s, 0.02f), mat);
        }
        Destroy(root, SplatSeconds);
        splats.Enqueue(root);
        GameAudio.PlayAt(SoundId.CoinLand, point, 0.35f);
    }

    private static void AddBlob(Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Collider c = g.GetComponent<Collider>();
        if (c != null) Destroy(c);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = scale;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }
}
