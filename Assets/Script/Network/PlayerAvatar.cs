using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Oyuncunun görünen gövdesi: kodla kurulan tombul, sevimli bir hırsız (GDD Bölüm 9: cozy, yuvarlak hatlı).
/// Bereli, maskeli, sırtında ganimet çuvalı var; çuval çanta doldukça şişer.
/// Yürürken kollar / bacaklar sallanır, gövde hafifçe zıplar; eğilince çöker; elinde eser varken kollarını öne uzatır.
/// Kendi oyuncunda gövde çizilmez (kamerayı kapatmasın) ama gölgesi yere düşer.
/// Prefab'daki eski kapsül görüntüsü kaldırılır; çarpıştırıcılar ve CharacterController aynen kalır.
/// PlayerNetworkSetup tarafından otomatik eklenir.
/// </summary>
public class PlayerAvatar : MonoBehaviour
{
    private static readonly Color Skin = new Color(1.00f, 0.82f, 0.68f);
    private static readonly Color Dark = new Color(0.16f, 0.16f, 0.20f);
    private static readonly Color Sack = new Color(0.55f, 0.42f, 0.28f);

    private PlayerNetworkSetup netSetup;
    private PlayerStealth stealth;
    private PlayerInventory inventory;

    private Transform root, body, head, armL, armR, legL, legR, sack;
    private Material mainMat, hatMat, skinMat, darkMat, whiteMat, sackMat, cheekMat;
    private readonly List<Renderer> renderers = new List<Renderer>();

    private Vector3 lastPos;
    private float phase, speedSmooth, crouchSmooth, holdSmooth, landSquash;
    private bool wasGroundedGuess = true;
    private float lastY;
    private bool ownerApplied;

    private void Awake()
    {
        netSetup = GetComponent<PlayerNetworkSetup>();
        stealth = GetComponent<PlayerStealth>();
        inventory = GetComponent<PlayerInventory>();

        // Prefab'daki eski kapsül / küp görüntüsünü kaldır (çarpıştırıcılara dokunma)
        foreach (MeshRenderer mr in GetComponentsInChildren<MeshRenderer>(true)) Destroy(mr);

        Build();
        lastPos = transform.position;
        lastY = transform.position.y;
    }

    private void Build()
    {
        mainMat = ArtworkLook.MakeLit(Color.white);
        hatMat = ArtworkLook.MakeLit(Color.white);
        skinMat = ArtworkLook.MakeLit(Skin);
        darkMat = ArtworkLook.MakeLit(Dark);
        whiteMat = ArtworkLook.MakeLit(Color.white);
        sackMat = ArtworkLook.MakeLit(Sack);
        cheekMat = ArtworkLook.MakeLit(new Color(1f, 0.6f, 0.6f));

        // Kök ayak hizasında (CharacterController merkezi gövde ortasında, yüksekliği 2)
        root = new GameObject("Avatar").transform;
        root.SetParent(transform, false);
        root.localPosition = new Vector3(0f, -1f, 0f);

        body = new GameObject("Body").transform;
        body.SetParent(root, false);

        // Gövde: çizgili kazak hissi için koyu kuşak
        Part(body, "Torso", PrimitiveType.Sphere, new Vector3(0f, 0.72f, 0f), new Vector3(0.74f, 0.80f, 0.64f), mainMat);
        Part(body, "Belt", PrimitiveType.Cylinder, new Vector3(0f, 0.60f, 0f), new Vector3(0.70f, 0.035f, 0.61f), darkMat);

        // Kafa
        head = new GameObject("Head").transform;
        head.SetParent(body, false);
        head.localPosition = new Vector3(0f, 1.22f, 0f);
        Part(head, "Face", PrimitiveType.Sphere, new Vector3(0f, 0.16f, 0f), new Vector3(0.60f, 0.56f, 0.58f), skinMat);
        Part(head, "Mask", PrimitiveType.Sphere, new Vector3(0f, 0.20f, 0.05f), new Vector3(0.615f, 0.17f, 0.545f), darkMat);
        Part(head, "EyeL", PrimitiveType.Sphere, new Vector3(-0.13f, 0.20f, 0.265f), new Vector3(0.13f, 0.15f, 0.07f), whiteMat);
        Part(head, "EyeR", PrimitiveType.Sphere, new Vector3(0.13f, 0.20f, 0.265f), new Vector3(0.13f, 0.15f, 0.07f), whiteMat);
        Part(head, "PupilL", PrimitiveType.Sphere, new Vector3(-0.12f, 0.19f, 0.295f), new Vector3(0.06f, 0.08f, 0.03f), darkMat);
        Part(head, "PupilR", PrimitiveType.Sphere, new Vector3(0.12f, 0.19f, 0.295f), new Vector3(0.06f, 0.08f, 0.03f), darkMat);
        Part(head, "CheekL", PrimitiveType.Sphere, new Vector3(-0.2f, 0.07f, 0.22f), new Vector3(0.1f, 0.06f, 0.04f), cheekMat);
        Part(head, "CheekR", PrimitiveType.Sphere, new Vector3(0.2f, 0.07f, 0.22f), new Vector3(0.1f, 0.06f, 0.04f), cheekMat);
        // Bere + ponpon
        Part(head, "Beanie", PrimitiveType.Sphere, new Vector3(0f, 0.33f, -0.01f), new Vector3(0.63f, 0.42f, 0.61f), hatMat);
        Part(head, "BeanieRim", PrimitiveType.Cylinder, new Vector3(0f, 0.30f, -0.01f), new Vector3(0.64f, 0.04f, 0.62f), darkMat);
        Part(head, "Pompom", PrimitiveType.Sphere, new Vector3(0f, 0.57f, -0.02f), new Vector3(0.16f, 0.16f, 0.16f), whiteMat);

        // Kollar (omuz pivotlu) ve eldivenler
        armL = Limb(body, "ArmL", new Vector3(-0.40f, 1.02f, 0f), new Vector3(0.17f, 0.24f, 0.17f), 0.24f, mainMat, darkMat, 0.11f);
        armR = Limb(body, "ArmR", new Vector3(0.40f, 1.02f, 0f), new Vector3(0.17f, 0.24f, 0.17f), 0.24f, mainMat, darkMat, 0.11f);

        // Bacaklar (kalça pivotlu) ve ayakkabılar — gövdenin değil kökün çocuğu (zıplamada yerde kalsınlar)
        legL = Limb(root, "LegL", new Vector3(-0.17f, 0.42f, 0f), new Vector3(0.21f, 0.20f, 0.21f), 0.20f, darkMat, darkMat, 0f);
        legR = Limb(root, "LegR", new Vector3(0.17f, 0.42f, 0f), new Vector3(0.21f, 0.20f, 0.21f), 0.20f, darkMat, darkMat, 0f);
        Part(legL, "Shoe", PrimitiveType.Sphere, new Vector3(0f, -0.37f, 0.06f), new Vector3(0.22f, 0.12f, 0.32f), whiteMat);
        Part(legR, "Shoe", PrimitiveType.Sphere, new Vector3(0f, -0.37f, 0.06f), new Vector3(0.22f, 0.12f, 0.32f), whiteMat);

        // Sırttaki ganimet çuvalı
        sack = new GameObject("Sack").transform;
        sack.SetParent(body, false);
        sack.localPosition = new Vector3(0f, 0.86f, -0.38f);
        Part(sack, "Bag", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.50f, 0.56f, 0.44f), sackMat);
        Part(sack, "Knot", PrimitiveType.Sphere, new Vector3(0f, 0.30f, 0f), new Vector3(0.16f, 0.14f, 0.16f), sackMat);
        Part(sack, "Rope", PrimitiveType.Cylinder, new Vector3(0f, 0.24f, 0f), new Vector3(0.2f, 0.015f, 0.2f), darkMat);
    }

    private Transform Limb(Transform parent, string name, Vector3 pivot, Vector3 scale, float halfLength, Material mat, Material tipMat, float tipSize)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pivot;
        Part(t, "Mesh", PrimitiveType.Capsule, new Vector3(0f, -halfLength, 0f), scale, mat);
        if (tipSize > 0f) Part(t, "Hand", PrimitiveType.Sphere, new Vector3(0f, -halfLength * 2f, 0f), Vector3.one * (tipSize * 2f), tipMat);
        return t;
    }

    private static readonly Dictionary<PrimitiveType, Mesh> meshCache = new Dictionary<PrimitiveType, Mesh>();

    /// <summary>Unity'nin hazır küre / kapsül / silindir mesh'i (çarpıştırıcı oluşturmadan).</summary>
    private static Mesh PrimitiveMesh(PrimitiveType type)
    {
        if (meshCache.TryGetValue(type, out Mesh cached) && cached != null) return cached;
        Mesh mesh = null;
        try { mesh = Resources.GetBuiltinResource<Mesh>(type + ".fbx"); }
        catch (System.Exception) { /* bulunamazsa aşağıda geçici bir primitive'den alınır */ }
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

    private void Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
    {
        // Çarpıştırıcısız: etkileşim ışınları, görüş testleri ve taşıma sistemi gövde parçalarına takılmasın
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(type);
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        renderers.Add(r);
    }

    /// <summary>Oyuncunun seçtiği renk: kazak ve bere.</summary>
    public void SetColor(Color c)
    {
        SetMat(mainMat, c);
        SetMat(hatMat, Color.Lerp(c, Color.black, 0.28f));
    }

    private static void SetMat(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
    }

    private void Update()
    {
        // Kendi gövden görünmesin, sadece gölgesi düşsün (ağda doğduktan sonra belli olur)
        if (!ownerApplied && netSetup != null && netSetup.IsSpawned)
        {
            ownerApplied = true;
            if (netSetup.IsOwner)
                foreach (Renderer r in renderers) if (r != null) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = transform.position - lastPos;
        lastPos = transform.position;

        float speed = new Vector3(delta.x, 0f, delta.z).magnitude / dt;
        if (speed > 30f) speed = 0f; // ışınlanma
        speedSmooth = Mathf.Lerp(speedSmooth, speed, 1f - Mathf.Exp(-10f * dt));

        // İniş: dikey hız aniden durduysa kısa bir "yaylanma"
        float vy = (transform.position.y - lastY) / dt;
        lastY = transform.position.y;
        bool grounded = Mathf.Abs(vy) < 0.4f;
        if (grounded && !wasGroundedGuess) landSquash = 1f;
        wasGroundedGuess = grounded;
        landSquash = Mathf.MoveTowards(landSquash, 0f, dt * 5f);

        bool crouch = stealth != null && stealth.IsSpawned && stealth.IsCrouching;
        crouchSmooth = Mathf.Lerp(crouchSmooth, crouch ? 1f : 0f, 1f - Mathf.Exp(-10f * dt));
        bool holding = inventory != null && inventory.IsSpawned && inventory.HasHeldItem;
        holdSmooth = Mathf.Lerp(holdSmooth, holding ? 1f : 0f, 1f - Mathf.Exp(-8f * dt));

        float move = Mathf.Clamp01(speedSmooth / 4.5f);
        phase += speedSmooth * 2.4f * dt;
        float swing = Mathf.Sin(phase) * 38f * move;

        // Gövde: yürürken zıplar, hızlanınca öne eğilir, eğilince çöker
        float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.05f * move + Mathf.Sin(Time.time * 1.8f) * 0.008f;
        float squash = 1f - crouchSmooth * 0.38f - landSquash * 0.12f;
        body.localPosition = new Vector3(0f, bob - crouchSmooth * 0.12f, 0f);
        body.localScale = new Vector3(1f + (1f - squash) * 0.35f, squash, 1f + (1f - squash) * 0.35f);
        body.localRotation = Quaternion.Euler(move * 7f + crouchSmooth * 14f, 0f, Mathf.Sin(phase) * 2.5f * move);

        // Kafa: baktığı yöne eğilir (diğer oyuncular nereye baktığını görsün)
        float pitch = netSetup != null && netSetup.IsSpawned ? netSetup.LookPitch : 0f;
        head.localRotation = Quaternion.Euler(Mathf.Clamp(pitch, -40f, 40f) * 0.7f, 0f, 0f);

        // Kollar: yürürken ters fazda sallanır; elinde eser varsa öne uzanır
        float reach = -72f * holdSmooth;
        armL.localRotation = Quaternion.Euler(Mathf.Lerp(swing, reach, holdSmooth), 0f, -10f - holdSmooth * 12f);
        armR.localRotation = Quaternion.Euler(Mathf.Lerp(-swing, reach, holdSmooth), 0f, 10f + holdSmooth * 12f);

        legL.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        legR.localRotation = Quaternion.Euler(swing, 0f, 0f);
        float legScale = 1f - crouchSmooth * 0.45f;
        legL.localScale = legR.localScale = new Vector3(1f, legScale, 1f);

        // Çuval çanta doldukça şişer
        float load = inventory != null && inventory.IsSpawned ? inventory.LoadRatio : 0f;
        float s = Mathf.Lerp(0.7f, 1.35f, load);
        sack.localScale = Vector3.Lerp(sack.localScale, new Vector3(s, s, s), 1f - Mathf.Exp(-6f * dt));
        sack.localRotation = Quaternion.Euler(-swing * 0.12f, 0f, Mathf.Sin(phase) * 4f * move);
    }

    private void OnDestroy()
    {
        foreach (Material m in new[] { mainMat, hatMat, skinMat, darkMat, whiteMat, sackMat, cheekMat })
            if (m != null) Destroy(m);
    }
}
