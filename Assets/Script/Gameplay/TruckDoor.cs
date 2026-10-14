using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Kamyonun arka kapısı: açık arka uca iki kanatlı kapı. Yalnızca GÖRSEL ve ağ nesnesi DEĞİLDİR:
/// TruckZone her bilgisayarda aynı yere kurar (sahneye bir şey eklemek gerekmez); durum MuseumSession.DoorClosed'dan okunur
/// (sunucu kapatınca herkeste aynı anda animasyonla kapanır; yeni görevde açık başlar; geç katılan doğru halini görür).
/// Kapı açıkken çarpıştırıcısı yoktur (müzeye çıkış serbest). Kapalıyken de çarpıştırıcı yok: sekansta kontroller zaten kilitli.
/// </summary>
public class TruckDoor : MonoBehaviour
{
    private const float CloseSeconds = 1.3f;
    private const float OpenSeconds = 0.9f;
    private const float OpenAngle = 95f;

    // TruckZone yerel koordinatları: kutu 8 x 3 x 5 (merkez = kamyon iç hacmi), arka uç +x (müzeye bakan taraf)
    private const float HingeX = 3.97f;
    private const float HingeZ = 2.45f;
    private const float FloorY = -1.44f;
    private const float LeafHeight = 2.95f;
    private const float LeafLength = 2.43f;

    private readonly List<Material> mats = new List<Material>();
    private Transform leafSouth, leafNorth; // güney (−z) ve kuzey (+z) kanat
    private float progress;                 // 0 = açık, 1 = kapalı
    private bool initialized;

    public static void CreateFor(TruckZone zone)
    {
        if (zone == null || zone.transform.Find("TruckDoor") != null) return;
        GameObject go = new GameObject("TruckDoor");
        go.transform.SetParent(zone.transform, false);
        go.AddComponent<TruckDoor>();
    }

    private void Awake()
    {
        Color panel = new Color(0.86f, 0.5f, 0.27f);
        Color rib = new Color(0.97f, 0.9f, 0.72f);
        Color dark = new Color(0.16f, 0.17f, 0.19f);

        leafSouth = BuildLeaf("Leaf_South", -HingeZ, 1f, panel, rib, dark);
        leafNorth = BuildLeaf("Leaf_North", HingeZ, -1f, panel, rib, dark);
        Apply(0f);
    }

    /// <summary>Menteşe noktasında duran bir kanat; kapalıyken menteşeden ortaya (dir yönünde) uzanır.</summary>
    private Transform BuildLeaf(string name, float hingeZ, float dir, Color panel, Color rib, Color dark)
    {
        Transform hinge = new GameObject(name).transform;
        hinge.SetParent(transform, false);
        hinge.localPosition = new Vector3(HingeX, FloorY, hingeZ);

        float cy = LeafHeight * 0.5f;
        float cz = dir * LeafLength * 0.5f;
        Part(hinge, "Panel", PrimitiveType.Cube, new Vector3(0f, cy, cz), new Vector3(0.07f, LeafHeight, LeafLength), panel);
        Part(hinge, "RibLow", PrimitiveType.Cube, new Vector3(0.045f, 0.75f, cz), new Vector3(0.03f, 0.22f, LeafLength - 0.2f), rib);
        Part(hinge, "RibHigh", PrimitiveType.Cube, new Vector3(0.045f, 2.2f, cz), new Vector3(0.03f, 0.22f, LeafLength - 0.2f), rib);
        Part(hinge, "Handle", PrimitiveType.Cube, new Vector3(0.1f, 1.35f, dir * (LeafLength - 0.25f)), new Vector3(0.07f, 0.55f, 0.07f), dark);
        Part(hinge, "HingeLow", PrimitiveType.Cylinder, new Vector3(0f, 0.5f, 0f), new Vector3(0.1f, 0.12f, 0.1f), dark);
        Part(hinge, "HingeHigh", PrimitiveType.Cylinder, new Vector3(0f, 2.4f, 0f), new Vector3(0.1f, 0.12f, 0.1f), dark);
        return hinge;
    }

    private void Part(Transform parent, string n, PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = n;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        c.a = 1f;
        Material m = ArtworkLook.MakeLit(c);
        mats.Add(m);
        go.GetComponent<Renderer>().sharedMaterial = m;
    }

    private void OnDestroy()
    {
        foreach (Material m in mats) if (m != null) Destroy(m);
    }

    private void Update()
    {
        MuseumSession s = MuseumSession.Instance;
        float target = s != null && s.Ready && s.DoorClosed ? 1f : 0f;

        if (!initialized)
        {
            // Oyuna (ya da kapalıyken sekansın ortasına) yeni girildi: animasyonsuz doğru haline otur
            initialized = true;
            progress = target;
            Apply(progress);
            return;
        }
        if (Mathf.Approximately(progress, target)) return;

        float before = progress;
        float speed = 1f / (target > progress ? CloseSeconds : OpenSeconds);
        progress = Mathf.MoveTowards(progress, target, speed * Time.deltaTime);
        Apply(progress);

        // Sesler: kapanış tamamlanınca "pat", yeniden açılırken menteşe sesi
        if (target >= 1f && progress >= 1f && before < 1f) GameAudio.PlayAt(SoundId.DoorClose, transform.position, 1f, 0.03f);
        else if (target <= 0f && before >= 1f) GameAudio.PlayAt(SoundId.DoorOpen, transform.position, 0.8f, 0.03f);
    }

    private void Apply(float p)
    {
        // Yumuşak hızlanıp yavaşlayan kapanış; son anda hafif "çarpma" payı yok (cozy)
        float open = 1f - Mathf.SmoothStep(0f, 1f, p);
        if (leafSouth != null) leafSouth.localRotation = Quaternion.Euler(0f, open * OpenAngle, 0f);
        if (leafNorth != null) leafNorth.localRotation = Quaternion.Euler(0f, -open * OpenAngle, 0f);
    }
}
