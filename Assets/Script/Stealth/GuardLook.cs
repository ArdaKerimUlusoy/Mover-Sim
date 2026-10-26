using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Güvenlik görevlisinin görünüşü: müze ziyaretçileriyle (VisitorLook) aynı oranlarda bir polis.
///   Açık mavi kısa kollu gömlek, lacivert pantolon + kravat, siperlikli polis şapkası (altın armalı), rozet, kemer + coplu kılıf,
///   elinde fener, her güvenlikte (adına göre) farklı ten rengi / bıyık.
/// Hareket animasyonu konumdan hesaplanır (ağda herkes NetworkTransform ile aynı hareketi görür):
///   yürürken kol/bacak sallanır; kovalarken öne eğilip kollarını hızlı sallar ve feneri kaldırır;
///   şüphelenince kafasını öne uzatıp bakar.
/// GuardAI tarafından otomatik eklenir; eski kapsül ve küp şapka gizlenir (çarpıştırıcılar aynen kalır).
/// </summary>
public class GuardLook : MonoBehaviour
{
    private static readonly Color Navy = new Color(0.13f, 0.18f, 0.34f);
    private static readonly Color NavyDark = new Color(0.08f, 0.10f, 0.20f);
    private static readonly Color Shirt = new Color(0.58f, 0.72f, 0.92f);
    private static readonly Color Gold = new Color(0.96f, 0.78f, 0.28f);
    private static readonly Color Black = new Color(0.07f, 0.07f, 0.08f);
    private static readonly Color[] SkinTones =
    {
        new Color(1.00f, 0.82f, 0.68f), new Color(0.93f, 0.72f, 0.56f), new Color(0.78f, 0.56f, 0.40f), new Color(0.55f, 0.37f, 0.25f),
    };
    private static readonly Color[] HairColors =
    {
        new Color(0.22f, 0.15f, 0.10f), new Color(0.08f, 0.07f, 0.07f), new Color(0.62f, 0.60f, 0.58f), new Color(0.55f, 0.30f, 0.15f),
    };

    private GuardAI ai;
    private Transform root, body, head, armL, armR, legL, legR, torch;
    private readonly List<Material> mats = new List<Material>();

    private Vector3 lastPos;
    private float phase, speedSmooth, chaseSmooth, curiousSmooth;

    private void Awake()
    {
        ai = GetComponent<GuardAI>();

        // Eski görüntü: kapsül gövde + küp şapka (VisionCone'a dokunma)
        MeshRenderer own = GetComponent<MeshRenderer>();
        if (own != null) Destroy(own);
        Transform oldHat = transform.Find("Hat");
        if (oldHat != null) Destroy(oldHat.gameObject);

        Build();
        lastPos = transform.position;
    }

    private void Build()
    {
        int h = ArtworkLook.Hash(name) & 0x7fffffff;
        Color skinC = SkinTones[h % SkinTones.Length];
        Color hairC = HairColors[(h / 7) % HairColors.Length];
        bool mustache = (h / 3) % 3 != 0;
        float girth = 0.86f + ((h / 11) % 3) * 0.06f; // ziyaretçilerle aynı oranlar: zayıf / orta / hafif tombul
        float height = 0.98f + ((h / 13) % 3) * 0.04f;

        Material navy = M(Navy), navyDark = M(NavyDark), shirt = M(Shirt), gold = M(Gold), black = M(Black);
        Material skin = M(skinC), hair = M(hairC), white = M(Color.white), lens = M(new Color(1f, 0.95f, 0.6f));

        // Kök ayak hizasında (NavMeshAgent baseOffset → obje yerden yukarıda).
        // Sahnedeki güvenlik objesi ölçeklenmişse (ör. kapsül genişletilmiş) burada geri alınır: herkes gerçek boyda görünür.
        UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        float baseOffset = agent != null ? agent.baseOffset : 1f;
        Vector3 ls = transform.lossyScale;
        root = new GameObject("GuardAvatar").transform;
        root.SetParent(transform, false);
        root.localPosition = new Vector3(0f, -baseOffset, 0f);
        root.localScale = new Vector3(SafeInv(ls.x), SafeInv(ls.y) * height, SafeInv(ls.z));
        if (Mathf.Abs(ls.x - 1f) > 0.05f || Mathf.Abs(ls.y - 1f) > 0.05f || Mathf.Abs(ls.z - 1f) > 0.05f)
            Debug.Log($"[GuardLook] {name} ölçeği {ls} — görünüş 1:1'e düzeltildi.");

        body = new GameObject("Body").transform;
        body.SetParent(root, false);

        // Gövde: açık mavi polis gömleği, lacivert kravat, kemer, rozet, omuz apoletleri
        float g = girth;
        Part(body, "Torso", PrimitiveType.Sphere, new Vector3(0f, 1.12f, 0f), new Vector3(0.54f * g, 0.68f, 0.4f * g), shirt);
        Part(body, "Hips", PrimitiveType.Sphere, new Vector3(0f, 0.86f, 0f), new Vector3(0.47f * g, 0.24f, 0.35f * g), navyDark);
        Part(body, "Collar", PrimitiveType.Sphere, new Vector3(0f, 1.42f, 0.06f), new Vector3(0.24f, 0.1f, 0.16f), shirt);
        Part(body, "Tie", PrimitiveType.Cube, new Vector3(0f, 1.25f, 0.19f * g), new Vector3(0.06f, 0.26f, 0.03f), navy);
        Part(body, "PocketL", PrimitiveType.Cube, new Vector3(-0.12f * g, 1.24f, 0.18f * g), new Vector3(0.1f, 0.08f, 0.03f), M(Color.Lerp(Shirt, Color.black, 0.12f)));
        Part(body, "Belt", PrimitiveType.Cylinder, new Vector3(0f, 0.92f, 0f), new Vector3(0.5f * g, 0.035f, 0.37f * g), black);
        Part(body, "Buckle", PrimitiveType.Cube, new Vector3(0f, 0.92f, 0.185f * g), new Vector3(0.09f, 0.06f, 0.03f), gold);
        Part(body, "Badge", PrimitiveType.Cylinder, new Vector3(0.12f * g, 1.26f, 0.185f * g), new Vector3(0.08f, 0.012f, 0.08f), gold).localRotation = Quaternion.Euler(80f, 0f, 0f);
        Part(body, "Holster", PrimitiveType.Cube, new Vector3(0.25f * g, 0.86f, 0.02f), new Vector3(0.06f, 0.16f, 0.1f), black);
        Part(body, "Baton", PrimitiveType.Cylinder, new Vector3(-0.25f * g, 0.82f, -0.04f), new Vector3(0.04f, 0.16f, 0.04f), black);
        Part(body, "Radio", PrimitiveType.Cube, new Vector3(-0.14f * g, 1.33f, 0.16f * g), new Vector3(0.06f, 0.1f, 0.04f), black);
        Part(body, "Neck", PrimitiveType.Cylinder, new Vector3(0f, 1.48f, 0f), new Vector3(0.15f, 0.06f, 0.15f), skin);

        // Kafa: ziyaretçilerle aynı boyda yüz, gözler, kaşlar, (bıyık), kulaklar, kısa saç
        head = new GameObject("Head").transform;
        head.SetParent(body, false);
        head.localPosition = new Vector3(0f, 1.53f, 0f);
        head.localScale = Vector3.one * 0.84f;
        Part(head, "Face", PrimitiveType.Sphere, new Vector3(0f, 0.15f, 0f), new Vector3(0.54f, 0.54f, 0.52f), skin);
        Part(head, "EarL", PrimitiveType.Sphere, new Vector3(-0.26f, 0.14f, 0f), new Vector3(0.08f, 0.12f, 0.06f), skin);
        Part(head, "EarR", PrimitiveType.Sphere, new Vector3(0.26f, 0.14f, 0f), new Vector3(0.08f, 0.12f, 0.06f), skin);
        Part(head, "EyeL", PrimitiveType.Sphere, new Vector3(-0.1f, 0.18f, 0.23f), new Vector3(0.08f, 0.1f, 0.05f), white);
        Part(head, "EyeR", PrimitiveType.Sphere, new Vector3(0.1f, 0.18f, 0.23f), new Vector3(0.08f, 0.1f, 0.05f), white);
        Part(head, "PupilL", PrimitiveType.Sphere, new Vector3(-0.1f, 0.175f, 0.255f), new Vector3(0.04f, 0.05f, 0.03f), black);
        Part(head, "PupilR", PrimitiveType.Sphere, new Vector3(0.1f, 0.175f, 0.255f), new Vector3(0.04f, 0.05f, 0.03f), black);
        Part(head, "BrowL", PrimitiveType.Cube, new Vector3(-0.1f, 0.26f, 0.24f), new Vector3(0.11f, 0.022f, 0.03f), hair).localRotation = Quaternion.Euler(0f, 0f, -10f);
        Part(head, "BrowR", PrimitiveType.Cube, new Vector3(0.1f, 0.26f, 0.24f), new Vector3(0.11f, 0.022f, 0.03f), hair).localRotation = Quaternion.Euler(0f, 0f, 10f);
        Part(head, "Nose", PrimitiveType.Sphere, new Vector3(0f, 0.11f, 0.27f), new Vector3(0.07f, 0.07f, 0.06f), skin);
        if (mustache)
        {
            Part(head, "MustacheL", PrimitiveType.Sphere, new Vector3(-0.05f, 0.055f, 0.25f), new Vector3(0.11f, 0.04f, 0.04f), hair).localRotation = Quaternion.Euler(0f, 0f, 14f);
            Part(head, "MustacheR", PrimitiveType.Sphere, new Vector3(0.05f, 0.055f, 0.25f), new Vector3(0.11f, 0.04f, 0.04f), hair).localRotation = Quaternion.Euler(0f, 0f, -14f);
        }
        Part(head, "Hair", PrimitiveType.Sphere, new Vector3(0f, 0.22f, -0.05f), new Vector3(0.55f, 0.36f, 0.52f), hair);

        // Polis şapkası (kafaya oturan bant, hafif öne taşan düz tepe, siyah siperlik, altın arma)
        Part(head, "CapBand", PrimitiveType.Cylinder, new Vector3(0f, 0.34f, 0f), new Vector3(0.52f, 0.05f, 0.52f), navyDark);
        Part(head, "CapStripe", PrimitiveType.Cylinder, new Vector3(0f, 0.31f, 0f), new Vector3(0.53f, 0.012f, 0.53f), gold);
        Part(head, "CapTop", PrimitiveType.Cylinder, new Vector3(0f, 0.41f, 0.02f), new Vector3(0.6f, 0.035f, 0.58f), navy);
        Part(head, "Visor", PrimitiveType.Cylinder, new Vector3(0f, 0.3f, 0.2f), new Vector3(0.38f, 0.012f, 0.26f), black).localRotation = Quaternion.Euler(-14f, 0f, 0f);
        Part(head, "CapBadge", PrimitiveType.Sphere, new Vector3(0f, 0.37f, 0.27f), new Vector3(0.08f, 0.08f, 0.03f), gold);

        // Kollar: kısa kollu gömlek, apolet, çıplak ön kol, el; sağ elde fener
        armL = Arm(body, "ArmL", -1f, girth, shirt, navy, skin);
        armR = Arm(body, "ArmR", 1f, girth, shirt, navy, skin);
        torch = new GameObject("Flashlight").transform;
        torch.SetParent(armR, false);
        torch.localPosition = new Vector3(0f, -0.64f, 0.06f);
        Part(torch, "Body", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.08f), new Vector3(0.05f, 0.1f, 0.05f), black).localRotation = Quaternion.Euler(90f, 0f, 0f);
        Part(torch, "Lens", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.19f), new Vector3(0.075f, 0.02f, 0.075f), lens).localRotation = Quaternion.Euler(90f, 0f, 0f);

        // Bacaklar: lacivert pantolon, siyah ayakkabı (ziyaretçilerle aynı uzunluk)
        legL = Leg(root, "LegL", -0.13f * girth, navyDark, black);
        legR = Leg(root, "LegR", 0.13f * girth, navyDark, black);
    }

    private static float SafeInv(float v) => Mathf.Abs(v) < 0.001f ? 1f : 1f / v;

    private Transform Arm(Transform parent, string n, float side, float girth, Material sleeve, Material epaulette, Material skin)
    {
        Transform t = new GameObject(n).transform;
        t.SetParent(parent, false);
        t.localPosition = new Vector3(side * (0.25f * girth + 0.09f), 1.38f, 0f);
        Part(t, "Shoulder", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.16f, sleeve);
        Part(t, "Epaulette", PrimitiveType.Cube, new Vector3(0f, 0.075f, 0f), new Vector3(0.13f, 0.025f, 0.11f), epaulette);
        Part(t, "UpperArm", PrimitiveType.Capsule, new Vector3(0f, -0.17f, 0f), new Vector3(0.14f, 0.16f, 0.14f), sleeve);
        Part(t, "Cuff", PrimitiveType.Cylinder, new Vector3(0f, -0.3f, 0f), new Vector3(0.15f, 0.025f, 0.15f), sleeve);
        Part(t, "Forearm", PrimitiveType.Capsule, new Vector3(0f, -0.45f, 0f), new Vector3(0.11f, 0.15f, 0.11f), skin);
        Part(t, "Hand", PrimitiveType.Sphere, new Vector3(0f, -0.64f, 0.01f), new Vector3(0.12f, 0.13f, 0.11f), skin);
        return t;
    }

    private Transform Leg(Transform parent, string n, float x, Material pants, Material shoe)
    {
        Transform t = new GameObject(n).transform;
        t.SetParent(parent, false);
        t.localPosition = new Vector3(x, 0.82f, 0f);
        Part(t, "Thigh", PrimitiveType.Capsule, new Vector3(0f, -0.2f, 0f), new Vector3(0.18f, 0.21f, 0.18f), pants);
        Part(t, "Shin", PrimitiveType.Capsule, new Vector3(0f, -0.55f, 0f), new Vector3(0.15f, 0.18f, 0.15f), pants);
        Part(t, "Shoe", PrimitiveType.Sphere, new Vector3(0f, -0.76f, 0.06f), new Vector3(0.17f, 0.11f, 0.3f), shoe);
        return t;
    }

    private Material M(Color c)
    {
        Material m = ArtworkLook.MakeLit(c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
        mats.Add(m);
        return m;
    }

    private static readonly Dictionary<PrimitiveType, Mesh> meshCache = new Dictionary<PrimitiveType, Mesh>();

    private static Mesh PrimitiveMesh(PrimitiveType type)
    {
        if (meshCache.TryGetValue(type, out Mesh cached) && cached != null) return cached;
        Mesh mesh = null;
        try { mesh = Resources.GetBuiltinResource<Mesh>(type + ".fbx"); }
        catch (System.Exception) { }
        if (mesh == null)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            temp.SetActive(false);
            mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
        }
        meshCache[type] = mesh;
        return mesh;
    }

    /// <summary>Çarpıştırıcısız parça (görüş ışınları ve etkileşim etkilenmesin).</summary>
    private static Transform Part(Transform parent, string n, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(type);
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.On;
        return go.transform;
    }

    private float slipUntil = -1f;
    private float slipT;

    /// <summary>Muz kabuğunda kaydı: bu kadar saniye yere yatar (sadece görsel).</summary>
    public void PlaySlip(float seconds) => slipUntil = Time.time + seconds;

    private void Update()
    {
        if (body == null) return;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // Kayma: kapsül arkasına devrilir, sonra kalkar
        slipT = Mathf.MoveTowards(slipT, Time.time < slipUntil ? 1f : 0f, dt * 5f);
        if (root != null)
        {
            root.localRotation = Quaternion.Euler(-80f * slipT, 0f, 0f);
        }

        Vector3 delta = transform.position - lastPos;
        lastPos = transform.position;
        float speed = new Vector3(delta.x, 0f, delta.z).magnitude / dt;
        if (speed > 25f) speed = 0f; // ışınlanma
        speedSmooth = Mathf.Lerp(speedSmooth, speed, 1f - Mathf.Exp(-8f * dt));

        GuardAI.State st = ai != null ? ai.CurrentState : GuardAI.State.Patrol;
        chaseSmooth = Mathf.Lerp(chaseSmooth, st == GuardAI.State.Chase ? 1f : 0f, 1f - Mathf.Exp(-6f * dt));
        curiousSmooth = Mathf.Lerp(curiousSmooth, st == GuardAI.State.Suspicious || st == GuardAI.State.Investigate ? 1f : 0f, 1f - Mathf.Exp(-5f * dt));

        float move = Mathf.Clamp01(speedSmooth / 5f);
        phase += speedSmooth * 2.2f * dt;
        float swing = Mathf.Sin(phase) * Mathf.Lerp(30f, 55f, chaseSmooth) * move;

        float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.06f * move + Mathf.Sin(Time.time * 1.6f) * 0.006f;
        body.localPosition = new Vector3(0f, bob, 0f);
        body.localRotation = Quaternion.Euler(move * 6f + chaseSmooth * 10f, 0f, Mathf.Sin(phase) * 2f * move);

        // Şüphelenince kafayı öne uzatıp hafifçe yana eğer ("hmm?")
        head.localRotation = Quaternion.Euler(curiousSmooth * 10f - chaseSmooth * 6f, 0f, curiousSmooth * Mathf.Sin(Time.time * 2f) * 10f);

        // Sol kol sallanır; sağ elde fener: devriyede aşağıda, şüphe / kovalamada öne kaldırılır
        float torchRaise = Mathf.Max(curiousSmooth, chaseSmooth);
        armL.localRotation = Quaternion.Euler(swing, 0f, -9f);
        armR.localRotation = Quaternion.Euler(Mathf.Lerp(-swing * 0.6f, -80f, torchRaise), 0f, 9f - torchRaise * 6f);

        legL.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        legR.localRotation = Quaternion.Euler(swing, 0f, 0f);
    }

    private void OnDestroy()
    {
        foreach (Material m in mats) if (m != null) Destroy(m);
    }
}
