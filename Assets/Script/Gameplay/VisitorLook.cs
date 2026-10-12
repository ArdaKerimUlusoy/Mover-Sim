using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Müze ziyaretçisinin görünüşü: her biri farklı (ten, saç, şapka, gözlük, çanta, kıyafet renkleri, boy, kilo, etek/pantolon).
/// Görünüş (tohum, ziyaretçi no) ile üretilir: herkesin ekranında aynı kişi aynı görünür.
/// Yürüme animasyonu konumdan hesaplanır. Bir hırsızlık gördüyse başının üstünde "!" çıkar.
/// </summary>
public class VisitorLook : MonoBehaviour
{
    public const byte StateCalm = 0, StateWitness = 1, StateReport = 2;

    private static readonly Color[] Skins =
    {
        new Color(1.00f, 0.84f, 0.70f), new Color(0.96f, 0.76f, 0.60f), new Color(0.86f, 0.64f, 0.48f),
        new Color(0.72f, 0.50f, 0.36f), new Color(0.55f, 0.37f, 0.25f), new Color(0.40f, 0.27f, 0.18f),
    };
    private static readonly Color[] Hairs =
    {
        new Color(0.1f, 0.08f, 0.07f), new Color(0.3f, 0.2f, 0.12f), new Color(0.55f, 0.35f, 0.18f),
        new Color(0.85f, 0.7f, 0.4f), new Color(0.7f, 0.7f, 0.72f), new Color(0.6f, 0.25f, 0.15f),
    };
    private static readonly Color[] Pants =
    {
        new Color(0.2f, 0.25f, 0.4f), new Color(0.15f, 0.15f, 0.17f), new Color(0.45f, 0.38f, 0.3f),
        new Color(0.3f, 0.35f, 0.3f), new Color(0.6f, 0.58f, 0.55f),
    };

    public byte State { get; set; }

    private Transform body, head, armL, armR, legL, legR;
    private readonly List<Material> mats = new List<Material>();
    private Vector3 lastPos;
    private float phase, speedSmooth;
    private float stateSince;
    private byte shownState;
    private GUIStyle iconStyle;

    public void Build(int seed, int id)
    {
        var rng = new System.Random(seed * 31 + id * 7919 + 17);
        float R() => (float)rng.NextDouble();

        Color skin = Skins[rng.Next(Skins.Length)];
        Color hairC = Hairs[rng.Next(Hairs.Length)];
        Color shirt = Color.HSVToRGB(R(), Mathf.Lerp(0.35f, 0.8f, R()), Mathf.Lerp(0.55f, 0.95f, R()));
        Color pantsC = Pants[rng.Next(Pants.Length)];
        Color shoe = rng.Next(2) == 0 ? new Color(0.12f, 0.1f, 0.1f) : new Color(0.9f, 0.9f, 0.88f);
        float height = Mathf.Lerp(0.88f, 1.08f, R());
        float girth = Mathf.Lerp(0.85f, 1.2f, R());
        bool skirt = rng.Next(4) == 0;
        int hairStyle = rng.Next(4);
        int hat = rng.Next(10); // 0-5 yok, 6 kasket, 7 hasır şapka, 8 bere, 9 yok
        bool glasses = rng.Next(10) < 3;
        bool bag = rng.Next(10) < 3;

        Material mSkin = M(skin), mHair = M(hairC), mShirt = M(shirt), mPants = M(pantsC), mShoe = M(shoe);
        Material mWhite = M(Color.white), mBlack = M(new Color(0.08f, 0.08f, 0.08f));

        Transform root = new GameObject("VisitorAvatar").transform;
        root.SetParent(transform, false);
        root.localScale = new Vector3(1f, height, 1f);

        body = new GameObject("Body").transform;
        body.SetParent(root, false);
        Part(body, "Torso", PrimitiveType.Sphere, new Vector3(0f, 1.12f, 0f), new Vector3(0.56f * girth, 0.7f, 0.42f * girth), mShirt);
        Part(body, "Hips", PrimitiveType.Sphere, new Vector3(0f, 0.86f, 0f), new Vector3(0.48f * girth, 0.24f, 0.36f * girth), skirt ? mShirt : mPants);
        if (skirt) Part(body, "Skirt", PrimitiveType.Cylinder, new Vector3(0f, 0.68f, 0f), new Vector3(0.6f * girth, 0.2f, 0.5f * girth), mPants);
        Part(body, "Neck", PrimitiveType.Cylinder, new Vector3(0f, 1.48f, 0f), new Vector3(0.15f, 0.06f, 0.15f), mSkin);
        if (bag)
        {
            Part(body, "Bag", PrimitiveType.Cube, new Vector3(0.32f * girth, 0.9f, 0f), new Vector3(0.12f, 0.3f, 0.3f), M(new Color(0.45f, 0.28f, 0.15f)));
            Part(body, "Strap", PrimitiveType.Cube, new Vector3(0f, 1.2f, 0f), new Vector3(0.6f * girth, 0.04f, 0.46f * girth), M(new Color(0.35f, 0.2f, 0.1f))).localRotation = Quaternion.Euler(0f, 0f, 35f);
        }

        head = new GameObject("Head").transform;
        head.SetParent(body, false);
        head.localPosition = new Vector3(0f, 1.52f, 0f);
        head.localScale = Vector3.one * 0.84f;
        Part(head, "Face", PrimitiveType.Sphere, new Vector3(0f, 0.15f, 0f), new Vector3(0.54f, 0.54f, 0.52f), mSkin);
        Part(head, "EyeL", PrimitiveType.Sphere, new Vector3(-0.1f, 0.18f, 0.23f), new Vector3(0.08f, 0.1f, 0.05f), mWhite);
        Part(head, "EyeR", PrimitiveType.Sphere, new Vector3(0.1f, 0.18f, 0.23f), new Vector3(0.08f, 0.1f, 0.05f), mWhite);
        Part(head, "PupilL", PrimitiveType.Sphere, new Vector3(-0.1f, 0.175f, 0.255f), new Vector3(0.04f, 0.05f, 0.03f), mBlack);
        Part(head, "PupilR", PrimitiveType.Sphere, new Vector3(0.1f, 0.175f, 0.255f), new Vector3(0.04f, 0.05f, 0.03f), mBlack);
        Part(head, "Nose", PrimitiveType.Sphere, new Vector3(0f, 0.11f, 0.27f), new Vector3(0.07f, 0.07f, 0.06f), mSkin);
        switch (hairStyle)
        {
            case 0: Part(head, "Hair", PrimitiveType.Sphere, new Vector3(0f, 0.25f, -0.04f), new Vector3(0.56f, 0.38f, 0.54f), mHair); break;
            case 1:
                Part(head, "Hair", PrimitiveType.Sphere, new Vector3(0f, 0.25f, -0.04f), new Vector3(0.58f, 0.4f, 0.56f), mHair);
                Part(head, "LongHair", PrimitiveType.Capsule, new Vector3(0f, -0.05f, -0.18f), new Vector3(0.5f, 0.32f, 0.2f), mHair);
                break;
            case 2:
                Part(head, "Hair", PrimitiveType.Sphere, new Vector3(0f, 0.25f, -0.04f), new Vector3(0.56f, 0.36f, 0.54f), mHair);
                Part(head, "Bun", PrimitiveType.Sphere, new Vector3(0f, 0.45f, -0.12f), Vector3.one * 0.2f, mHair);
                break;
        }
        if (glasses)
        {
            Part(head, "GlassL", PrimitiveType.Cylinder, new Vector3(-0.1f, 0.18f, 0.27f), new Vector3(0.12f, 0.01f, 0.12f), mBlack).localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part(head, "GlassR", PrimitiveType.Cylinder, new Vector3(0.1f, 0.18f, 0.27f), new Vector3(0.12f, 0.01f, 0.12f), mBlack).localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        if (hat == 6)
        {
            Material mh = M(Color.HSVToRGB(R(), 0.6f, 0.7f));
            Part(head, "CapDome", PrimitiveType.Sphere, new Vector3(0f, 0.3f, -0.01f), new Vector3(0.56f, 0.3f, 0.54f), mh);
            Part(head, "CapVisor", PrimitiveType.Cylinder, new Vector3(0f, 0.3f, 0.22f), new Vector3(0.4f, 0.015f, 0.34f), mh);
        }
        else if (hat == 7)
        {
            Material straw = M(new Color(0.9f, 0.8f, 0.5f));
            Part(head, "SunBrim", PrimitiveType.Cylinder, new Vector3(0f, 0.33f, 0f), new Vector3(0.95f, 0.015f, 0.95f), straw);
            Part(head, "SunCrown", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f), new Vector3(0.48f, 0.1f, 0.48f), straw);
        }
        else if (hat == 8)
        {
            Part(head, "Beret", PrimitiveType.Sphere, new Vector3(0.04f, 0.38f, -0.02f), new Vector3(0.6f, 0.18f, 0.58f), M(Color.HSVToRGB(R(), 0.5f, 0.45f)));
        }

        armL = Arm(body, -1f, girth, mShirt, mSkin);
        armR = Arm(body, 1f, girth, mShirt, mSkin);
        legL = Leg(root, -0.13f * girth, skirt ? mSkin : mPants, mShoe);
        legR = Leg(root, 0.13f * girth, skirt ? mSkin : mPants, mShoe);

        lastPos = transform.position;
    }

    private Transform Arm(Transform parent, float side, float girth, Material sleeve, Material skin)
    {
        Transform t = new GameObject(side < 0 ? "ArmL" : "ArmR").transform;
        t.SetParent(parent, false);
        t.localPosition = new Vector3(side * (0.26f * girth + 0.09f), 1.38f, 0f);
        Part(t, "Shoulder", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.16f, sleeve);
        Part(t, "UpperArm", PrimitiveType.Capsule, new Vector3(0f, -0.17f, 0f), new Vector3(0.14f, 0.16f, 0.14f), sleeve);
        Part(t, "Forearm", PrimitiveType.Capsule, new Vector3(0f, -0.45f, 0f), new Vector3(0.12f, 0.15f, 0.12f), skin);
        Part(t, "Hand", PrimitiveType.Sphere, new Vector3(0f, -0.64f, 0.01f), new Vector3(0.13f, 0.13f, 0.12f), skin);
        return t;
    }

    private Transform Leg(Transform parent, float x, Material pants, Material shoe)
    {
        Transform t = new GameObject("Leg").transform;
        t.SetParent(parent, false);
        t.localPosition = new Vector3(x, 0.82f, 0f);
        Part(t, "Thigh", PrimitiveType.Capsule, new Vector3(0f, -0.2f, 0f), new Vector3(0.19f, 0.21f, 0.19f), pants);
        Part(t, "Shin", PrimitiveType.Capsule, new Vector3(0f, -0.55f, 0f), new Vector3(0.15f, 0.18f, 0.15f), pants);
        Part(t, "Shoe", PrimitiveType.Sphere, new Vector3(0f, -0.76f, 0.06f), new Vector3(0.18f, 0.11f, 0.3f), shoe);
        return t;
    }

    private Material M(Color c)
    {
        Material m = ArtworkLook.MakeLit(c);
        mats.Add(m);
        return m;
    }

    private static readonly Dictionary<PrimitiveType, Mesh> meshCache = new Dictionary<PrimitiveType, Mesh>();

    private static Transform Part(Transform parent, string n, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
    {
        if (!meshCache.TryGetValue(type, out Mesh mesh) || mesh == null)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            temp.SetActive(false);
            mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
            meshCache[type] = mesh;
        }
        GameObject go = new GameObject(n);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.On;
        return go.transform;
    }

    private void Update()
    {
        if (body == null) return;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = transform.position - lastPos;
        lastPos = transform.position;
        float speed = new Vector3(delta.x, 0f, delta.z).magnitude / dt;
        if (speed > 20f) speed = 0f;
        speedSmooth = Mathf.Lerp(speedSmooth, speed, 1f - Mathf.Exp(-8f * dt));

        float move = Mathf.Clamp01(speedSmooth / 4f);
        phase += speedSmooth * 2.3f * dt;
        float swing = Mathf.Sin(phase) * Mathf.Lerp(28f, 50f, Mathf.Clamp01(speedSmooth / 4f - 0.3f)) * Mathf.Clamp01(speedSmooth / 1f);
        body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(phase)) * 0.04f * move, 0f);
        body.localRotation = Quaternion.Euler(move * 8f, 0f, 0f);

        bool shocked = State == StateWitness;
        armL.localRotation = Quaternion.Euler(shocked ? -150f : swing, 0f, shocked ? -25f : -8f);
        armR.localRotation = Quaternion.Euler(shocked ? -150f : -swing, 0f, shocked ? 25f : 8f);
        legL.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        legR.localRotation = Quaternion.Euler(swing, 0f, 0f);
        head.localRotation = Quaternion.Euler(shocked ? -10f : Mathf.Sin(Time.time * 0.5f + phase) * 4f, 0f, 0f);

        if (State != shownState) { shownState = State; stateSince = Time.unscaledTime; }
    }

    private void OnGUI()
    {
        if (State == StateCalm || SessionMenuUI.IsPaused) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.25f);
        if (sp.z <= 0f || sp.z > 30f) return;
        if (iconStyle == null) iconStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        float t = Time.unscaledTime - stateSince;
        float pop = t < 0.25f ? Mathf.Lerp(1.8f, 1f, t / 0.25f) : 1f;
        iconStyle.fontSize = Mathf.RoundToInt(38f * pop);
        Rect r = new Rect(sp.x - 30f, Screen.height - sp.y - 40f, 60f, 60f);
        iconStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), "!", iconStyle);
        iconStyle.normal.textColor = State == StateReport ? new Color(1f, 0.55f, 0.15f) : new Color(1f, 0.9f, 0.3f);
        GUI.Label(r, "!", iconStyle);
    }

    private void OnDestroy()
    {
        foreach (Material m in mats) if (m != null) Destroy(m);
    }
}
