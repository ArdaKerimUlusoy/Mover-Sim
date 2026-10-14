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
    private Transform torso;
    private GameObject[] hats, faces;
    private int build = -1;
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
        torso = new GameObject("TorsoGroup").transform;
        torso.SetParent(body, false);
        // Daha uzun, net uzuvlu gövde: göğüs + kuşak + boyun (kollar ve bacaklar gövdeden ayrı görünür)
        Part(torso, "Torso", PrimitiveType.Sphere, new Vector3(0f, 1.12f, 0f), new Vector3(0.58f, 0.70f, 0.44f), mainMat);
        Part(torso, "Belt", PrimitiveType.Cylinder, new Vector3(0f, 0.86f, 0f), new Vector3(0.52f, 0.035f, 0.40f), darkMat);
        Part(torso, "Hips", PrimitiveType.Sphere, new Vector3(0f, 0.84f, 0f), new Vector3(0.48f, 0.22f, 0.36f), darkMat);
        Part(body, "Neck", PrimitiveType.Cylinder, new Vector3(0f, 1.48f, 0f), new Vector3(0.16f, 0.06f, 0.16f), skinMat);

        // Kafa (biraz küçültüldü: gövdeyle orantılı)
        head = new GameObject("Head").transform;
        head.SetParent(body, false);
        head.localPosition = new Vector3(0f, 1.5f, 0f);
        head.localScale = Vector3.one * 0.84f;
        Part(head, "Face", PrimitiveType.Sphere, new Vector3(0f, 0.16f, 0f), new Vector3(0.60f, 0.56f, 0.58f), skinMat);
        // Yüz seçenekleri: 0 göz maskesi, 1 bandana (ağzı kapatır), 2 açık
        faces = new GameObject[PlayerNetworkSetup.FaceCount];
        faces[0] = Group(head, "Face_Mask");
        Part(faces[0].transform, "Mask", PrimitiveType.Sphere, new Vector3(0f, 0.20f, 0.05f), new Vector3(0.615f, 0.17f, 0.545f), darkMat);
        faces[1] = Group(head, "Face_Bandana");
        Part(faces[1].transform, "Bandana", PrimitiveType.Sphere, new Vector3(0f, 0.04f, 0.04f), new Vector3(0.62f, 0.27f, 0.56f), hatMat);
        Part(faces[1].transform, "Knot", PrimitiveType.Sphere, new Vector3(0f, 0.05f, -0.29f), new Vector3(0.12f, 0.1f, 0.1f), hatMat);
        faces[2] = Group(head, "Face_None");
        Part(head, "EyeL", PrimitiveType.Sphere, new Vector3(-0.13f, 0.20f, 0.265f), new Vector3(0.13f, 0.15f, 0.07f), whiteMat);
        Part(head, "EyeR", PrimitiveType.Sphere, new Vector3(0.13f, 0.20f, 0.265f), new Vector3(0.13f, 0.15f, 0.07f), whiteMat);
        Part(head, "PupilL", PrimitiveType.Sphere, new Vector3(-0.12f, 0.19f, 0.295f), new Vector3(0.06f, 0.08f, 0.03f), darkMat);
        Part(head, "PupilR", PrimitiveType.Sphere, new Vector3(0.12f, 0.19f, 0.295f), new Vector3(0.06f, 0.08f, 0.03f), darkMat);
        Part(head, "CheekL", PrimitiveType.Sphere, new Vector3(-0.2f, 0.07f, 0.22f), new Vector3(0.1f, 0.06f, 0.04f), cheekMat);
        Part(head, "CheekR", PrimitiveType.Sphere, new Vector3(0.2f, 0.07f, 0.22f), new Vector3(0.1f, 0.06f, 0.04f), cheekMat);
        // Şapka seçenekleri: 0 bere, 1 kasket, 2 silindir şapka, 3 yok
        hats = new GameObject[PlayerNetworkSetup.HatCount];
        hats[0] = Group(head, "Hat_Beanie");
        Part(hats[0].transform, "Beanie", PrimitiveType.Sphere, new Vector3(0f, 0.33f, -0.01f), new Vector3(0.63f, 0.42f, 0.61f), hatMat);
        Part(hats[0].transform, "BeanieRim", PrimitiveType.Cylinder, new Vector3(0f, 0.30f, -0.01f), new Vector3(0.64f, 0.04f, 0.62f), darkMat);
        Part(hats[0].transform, "Pompom", PrimitiveType.Sphere, new Vector3(0f, 0.57f, -0.02f), new Vector3(0.16f, 0.16f, 0.16f), whiteMat);
        hats[1] = Group(head, "Hat_Cap");
        Part(hats[1].transform, "Dome", PrimitiveType.Sphere, new Vector3(0f, 0.34f, -0.01f), new Vector3(0.62f, 0.32f, 0.60f), hatMat);
        Part(hats[1].transform, "Visor", PrimitiveType.Cylinder, new Vector3(0f, 0.33f, 0.24f), new Vector3(0.46f, 0.015f, 0.40f), darkMat);
        hats[2] = Group(head, "Hat_Top");
        Part(hats[2].transform, "Brim", PrimitiveType.Cylinder, new Vector3(0f, 0.40f, 0f), new Vector3(0.74f, 0.015f, 0.74f), darkMat);
        Part(hats[2].transform, "Crown", PrimitiveType.Cylinder, new Vector3(0f, 0.58f, 0f), new Vector3(0.44f, 0.18f, 0.44f), darkMat);
        Part(hats[2].transform, "Band", PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), new Vector3(0.455f, 0.035f, 0.455f), hatMat);
        hats[3] = Group(head, "Hat_None");
        Part(hats[3].transform, "Hair", PrimitiveType.Sphere, new Vector3(0f, 0.30f, -0.04f), new Vector3(0.58f, 0.34f, 0.56f), darkMat);
        SetOutfit(0, 0, 0);

        // Kollar: omuz gövdenin üst kenarında, kollar hafifçe dışa açık durur (gövdenin içine gömülmesin),
        // kazak kolu + manşet + eldiven (başparmaklı). Eller artık gövdenin dışında net görünür.
        armL = Arm(body, "ArmL", -1f);
        armR = Arm(body, "ArmR", 1f);

        // Bacaklar (kalça pivotlu) ve ayakkabılar — gövdenin değil kökün çocuğu (zıplamada yerde kalsınlar)
        legL = Leg(root, "LegL", -0.13f);
        legR = Leg(root, "LegR", 0.13f);

        // Sırttaki ganimet çuvalı
        sack = new GameObject("Sack").transform;
        sack.SetParent(body, false);
        sack.localPosition = new Vector3(0f, 1.16f, -0.32f);
        Part(sack, "Bag", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.44f, 0.5f, 0.38f), sackMat);
        Part(sack, "Knot", PrimitiveType.Sphere, new Vector3(0f, 0.30f, 0f), new Vector3(0.16f, 0.14f, 0.16f), sackMat);
        Part(sack, "Rope", PrimitiveType.Cylinder, new Vector3(0f, 0.24f, 0f), new Vector3(0.2f, 0.015f, 0.2f), darkMat);

        // Baygınlık göstergesi: başın üstünde dönen sarı yıldızlar (sadece baygınken görünür)
        starMat = ArtworkLook.MakeLit(new Color(1f, 0.9f, 0.25f));
        stars = new GameObject("KnockStars").transform;
        stars.SetParent(root, false);
        stars.gameObject.SetActive(false);
        for (int i = 0; i < 3; i++)
            Part(stars, "Star" + i, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.11f, starMat);

        // Baygın yüz: normal gözler gizlenir, yerine X gözler ve dışarı sarkan dil gelir
        normalEyes = new GameObject[4];
        string[] eyeNames = { "EyeL", "EyeR", "PupilL", "PupilR" };
        for (int i = 0; i < 4; i++) { Transform e = head.Find(eyeNames[i]); normalEyes[i] = e != null ? e.gameObject : null; }
        xMat = ArtworkLook.MakeLit(new Color(1f, 0.95f, 0.55f));
        xEyes = Group(head, "XEyes");
        xEyes.SetActive(false);
        for (int s = -1; s <= 1; s += 2)
            for (int d = -1; d <= 1; d += 2)
            {
                Part(xEyes.transform, "X", PrimitiveType.Cube, new Vector3(s * 0.13f, 0.20f, 0.325f), new Vector3(0.17f, 0.035f, 0.03f), xMat);
                xEyes.transform.GetChild(xEyes.transform.childCount - 1).localRotation = Quaternion.Euler(0f, 0f, d * 45f);
            }
        tongue = Group(head, "Tongue");
        tongue.SetActive(false);
        Part(tongue.transform, "TongueMesh", PrimitiveType.Sphere, new Vector3(0.04f, -0.05f, 0.285f), new Vector3(0.09f, 0.13f, 0.04f), cheekMat);
    }

    // Baygınlık (PlayerStealth.IsKnockedOutNet): önce sendeler, sonra yüzüstü düşer, kalkarken sallanıp silkinir
    private Transform stars;
    private Material starMat;
    private float knockSmooth, lieRaw, downTime, recoverShake;
    private bool wasKnocked, knockInit;
    private Material xMat;
    private GameObject xEyes, tongue;
    private GameObject[] normalEyes;

    private static GameObject Group(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>Oyuncunun seçtiği kıyafet: şapka, yüz ve gövde yapısı (PlayerNetworkSetup herkese senkronlar).</summary>
    public void SetOutfit(int hat, int face, int bodyBuild)
    {
        if (hats == null || faces == null) return;
        for (int i = 0; i < hats.Length; i++) hats[i].SetActive(i == Mathf.Clamp(hat, 0, hats.Length - 1));
        for (int i = 0; i < faces.Length; i++) faces[i].SetActive(i == Mathf.Clamp(face, 0, faces.Length - 1));

        // Gövde: 0 tombul, 1 ince, 2 iri
        build = Mathf.Clamp(bodyBuild, 0, PlayerNetworkSetup.BuildCount - 1);
        float w = build == 1 ? 0.8f : build == 2 ? 1.18f : 1f;
        torso.localScale = new Vector3(w, build == 2 ? 1.06f : 1f, w);
        if (armL != null) armL.localPosition = ShoulderPos(-1f, w);
        if (armR != null) armR.localPosition = ShoulderPos(1f, w);
        shoulderWidth = w;
    }

    private float shoulderWidth = 1f;

    private static Vector3 ShoulderPos(float side, float w) => new Vector3(side * (0.26f * w + 0.09f), 1.38f, 0f);

    /// <summary>Omuz pivotlu kol: omuz, üst kol, dirsek, ön kol, manşet, eldiven + başparmak (hepsi ayrı görünür).</summary>
    private Transform Arm(Transform parent, string name, float side)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = ShoulderPos(side, 1f);
        Part(t, "Shoulder", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.17f, 0.17f, 0.17f), mainMat);
        Part(t, "UpperArm", PrimitiveType.Capsule, new Vector3(0f, -0.17f, 0f), new Vector3(0.15f, 0.16f, 0.15f), mainMat);
        Part(t, "Elbow", PrimitiveType.Sphere, new Vector3(0f, -0.33f, 0f), new Vector3(0.14f, 0.14f, 0.14f), mainMat);
        Part(t, "Forearm", PrimitiveType.Capsule, new Vector3(0f, -0.46f, 0f), new Vector3(0.13f, 0.14f, 0.13f), mainMat);
        Part(t, "Cuff", PrimitiveType.Cylinder, new Vector3(0f, -0.58f, 0f), new Vector3(0.15f, 0.025f, 0.15f), darkMat);
        Part(t, "Hand", PrimitiveType.Sphere, new Vector3(0f, -0.66f, 0.01f), new Vector3(0.15f, 0.15f, 0.13f), darkMat);
        Part(t, "Thumb", PrimitiveType.Sphere, new Vector3(-side * 0.06f, -0.63f, 0.06f), new Vector3(0.06f, 0.08f, 0.06f), darkMat);
        return t;
    }

    /// <summary>Kalça pivotlu bacak: uyluk, diz, baldır, ayakkabı.</summary>
    private Transform Leg(Transform parent, string name, float x)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = new Vector3(x, 0.82f, 0f);
        Part(t, "Thigh", PrimitiveType.Capsule, new Vector3(0f, -0.2f, 0f), new Vector3(0.2f, 0.21f, 0.2f), darkMat);
        Part(t, "Knee", PrimitiveType.Sphere, new Vector3(0f, -0.4f, 0.01f), new Vector3(0.17f, 0.17f, 0.17f), darkMat);
        Part(t, "Shin", PrimitiveType.Capsule, new Vector3(0f, -0.56f, 0f), new Vector3(0.16f, 0.17f, 0.16f), darkMat);
        Part(t, "Shoe", PrimitiveType.Sphere, new Vector3(0f, -0.76f, 0.06f), new Vector3(0.2f, 0.12f, 0.32f), whiteMat);
        return t;
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
        float squash = 1f - landSquash * 0.1f;
        float drop = crouchSmooth * 0.32f; // eğilince gövde aşağı iner, bacaklar kısalır (ayaklar yerde kalır)
        body.localPosition = new Vector3(0f, bob - drop, 0f);
        body.localScale = new Vector3(1f + (1f - squash) * 0.35f, squash, 1f + (1f - squash) * 0.35f);
        body.localRotation = Quaternion.Euler(move * 7f + crouchSmooth * 14f, 0f, Mathf.Sin(phase) * 2.5f * move);

        // Kafa: baktığı yöne eğilir (diğer oyuncular nereye baktığını görsün)
        float pitch = netSetup != null && netSetup.IsSpawned ? netSetup.LookPitch : 0f;
        head.localRotation = Quaternion.Euler(Mathf.Clamp(pitch, -40f, 40f) * 0.7f, 0f, 0f);

        // Kollar: yürürken ters fazda sallanır; elinde eser varsa öne uzanır
        // Dışa açıklık: gövde genişledikçe biraz artar; eser tutarken kollar öne uzanıp eller önde birleşir
        float reach = -72f * holdSmooth;
        float splay = Mathf.Lerp(9f + shoulderWidth * 5f, 5f, holdSmooth) + crouchSmooth * 6f;
        float inward = holdSmooth * 14f; // eller öne doğru birbirine yaklaşır
        armL.localRotation = Quaternion.Euler(Mathf.Lerp(swing, reach, holdSmooth), inward, -splay);
        armR.localRotation = Quaternion.Euler(Mathf.Lerp(-swing, reach, holdSmooth), -inward, splay);

        legL.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        legR.localRotation = Quaternion.Euler(swing, 0f, 0f);
        float hip = 0.82f - drop;
        legL.localPosition = new Vector3(legL.localPosition.x, hip, 0f);
        legR.localPosition = new Vector3(legR.localPosition.x, hip, 0f);
        legL.localScale = legR.localScale = new Vector3(1f, hip / 0.82f, 1f);

        // Çuval çanta doldukça şişer
        float load = inventory != null && inventory.IsSpawned ? inventory.LoadRatio : 0f;
        float s = Mathf.Lerp(0.7f, 1.35f, load);
        sack.localScale = Vector3.Lerp(sack.localScale, new Vector3(s, s, s), 1f - Mathf.Exp(-6f * dt));
        sack.localRotation = Quaternion.Euler(-swing * 0.12f, 0f, Mathf.Sin(phase) * 4f * move);

        ApplyKnockPose(dt);
    }

    /// <summary>
    /// Baygınlık (tüm oyuncularda görünür, komik): önce sendeleyip dönerek kollarını savurur, sonra 'pof' diye yüzüstü
    /// yaylanarak düşer; gözler X, dil dışarıda, başının etrafında yıldızlar döner. Kalkarken sallanır ve silkinir.
    /// Kök ayak hizasında döner; yatan gövdenin en alt noktası yere (y~0) oturur.
    /// </summary>
    private void ApplyKnockPose(float dt)
    {
        bool down = stealth != null && stealth.IsSpawned && stealth.IsKnockedOutNet;
        const float StaggerTime = 0.55f, FallTime = 0.28f;
        float stag = 0f;
        if (down)
        {
            if (!wasKnocked)
            {
                // Yeni baygınlık: ilk karede zaten yatıyorsa (geç katılan) sendelemeyi atla; kalkarken yeniden vurulduysa mevcut pozdan devam et
                if (!knockInit) { lieRaw = 1f; downTime = 100f; }
                else downTime = StaggerTime + lieRaw * FallTime;
                recoverShake = 0f;
            }
            downTime += dt;
            if (downTime >= StaggerTime) lieRaw = Mathf.MoveTowards(lieRaw, 1f, dt / FallTime);
            if (downTime < StaggerTime) stag = downTime > StaggerTime - 0.1f ? (StaggerTime - downTime) / 0.1f : 1f;
        }
        else
        {
            downTime = 0f;
            float before = lieRaw;
            lieRaw = Mathf.MoveTowards(lieRaw, 0f, dt * 1.8f);
            if (before > 0f && lieRaw <= 0f) recoverShake = 0.45f;
        }
        wasKnocked = down;
        knockInit = stealth != null && stealth.IsSpawned;
        knockSmooth = lieRaw;
        if (recoverShake > 0f) recoverShake -= dt;

        float lie = Mathf.SmoothStep(0f, 1f, lieRaw); // tek, sürekli geçiş (düşüş ve kalkış aynı eğri)
        float rise = lie;
        bool active = down || lieRaw > 0f || recoverShake > 0f;
        if (!active)
        {
            if (stars != null && stars.gameObject.activeSelf) stars.gameObject.SetActive(false);
            if (xEyes != null && xEyes.activeSelf) SetKnockFace(false);
            if (root.localRotation != Quaternion.identity || root.localScale != Vector3.one) { root.localRotation = Quaternion.identity; root.localPosition = new Vector3(0f, -1f, 0f); root.localScale = Vector3.one; }
            return;
        }

        float t = down ? downTime : Time.time;
        // Sendeleme: dönerek öne arkaya sallanır; düşünce yüzüstü (88 derece, X ekseni)
        float yaw = down ? 360f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(downTime / StaggerTime)) : 0f;
        float shake = recoverShake > 0f ? Mathf.Sin(Time.time * 50f) * 14f * (recoverShake / 0.45f) : 0f;
        float wobbleZ = down ? Mathf.Sin(t * 11f) * 13f * stag : Mathf.Sin(Time.time * 9f) * 9f * rise;
        float wobbleX = down ? Mathf.Sin(t * 8f + 1f) * 9f * stag : 0f;
        root.localRotation = Quaternion.Euler(88f * lie + wobbleX, yaw + shake, wobbleZ);

        // Yaylanma: yere çarpınca küçük zıplamalar; sonra yatan gövde yere oturur
        float bounce = 0f;
        if (down && downTime > StaggerTime + FallTime)
        {
            float bt = downTime - StaggerTime - FallTime;
            bounce = Mathf.Abs(Mathf.Sin(bt * 13f)) * 0.16f * Mathf.Exp(-bt * 4.5f);
        }
        float squashHit = down && downTime > StaggerTime + FallTime ? Mathf.Exp(-(downTime - StaggerTime - FallTime) * 9f) * 0.12f : 0f;
        // Yüzüstü yatınca gövde +Z'ye uzanır: merkeze çek; yüz/karın yarıçapı kadar kaldır (yere gömülmesin)
        root.localPosition = new Vector3(0f, -1f + 0.27f * lie + bounce, -0.75f * lie);
        root.localScale = new Vector3(1f + squashHit, 1f - squashHit, 1f + squashHit);

        // Kollar: sendelerken pervane gibi savrulur, yatınca yanlara yayılır
        float flail = Mathf.Sin(t * 22f) * 115f * stag;
        Quaternion aL = Quaternion.Euler(Mathf.Lerp(flail, 170f, lie), 0f, Mathf.Lerp(-50f * stag - 12f, -55f, lie));
        Quaternion aR = Quaternion.Euler(Mathf.Lerp(-flail, 170f, lie), 0f, Mathf.Lerp(50f * stag + 12f, 55f, lie));
        armL.localRotation = down || lieRaw > 0f ? Quaternion.Slerp(armL.localRotation, aL, Mathf.Max(stag, lie)) : armL.localRotation;
        armR.localRotation = down || lieRaw > 0f ? Quaternion.Slerp(armR.localRotation, aR, Mathf.Max(stag, lie)) : armR.localRotation;
        float legSwing = Mathf.Sin(t * 18f) * 30f * stag;
        legL.localRotation = Quaternion.Slerp(legL.localRotation, Quaternion.Euler(-legSwing, 0f, -12f), Mathf.Max(stag, lie));
        legR.localRotation = Quaternion.Slerp(legR.localRotation, Quaternion.Euler(legSwing, 0f, 12f), Mathf.Max(stag, lie));
        head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(-20f * lie + Mathf.Sin(t * 7f) * 12f * stag, Mathf.Sin(t * 6f) * 25f * lie, Mathf.Sin(t * 9f) * 14f * stag), Mathf.Max(stag, lie));

        // Yüz: gözler X, dil dışarıda (sendelerken de sersemlemiş görünür)
        SetKnockFace(down || lieRaw > 0.35f);

        // Yıldızlar: dünya yönüne göre başın üstünde döner (sendeleme sırasında da)
        if (stars != null)
        {
            bool showStars = down || lieRaw > 0.2f;
            if (stars.gameObject.activeSelf != showStars) stars.gameObject.SetActive(showStars);
            if (showStars)
            {
                float grow = down ? Mathf.Clamp01(downTime * 4f) : lieRaw;
                Vector3 headWorld = head.position + Vector3.up * 0.5f;
                for (int i = 0; i < stars.childCount; i++)
                {
                    float a = Time.time * 3.6f + i * 2.094f;
                    Vector3 off = new Vector3(Mathf.Cos(a) * 0.34f, Mathf.Sin(a * 1.9f) * 0.05f, Mathf.Sin(a) * 0.34f);
                    Transform st = stars.GetChild(i);
                    st.position = headWorld + off;
                    st.rotation = Quaternion.identity;
                    st.localScale = Vector3.one * (0.12f * grow);
                }
            }
        }
    }

    private void SetKnockFace(bool on)
    {
        if (xEyes != null) xEyes.SetActive(on);
        if (tongue != null) tongue.SetActive(on);
        if (normalEyes != null) foreach (GameObject g in normalEyes) if (g != null) g.SetActive(!on);
    }

    private void OnDestroy()
    {
        foreach (Material m in new[] { mainMat, hatMat, skinMat, darkMat, whiteMat, sackMat, cheekMat, starMat, xMat })
            if (m != null) Destroy(m);
    }
}
