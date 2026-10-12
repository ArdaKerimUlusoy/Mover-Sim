using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MuseumLayout'un DIŞ kısmı: temanın gerçek müzesine benzeyen cephe ve çatı, sokak, yer rengi, günün saati.
///   Paris      → Louvre: Cour Napoléon — U biçimli 3 katlı saray, köşkler, mansart çatılar, örgülü cam piramit ve havuzlar
///   Londra     → British Museum: 8 sütunlu Yunan revağı ve büyük üçgen alınlık
///   Amsterdam  → Rijksmuseum: kırmızı tuğla, sivri çatılı iki kule, ortada kemerli geçit
///   Floransa   → Uffizi revakları + Duomo'nun kiremit kubbesi
///   Madrid     → Prado: tuğla ve granit uzun cephe, dor sütunlu giriş, önünde heykel
///   İstanbul   → Topkapı Bab-üs Selam (iki konik kuleli kapı) + Ayasofya kubbesi ve minareler
///   Kahire     → Mısır Müzesi (pembe cephe, kemerli giriş) + dikilitaşlar, sfenks, piramit
///   Tokyo      → Tokyo Ulusal Müzesi: büyük kiremit kırma çatı ("imparatorluk tacı"), kırmızı torii, taş fenerler
/// </summary>
public partial class MuseumLayout
{
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Rect> extraBlocked = new List<Rect>(); // temaya özel: sokak süslerinin konmayacağı yerler

    private void BuildExterior(System.Random rng, MuseumTheme th, int themeIndex)
    {
        foreach (Mesh m in meshes) if (m != null) Destroy(m);
        meshes.Clear();
        extraBlocked.Clear();
        BuildFacade(themeIndex % 8, th);
        BuildStreetProps(rng, themeIndex);
        TintGround(themeIndex);
    }

    // =====================================================================
    //  CEPHELER (müze batı duvarı x = -6, sokak tarafı -x; giriş z = 0)
    // =====================================================================

    private void BuildFacade(int style, MuseumTheme th)
    {
        Transform f = NewGroup(exterior, "Facade");
        Color stone = new Color(0.88f, 0.86f, 0.8f);

        // Hepsinde: basamaklar ve giriş tabelası
        Decor(f, "Facade_Steps1", PrimitiveType.Cube, new Vector3(-7.3f, 0.06f, 0f), 0f, new Vector3(2.4f, 0.12f, 7f), stone * 0.95f);
        Decor(f, "Facade_Steps2", PrimitiveType.Cube, new Vector3(-6.8f, 0.16f, 0f), 0f, new Vector3(1.4f, 0.1f, 5f), stone * 0.95f);

        switch (style)
        {
            case 0: Louvre(f, th); break;
            case 1: BritishMuseum(f, th); break;
            case 2: Rijksmuseum(f, th); break;
            case 3: Uffizi(f, th); break;
            case 4: Prado(f, th); break;
            case 5: Topkapi(f, th); break;
            case 6: EgyptianMuseum(f, th); break;
            default: TokyoMuseum(f, th); break;
        }
    }

    /// <summary>Batı duvarının dış yüzüne kaplama (girişi açık bırakır).</summary>
    private void Cladding(Transform f, Color c, float height = 4f)
    {
        Decor(f, "Facade_CladA", PrimitiveType.Cube, new Vector3(-6.21f, height * 0.5f, -8.7f), 0f, new Vector3(0.1f, height, 14.9f), c);
        Decor(f, "Facade_CladB", PrimitiveType.Cube, new Vector3(-6.21f, height * 0.5f, 7.7f), 0f, new Vector3(0.1f, height, 12.9f), c);
        Decor(f, "Facade_CladTop", PrimitiveType.Cube, new Vector3(-6.21f, 2.8f + (height - 2.8f) * 0.5f, 0f), 0f, new Vector3(0.1f, height - 2.8f, 2.6f), c);
    }

    /// <summary>Cephe boyunca pencere sırası (girişin yanını boş bırakır).</summary>
    private void Windows(Transform f, float y, float h, float w, float spacing, Color frame, Color glass, float x = -6.28f)
    {
        for (float z = -14.5f; z <= 13f; z += spacing)
        {
            if (Mathf.Abs(z) < 3f) continue;
            Decor(f, "Facade_WindowFrame", PrimitiveType.Cube, new Vector3(x, y, z), 0f, new Vector3(0.06f, h + 0.16f, w + 0.16f), frame);
            Decor(f, "Facade_Window", PrimitiveType.Cube, new Vector3(x - 0.02f, y, z), 0f, new Vector3(0.05f, h, w), glass);
        }
    }

    // ---------------- Paris: Louvre ----------------
    // Cour Napoléon: arkada Sully kanadı (müzenin batı cephesi, ortada saat köşkü), iki yanda öne uzanan
    // Richelieu / Denon kanatları ve uçlarında köşkler; ortada çelik örgülü büyük cam piramit,
    // iki küçük piramit ve üçgen havuzlar. 3 katlı taş cephe: rustik zemin kat kemerleri, pilastrlar,
    // alınlıklı pencereler, korniş + korkuluk, arduvaz mansart çatılar, çatı pencereleri, kare kubbeli köşkler.
    private void Louvre(Transform f, MuseumTheme th)
    {
        Color stone = new Color(0.87f, 0.81f, 0.68f), dark = new Color(0.66f, 0.6f, 0.5f), slate = new Color(0.27f, 0.31f, 0.37f);
        Color gold = new Color(0.86f, 0.7f, 0.32f), glassC = new Color(0.13f, 0.16f, 0.21f), frame = new Color(0.95f, 0.92f, 0.84f);
        const float H = 11f, Hp = 13.2f;

        // Sokak süsleri (ağaç, bank, araba) kanatların / havuzların içine düşmesin
        extraBlocked.Add(Rect.MinMaxRect(-28f, 12f, -5f, 23f));
        extraBlocked.Add(Rect.MinMaxRect(-28f, -31f, -5f, -14f));
        extraBlocked.Add(Rect.MinMaxRect(-29f, -12.5f, -6f, 12.5f));

        // Avlu zemini (açık renk taş döşeme)
        Decor(f, "Facade_Paving", PrimitiveType.Cube, new Vector3(-17.5f, 0.012f, -1f), 0f, new Vector3(22.6f, 0.024f, 27.4f), new Color(0.8f, 0.77f, 0.7f));

        // ---- 1) Sully kanadı = müzenin batı cephesi (girişi açık bırakır) + çatının üstünde yükselen kütle
        Cladding(f, stone, H);
        Decor(f, "Facade_UpperBlock", PrimitiveType.Cube, new Vector3(-1.1f, (WallH + 0.24f + H) * 0.5f, -1f), 0f, new Vector3(10.2f, H - WallH - 0.24f, 30.4f), stone);
        foreach (float z in new[] { -16.3f, 14.3f })
            Decor(f, "Facade_SideClad", PrimitiveType.Cube, new Vector3(-1.1f, H * 0.5f, z), 0f, new Vector3(10.2f, H, 0.1f), stone);
        Mansard(f, new Vector3(-1.1f, H, -1f), 10.2f, 30.4f, 3.2f, slate, gold, stone, glassC, Vector3.left);
        PalaceFace(f, new Vector3(-6.26f, 0f, -9.55f), Vector3.left, 10.1f, H, stone, dark, frame, glassC, true, 0f);
        PalaceFace(f, new Vector3(-6.26f, 0f, 8.55f), Vector3.left, 8.1f, H, stone, dark, frame, glassC, true, 0f);

        // ---- 2) Ortadaki saat köşkü (Pavillon de l'Horloge): kapının iki yanında ayaklar, üstte kare kubbe
        float hc = H + 3.4f;
        foreach (float z in new[] { -2.95f, 2.95f })
            Solid(f, "Facade_PavilionPier", new Vector3(-6.8f, hc * 0.5f, z), new Vector3(1.2f, hc, 3.1f), stone);
        Decor(f, "Facade_PavilionLintel", PrimitiveType.Cube, new Vector3(-6.8f, (2.9f + hc) * 0.5f, 0f), 0f, new Vector3(1.2f, hc - 2.9f, 2.8f), stone);
        Decor(f, "Facade_PavilionUpper", PrimitiveType.Cube, new Vector3(-2.1f, (WallH + 0.24f + hc) * 0.5f, 0f), 0f, new Vector3(8.2f, hc - WallH - 0.24f, 9f), stone);
        PalaceFace(f, new Vector3(-7.42f, 0f, 0f), Vector3.left, 9f, hc, stone, dark, frame, glassC, true, 1.6f);
        foreach (float z in new[] { -4.05f, -1.7f, 1.7f, 4.05f })
            Pillar(f, new Vector3(-7.75f, 3.6f, z), 0.2f, H - 4.2f, Color.Lerp(stone, Color.white, 0.2f), false);
        Decor(f, "Facade_DoorFrameL", PrimitiveType.Cube, new Vector3(-7.45f, 1.45f, -1.48f), 0f, new Vector3(0.1f, 2.9f, 0.16f), gold);
        Decor(f, "Facade_DoorFrameR", PrimitiveType.Cube, new Vector3(-7.45f, 1.45f, 1.48f), 0f, new Vector3(0.1f, 2.9f, 0.16f), gold);
        Decor(f, "Facade_DoorFrameT", PrimitiveType.Cube, new Vector3(-7.45f, 2.95f, 0f), 0f, new Vector3(0.1f, 0.16f, 3.1f), gold);
        Prism(f, "Facade_Pediment", new Vector3(-7.4f, hc, 0f), 0.6f, 5.2f, 1.9f, 0f, stone);
        Decor(f, "Facade_ClockRim", PrimitiveType.Cylinder, new Vector3(-7.45f, 10.9f, 0f), 0f, new Vector3(1.7f, 0.02f, 1.7f), gold).localRotation = Quaternion.Euler(0f, 0f, 90f);
        Decor(f, "Facade_ClockFace", PrimitiveType.Cylinder, new Vector3(-7.48f, 10.9f, 0f), 0f, new Vector3(1.45f, 0.02f, 1.45f), frame).localRotation = Quaternion.Euler(0f, 0f, 90f);
        Decor(f, "Facade_ClockHand", PrimitiveType.Cube, new Vector3(-7.51f, 11.1f, 0f), 0f, new Vector3(0.03f, 0.42f, 0.06f), new Color(0.1f, 0.1f, 0.1f));
        Decor(f, "Facade_ClockHand2", PrimitiveType.Cube, new Vector3(-7.51f, 10.9f, 0.14f), 0f, new Vector3(0.03f, 0.06f, 0.3f), new Color(0.1f, 0.1f, 0.1f));
        PavilionRoof(f, new Vector3(-2.7f, hc, 0f), 9.4f, 9f, 6.2f, slate, gold);

        // ---- 3) Köşe köşkleri (kanatlarla Sully kanadının birleştiği yer)
        foreach (int side in new[] { 1, -1 })
        {
            float zIn = side > 0 ? 12.6f : -14.6f;       // avluya bakan iç yüz
            float zWall = side > 0 ? 14.35f : -16.35f;   // müze duvarının dışı
            float zOut = side > 0 ? 22f : -30f;
            Solid(f, "Facade_CornerPavilion", new Vector3(-5.65f, Hp * 0.5f, (zWall + zOut) * 0.5f), new Vector3(6.3f, Hp, Mathf.Abs(zOut - zWall)), stone);
            Solid(f, "Facade_CornerFront", new Vector3(-7.5f, Hp * 0.5f, (zIn + zWall) * 0.5f), new Vector3(2.6f, Hp, Mathf.Abs(zWall - zIn)), stone);
            Decor(f, "Facade_CornerOverRoof", PrimitiveType.Cube, new Vector3(-4.35f, (WallH + 0.24f + Hp) * 0.5f, (zIn + zWall) * 0.5f), 0f, new Vector3(3.7f, Hp - WallH - 0.24f, Mathf.Abs(zWall - zIn)), stone);
            // Batı yüzünün avludan görünen kısmı: kanadın başladığı yere kadar (güneyde avlu daha uzun)
            float zFaceA = zIn, zFaceB = side > 0 ? 16f : -24f;
            PalaceFace(f, new Vector3(-8.86f, 0f, (zFaceA + zFaceB) * 0.5f), Vector3.left, Mathf.Abs(zFaceB - zFaceA), Hp, stone, dark, frame, glassC, true, 0f);
            PalaceFace(f, new Vector3(-7.5f, 0f, zIn - side * 0.06f), side > 0 ? Vector3.back : Vector3.forward, 2.6f, Hp, stone, dark, frame, glassC, true, 0f);
            PavilionRoof(f, new Vector3(-5.65f, Hp, (zIn + zOut) * 0.5f), 6.6f, Mathf.Abs(zOut - zIn), 5.2f, slate, gold);

            // ---- 4) Öne uzanan kanat (Richelieu / Denon) ve ucundaki köşk
            float zA = side > 0 ? 16f : -30f, zB = side > 0 ? 22f : -24f;    // kanat gövdesi
            float zCourt = side > 0 ? zA : zB;                               // avluya bakan yüz
            Vector3 courtN = side > 0 ? Vector3.back : Vector3.forward;
            Solid(f, "Facade_Wing", new Vector3(-15.4f, H * 0.5f, (zA + zB) * 0.5f), new Vector3(13.2f, H, 6f), stone);
            PalaceFace(f, new Vector3(-14.9f, 0f, zCourt - side * 0.06f), courtN, 12.2f, H, stone, dark, frame, glassC, true, 0f);
            Mansard(f, new Vector3(-15.4f, H, (zA + zB) * 0.5f), 13.2f, 6f, 3f, slate, gold, stone, glassC, courtN);

            float zP0 = side > 0 ? 15.2f : -30.8f, zP1 = side > 0 ? 22.8f : -23.2f;
            Solid(f, "Facade_EndPavilion", new Vector3(-24f, Hp * 0.5f, (zP0 + zP1) * 0.5f), new Vector3(6f, Hp, 7.6f), stone);
            PalaceFace(f, new Vector3(-27.06f, 0f, (zP0 + zP1) * 0.5f), Vector3.left, 7.6f, Hp, stone, dark, frame, glassC, true, 0f);
            PalaceFace(f, new Vector3(-24f, 0f, (side > 0 ? zP0 : zP1) - side * 0.06f), courtN, 6f, Hp, stone, dark, frame, glassC, true, 0f);
            Prism(f, "Facade_EndPediment", new Vector3(-24f, Hp, (side > 0 ? zP0 : zP1) - side * 0.2f), 0.5f, 4.6f, 1.6f, 0f, stone).localRotation = Quaternion.Euler(0f, 90f, 0f);
            Prism(f, "Facade_EndPedimentW", new Vector3(-27.2f, Hp, (zP0 + zP1) * 0.5f), 0.5f, 5f, 1.7f, 0f, stone);
            PavilionRoof(f, new Vector3(-24f, Hp, (zP0 + zP1) * 0.5f), 6.3f, 7.9f, 5f, slate, gold);
        }

        // ---- 5) Cam piramit: çelik baklava örgüsü, kalın kenarlar; iki küçük piramit; üçgen havuzlar ve fıskiyeler
        Vector3 pc = new Vector3(-14.5f, 0.03f, 0f);
        const float pb = 12f, ph = 7.4f;
        MeshPart(f, "Facade_GlassPyramid", PyramidMesh(pb, pb, ph, 0f, 0f), pc, Glass(new Color(0.58f, 0.74f, 0.86f, 0.42f)));
        Color steel = new Color(0.2f, 0.22f, 0.25f);
        Vector3 apex = pc + Vector3.up * ph;
        Vector3[] corners =
        {
            pc + new Vector3(-pb / 2f, 0f, -pb / 2f), pc + new Vector3(pb / 2f, 0f, -pb / 2f),
            pc + new Vector3(pb / 2f, 0f, pb / 2f), pc + new Vector3(-pb / 2f, 0f, pb / 2f),
        };
        const int grid = 7;
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % 4];
            Beam(f, "Facade_PyramidEdge", a, apex, 0.14f, steel);
            Beam(f, "Facade_PyramidBase", a, b, 0.16f, steel);
            for (int k = 1; k < grid; k++)
            {
                float u = k / (float)grid;
                Vector3 sp = Vector3.Lerp(a, b, u);
                Beam(f, "Facade_PyramidLattice", sp, b * u + apex * (1f - u), 0.045f, steel);
                Beam(f, "Facade_PyramidLattice", sp, a * (1f - u) + apex * u, 0.045f, steel);
            }
        }
        foreach (float z in new[] { -10f, 10f })
        {
            Vector3 sc = new Vector3(-14.5f, 0.03f, z);
            MeshPart(f, "Facade_SmallPyramid", PyramidMesh(3f, 3f, 1.85f, 0f, 0f), sc, Glass(new Color(0.58f, 0.74f, 0.86f, 0.45f)));
            Vector3 sa = sc + Vector3.up * 1.85f;
            foreach (Vector3 o in new[] { new Vector3(-1.5f, 0f, -1.5f), new Vector3(1.5f, 0f, -1.5f), new Vector3(1.5f, 0f, 1.5f), new Vector3(-1.5f, 0f, 1.5f) })
                Beam(f, "Facade_PyramidEdge", sc + o, sa, 0.07f, steel);
        }
        foreach (int side in new[] { 1, -1 })
        {
            TriPool(f, new Vector2(-21.2f, 3.4f * side), new Vector2(-27.6f, 3.4f * side), new Vector2(-21.2f, 9.6f * side));
            TriPool(f, new Vector2(-12f, 7.2f * side), new Vector2(-7.7f, 7.2f * side), new Vector2(-7.7f, 11.8f * side));
        }

        // ---- 6) Gece aydınlatması: piramidin içi ve cephe sıcak ışıkla yanar
        FacadeLight(f, new Vector3(-14.5f, 1.4f, 0f), new Color(1f, 0.86f, 0.6f), 3.5f, 10f);
        FacadeLight(f, new Vector3(-9.6f, 0.8f, -9f), new Color(1f, 0.85f, 0.62f), 3f, 11f);
        FacadeLight(f, new Vector3(-9.6f, 0.8f, 8f), new Color(1f, 0.85f, 0.62f), 3f, 11f);
        FacadeLight(f, new Vector3(-16f, 0.8f, 14f), new Color(1f, 0.85f, 0.62f), 2.5f, 11f);
        FacadeLight(f, new Vector3(-16f, 0.8f, -22f), new Color(1f, 0.85f, 0.62f), 2.5f, 11f);
    }

    /// <summary>
    /// Saray cephesi (3 kat): rustik zemin kat (kemerli açıklıklar ya da pencereler), alınlıklı büyük pencereli
    /// soylu kat, üst kat pencereleri, pilastrlar, kat silmeleri, korniş ve korkuluk.
    /// c = cephe yüzeyinin ortası (y = 0), n = dışa bakan yön, len = cephe genişliği.
    /// skipGround: ortadaki bu yarıçap içinde zemin kat açıklığı çizilmez (kapı için).
    /// </summary>
    private void PalaceFace(Transform f, Vector3 c, Vector3 n, float len, float h, Color stone, Color dark, Color frame, Color glass, bool arches, float skipGround)
    {
        Quaternion q = Quaternion.LookRotation(n);
        Vector3 right = q * Vector3.right;
        Color light = Color.Lerp(stone, Color.white, 0.18f);

        Transform P(string _nm, PrimitiveType _type, float _along, float _y, Vector3 _size, Color _col, float _roll = 0f)
        {
            Transform _tr = Decor(f, _nm, _type, c + right * _along + Vector3.up * _y + n * (_size.z * 0.5f), 0f, _size, _col);
            _tr.localRotation = q * Quaternion.Euler(0f, 0f, _roll);
            return _tr;
        }
        Transform Disc(string _nm, float _along, float _y, float _d, Color _col, float _outward)
        {
            Transform _tr = Decor(f, _nm, PrimitiveType.Cylinder, c + right * _along + Vector3.up * _y + n * _outward, 0f, new Vector3(_d, 0.015f, _d), _col);
            _tr.localRotation = q * Quaternion.Euler(90f, 0f, 0f);
            return _tr;
        }

        // Yatay hatlar
        foreach (float y in new[] { 0.9f, 1.8f, 2.7f }) P("Facade_Rustic", PrimitiveType.Cube, 0f, y, new Vector3(len, 0.05f, 0.05f), dark);
        P("Facade_Plinth", PrimitiveType.Cube, 0f, 0.2f, new Vector3(len, 0.4f, 0.12f), dark);
        P("Facade_StringCourse", PrimitiveType.Cube, 0f, 3.6f, new Vector3(len, 0.24f, 0.3f), light);
        P("Facade_Band", PrimitiveType.Cube, 0f, 7.25f, new Vector3(len, 0.16f, 0.2f), light);
        P("Facade_Cornice", PrimitiveType.Cube, 0f, h - 0.3f, new Vector3(len + 0.3f, 0.45f, 0.55f), light);
        P("Facade_Frieze", PrimitiveType.Cube, 0f, h - 0.75f, new Vector3(len, 0.35f, 0.12f), stone);
        P("Facade_BalustradeBase", PrimitiveType.Cube, 0f, h + 0.1f, new Vector3(len, 0.14f, 0.32f), light);
        P("Facade_BalustradeRail", PrimitiveType.Cube, 0f, h + 0.75f, new Vector3(len, 0.12f, 0.32f), light);
        P("Facade_BalustradeShadow", PrimitiveType.Cube, 0f, h + 0.43f, new Vector3(len - 0.1f, 0.52f, 0.14f), dark);

        int bays = Mathf.Max(1, Mathf.RoundToInt(len / 2.6f));
        float bay = len / bays;
        for (int k = 0; k < bays; k++)
        {
            float a = -len / 2f + bay * (k + 0.5f);

            // Zemin kat
            if (Mathf.Abs(a) >= skipGround)
            {
                if (arches)
                {
                    P("Facade_ArchOpening", PrimitiveType.Cube, a, 1.15f, new Vector3(1.3f, 2.3f, 0.04f), glass);
                    Disc("Facade_ArchTop", a, 2.3f, 1.3f, glass, 0.02f);
                    Disc("Facade_ArchRing", a, 2.3f, 1.55f, light, 0.01f);
                    P("Facade_Keystone", PrimitiveType.Cube, a, 3.08f, new Vector3(0.26f, 0.36f, 0.1f), light);
                }
                else
                {
                    P("Facade_WindowFrame", PrimitiveType.Cube, a, 1.7f, new Vector3(1.2f, 1.9f, 0.06f), frame);
                    P("Facade_Window", PrimitiveType.Cube, a, 1.7f, new Vector3(0.96f, 1.66f, 0.08f), glass);
                }
            }

            // Soylu kat: uzun pencere + alınlık (üçgen / düz sırayla)
            P("Facade_Sill", PrimitiveType.Cube, a, 4.0f, new Vector3(1.6f, 0.12f, 0.26f), light);
            P("Facade_WindowFrame", PrimitiveType.Cube, a, 5.3f, new Vector3(1.36f, 2.5f, 0.06f), frame);
            P("Facade_Window", PrimitiveType.Cube, a, 5.3f, new Vector3(1.1f, 2.24f, 0.08f), glass);
            P("Facade_Mullion", PrimitiveType.Cube, a, 5.3f, new Vector3(0.06f, 2.24f, 0.1f), frame);
            P("Facade_WindowCap", PrimitiveType.Cube, a, 6.65f, new Vector3(1.6f, 0.12f, 0.28f), light);
            if (k % 2 == 0)
            {
                P("Facade_PedimentL", PrimitiveType.Cube, a - 0.4f, 6.95f, new Vector3(0.92f, 0.1f, 0.26f), light, 18f);
                P("Facade_PedimentR", PrimitiveType.Cube, a + 0.4f, 6.95f, new Vector3(0.92f, 0.1f, 0.26f), light, -18f);
            }
            else P("Facade_WindowCrown", PrimitiveType.Cube, a, 6.85f, new Vector3(1.3f, 0.2f, 0.24f), light);

            // Üst kat
            P("Facade_Sill", PrimitiveType.Cube, a, 7.7f, new Vector3(1.3f, 0.1f, 0.22f), light);
            P("Facade_WindowFrame", PrimitiveType.Cube, a, 8.7f, new Vector3(1.2f, 1.9f, 0.06f), frame);
            P("Facade_Window", PrimitiveType.Cube, a, 8.7f, new Vector3(0.96f, 1.66f, 0.08f), glass);

            // Köşklerin ek katı: yuvarlak pencere (oeil-de-boeuf)
            if (h > 12f)
            {
                Disc("Facade_OculusRing", a, h - 1.75f, 0.95f, light, 0.04f);
                Disc("Facade_Oculus", a, h - 1.75f, 0.7f, glass, 0.06f);
            }
        }

        // Pilastrlar (bölmeler arasında) ve başlıkları
        for (int k = 0; k <= bays; k++)
        {
            float a = -len / 2f + bay * k;
            if (k == 0) a += 0.22f;
            if (k == bays) a -= 0.22f;
            float top = h - 0.95f;
            P("Facade_Pilaster", PrimitiveType.Cube, a, (3.75f + top) * 0.5f, new Vector3(0.4f, top - 3.75f, 0.16f), light);
            P("Facade_PilasterCap", PrimitiveType.Cube, a, top + 0.06f, new Vector3(0.56f, 0.16f, 0.24f), light);
        }
    }

    /// <summary>Arduvaz mansart çatı (dört yanı dik eğimli), altın kenar şeridi, bacalar, bir yüzde çatı pencereleri.</summary>
    private void Mansard(Transform f, Vector3 c, float w, float d, float h, Color slate, Color gold, Color stone, Color glass, Vector3 dormerDir)
    {
        const float inset = 0.9f;
        MeshPart(f, "Facade_Mansard", PyramidMesh(w + 0.3f, d + 0.3f, h, w - inset * 2f, d - inset * 2f), c, Mat(slate));
        Decor(f, "Facade_RoofCrest", PrimitiveType.Cube, c + Vector3.up * (h + 0.04f), 0f, new Vector3(w - inset * 2f + 0.06f, 0.08f, d - inset * 2f + 0.06f), gold);
        float half = Mathf.Abs(dormerDir.x) > 0.5f ? w * 0.5f : d * 0.5f;
        float len = Mathf.Abs(dormerDir.x) > 0.5f ? d : w;
        Vector3 tangent = Mathf.Abs(dormerDir.x) > 0.5f ? Vector3.forward : Vector3.right;
        Quaternion q = Quaternion.LookRotation(dormerDir);
        int count = Mathf.Max(1, Mathf.RoundToInt(len / 2.6f));
        float step = len / count;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = c + dormerDir * (half - 0.35f) + tangent * (-len / 2f + step * (i + 0.5f)) + Vector3.up * 1.05f;
            Decor(f, "Facade_Dormer", PrimitiveType.Cube, p, 0f, new Vector3(0.9f, 1.3f, 0.9f), stone).localRotation = q;
            Decor(f, "Facade_DormerGlass", PrimitiveType.Cube, p + dormerDir * 0.46f + Vector3.down * 0.08f, 0f, new Vector3(0.55f, 0.8f, 0.03f), glass).localRotation = q;
            MeshPart(f, "Facade_DormerRoof", PyramidMesh(1.1f, 1.1f, 0.55f, 0f, 0f), p + Vector3.up * 0.65f, Mat(slate));
        }
        for (int i = 0; i < 2; i++)
        {
            Vector3 p = c + tangent * (len * (i == 0 ? -0.3f : 0.3f)) + Vector3.up * (h + 0.6f);
            Decor(f, "Facade_Chimney", PrimitiveType.Cube, p, 0f, new Vector3(0.5f, 1.3f, 1.1f), stone);
        }
    }

    /// <summary>Louvre köşk çatısı: dik kesik piramit (kare kubbe) + üstte ikinci kat, altın sırt, tepe süsü.</summary>
    private void PavilionRoof(Transform f, Vector3 c, float w, float d, float h, Color slate, Color gold)
    {
        float h1 = h * 0.68f, h2 = h * 0.32f;
        MeshPart(f, "Facade_PavilionRoof", PyramidMesh(w + 0.3f, d + 0.3f, h1, w * 0.58f, d * 0.58f), c, Mat(slate));
        MeshPart(f, "Facade_PavilionRoofTop", PyramidMesh(w * 0.58f, d * 0.58f, h2, w * 0.18f, d * 0.18f), c + Vector3.up * h1, Mat(slate));
        Decor(f, "Facade_PavilionBand", PrimitiveType.Cube, c + Vector3.up * h1, 0f, new Vector3(w * 0.58f + 0.08f, 0.12f, d * 0.58f + 0.08f), gold);
        Decor(f, "Facade_PavilionCrest", PrimitiveType.Cube, c + Vector3.up * (h + 0.06f), 0f, new Vector3(w * 0.18f + 0.06f, 0.12f, d * 0.18f + 0.06f), gold);
        Decor(f, "Facade_Finial", PrimitiveType.Cylinder, c + Vector3.up * (h + 0.6f), 0f, new Vector3(0.12f, 0.55f, 0.12f), gold);
        Decor(f, "Facade_FinialBall", PrimitiveType.Sphere, c + Vector3.up * (h + 1.2f), 0f, Vector3.one * 0.3f, gold);
        Decor(f, "Facade_PavilionCornice", PrimitiveType.Cube, c + Vector3.up * 0.02f, 0f, new Vector3(w + 0.5f, 0.2f, d + 0.5f), Color.Lerp(gold, Color.white, 0.6f));
    }

    /// <summary>Üçgen havuz: granit kenar + içinde su + ortada fıskiye.</summary>
    private void TriPool(Transform f, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 m = (a + b + c) / 3f;
        // Alçak, açık renk granit kenar; içinde parlak su; ortada ince, yarı saydam su fıskiyesi
        MeshPart(f, "Facade_PoolRim", TriPrismMesh(a, b, c, 0.16f), new Vector3(0f, 0f, 0f), Mat(new Color(0.66f, 0.65f, 0.63f)));
        Vector2 Shrink(Vector2 pt) => m + (pt - m) * 0.9f;
        MeshPart(f, "Facade_PoolWater", TriPrismMesh(Shrink(a), Shrink(b), Shrink(c), 0.01f), new Vector3(0f, 0.162f, 0f), Glass(new Color(0.22f, 0.45f, 0.62f, 0.85f)));
        Transform jet = MeshPart(f, "Facade_FountainJet", ConeMesh(0.12f, 1.1f, 8), new Vector3(m.x, 0.17f, m.y), Glass(new Color(0.85f, 0.93f, 1f, 0.45f)));
        jet.localScale = Vector3.one;
    }

    /// <summary>Tabanı (x, z) üçgeni olan dik prizma.</summary>
    private Mesh TriPrismMesh(Vector2 a, Vector2 b, Vector2 c, float h)
    {
        // Saat yönünün tersi olsun ki üst yüz yukarı baksın
        if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) > 0f) { Vector2 t = b; b = c; c = t; }
        Vector3 A0 = new Vector3(a.x, 0f, a.y), B0 = new Vector3(b.x, 0f, b.y), C0 = new Vector3(c.x, 0f, c.y);
        Vector3 up = Vector3.up * h;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        void Tri(Vector3 v0, Vector3 v1, Vector3 v2) { int vi = verts.Count; verts.Add(v0); verts.Add(v1); verts.Add(v2); tris.AddRange(new[] { vi, vi + 1, vi + 2 }); }
        Tri(A0 + up, B0 + up, C0 + up);
        Vector3[] ring = { A0, B0, C0 };
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = ring[i], q = ring[(i + 1) % 3];
            // Halka (üstten) saat yönünde: dış yüz için kenarı ters sırayla örüyoruz (PyramidMesh ile aynı düzen)
            Tri(q, p + up, p);
            Tri(q, q + up, p + up);
        }
        return Finish(verts, tris, "TriPrism");
    }

    /// <summary>Çarpıştırıcılı kutu (oyuncular kanatların / köşklerin içinden geçemesin).</summary>
    private Transform Solid(Transform f, string name, Vector3 pos, Vector3 size, Color c)
    {
        Transform t = Decor(f, name, PrimitiveType.Cube, pos, 0f, size, c);
        t.gameObject.AddComponent<BoxCollider>();
        return t;
    }

    private void FacadeLight(Transform f, Vector3 pos, Color c, float intensity, float range)
    {
        GameObject go = new GameObject("Facade_Light");
        go.transform.SetParent(f, false);
        go.transform.localPosition = pos;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
    }

    // ---------------- Londra: British Museum ----------------
    private void BritishMuseum(Transform f, MuseumTheme th)
    {
        Color portland = new Color(0.88f, 0.86f, 0.8f);
        Cladding(f, portland);
        Windows(f, 2.1f, 1.6f, 0.8f, 3f, portland * 0.9f, new Color(0.2f, 0.24f, 0.3f));
        // Geniş basamaklı platform ve 8 sütunlu revak
        Decor(f, "Facade_Stylobate", PrimitiveType.Cube, new Vector3(-8.8f, 0.2f, 0f), 0f, new Vector3(5.6f, 0.4f, 18f), portland * 0.95f);
        for (int i = 0; i < 8; i++)
        {
            float z = -7f + i * 2f;
            Pillar(f, new Vector3(-10.8f, 0.4f, z), 0.32f, 4.6f, portland, Mathf.Abs(z) > 1.5f);
            Decor(f, "Facade_IonicCap", PrimitiveType.Cube, new Vector3(-10.8f, 5.05f, z), 0f, new Vector3(0.95f, 0.18f, 0.7f), portland * 0.92f);
        }
        Decor(f, "Facade_Entablature", PrimitiveType.Cube, new Vector3(-8.7f, 5.4f, 0f), 0f, new Vector3(5.2f, 0.6f, 17f), portland);
        Decor(f, "Facade_Frieze", PrimitiveType.Cube, new Vector3(-11.31f, 5.4f, 0f), 0f, new Vector3(0.04f, 0.3f, 16.6f), portland * 0.82f);
        Prism(f, "Facade_Pediment", new Vector3(-8.7f, 5.7f, 0f), 5.2f, 17.4f, 2.4f, 0f, portland);
        Decor(f, "Facade_Tympanum", PrimitiveType.Cube, new Vector3(-11.25f, 6.3f, 0f), 0f, new Vector3(0.05f, 0.8f, 8f), portland * 0.8f);
        // Portik tavanı
        Decor(f, "Facade_PorticoCeiling", PrimitiveType.Cube, new Vector3(-8.7f, 5.05f, 0f), 0f, new Vector3(5f, 0.1f, 16.6f), portland * 0.85f);
    }

    // ---------------- Amsterdam: Rijksmuseum ----------------
    private void Rijksmuseum(Transform f, MuseumTheme th)
    {
        Color brick = new Color(0.62f, 0.27f, 0.2f), white = new Color(0.92f, 0.9f, 0.84f), slate = new Color(0.22f, 0.25f, 0.3f), gold = new Color(0.9f, 0.75f, 0.35f);
        Cladding(f, brick);
        for (float y = 1f; y < 4f; y += 1.4f)
            Decor(f, "Facade_StoneBand", PrimitiveType.Cube, new Vector3(-6.28f, y, -1f), 0f, new Vector3(0.04f, 0.14f, 30.2f), white);
        Windows(f, 2.4f, 1.5f, 0.8f, 2.6f, white, new Color(0.2f, 0.24f, 0.3f), -6.3f);
        // Kırma çatı (tüm bina) ve iki kule
        Prism(f, "Facade_MainRoof", new Vector3(16f, WallH + 0.24f, -1f), 45f, 31f, 4.2f, 0.25f, slate);
        foreach (float z in new[] { -3.6f, 3.6f })
        {
            Decor(f, "Facade_Tower", PrimitiveType.Cube, new Vector3(-7.3f, 4f, z), 0f, new Vector3(2.4f, 8f, 2.4f), brick);
            Decor(f, "Facade_TowerBand", PrimitiveType.Cube, new Vector3(-7.3f, 6.2f, z), 0f, new Vector3(2.5f, 0.2f, 2.5f), white);
            Decor(f, "Facade_TowerWindow", PrimitiveType.Cube, new Vector3(-8.52f, 6.9f, z), 0f, new Vector3(0.05f, 0.9f, 0.7f), new Color(0.2f, 0.24f, 0.3f));
            MeshPart(f, "Facade_TowerRoof", PyramidMesh(2.6f, 2.6f, 3.2f, 0f, 0f), new Vector3(-7.3f, 8f, z), Mat(slate));
            Decor(f, "Facade_Spire", PrimitiveType.Cylinder, new Vector3(-7.3f, 11.6f, z), 0f, new Vector3(0.08f, 0.5f, 0.08f), gold);
        }
        // Kemerli geçit
        Decor(f, "Facade_GateWall", PrimitiveType.Cube, new Vector3(-7.3f, 4.6f, 0f), 0f, new Vector3(2.4f, 1.8f, 4.8f), brick);
        Transform arch = Decor(f, "Facade_Arch", PrimitiveType.Cylinder, new Vector3(-8.52f, 3.6f, 0f), 0f, new Vector3(2.6f, 0.06f, 2.6f), white);
        arch.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Decor(f, "Facade_Clock", PrimitiveType.Cylinder, new Vector3(-8.53f, 4.9f, 0f), 0f, new Vector3(0.9f, 0.03f, 0.9f), gold).localRotation = Quaternion.Euler(0f, 0f, 90f);
    }

    // ---------------- Floransa: Uffizi + Duomo ----------------
    private void Uffizi(Transform f, MuseumTheme th)
    {
        Color ochre = new Color(0.86f, 0.74f, 0.52f), pietra = new Color(0.55f, 0.55f, 0.55f), terracotta = new Color(0.78f, 0.38f, 0.24f), white = new Color(0.95f, 0.94f, 0.9f);
        Cladding(f, ochre);
        // Uzun revak: sütunlar, kemerler, üst kat ve kiremit saçak
        for (float z = -14f; z <= 13f; z += 3f)
        {
            if (Mathf.Abs(z) < 1.6f) continue;
            Pillar(f, new Vector3(-9.2f, 0f, z), 0.22f, 3f, pietra, true);
        }
        for (float z = -12.5f; z <= 12f; z += 3f)
        {
            Transform arch = Decor(f, "Facade_Arch", PrimitiveType.Cylinder, new Vector3(-9.2f, 3f, z), 0f, new Vector3(2.8f, 0.12f, 2.8f), pietra);
            arch.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        Decor(f, "Facade_LoggiaUpper", PrimitiveType.Cube, new Vector3(-9.2f, 4.2f, -1f), 0f, new Vector3(0.4f, 1.6f, 30f), ochre);
        Decor(f, "Facade_LoggiaRoof", PrimitiveType.Cube, new Vector3(-7.6f, 5.05f, -1f), 0f, new Vector3(3.6f, 0.2f, 30.4f), terracotta);
        for (float z = -11f; z <= 11f; z += 6f)
        {
            if (Mathf.Abs(z) < 2f) continue;
            Decor(f, "Facade_NicheStatue", PrimitiveType.Capsule, new Vector3(-6.45f, 1.6f, z), 0f, new Vector3(0.4f, 0.7f, 0.4f), white);
        }
        // Duomo kubbesi (çatının ortasında): kiremit kubbe, beyaz kaburgalar, fener
        Vector3 dc = new Vector3(14f, WallH + 0.24f, -1f);
        Decor(f, "Facade_DrumBase", PrimitiveType.Cylinder, dc + Vector3.up * 0.8f, 0f, new Vector3(9f, 0.8f, 9f), ochre);
        Decor(f, "Facade_Dome", PrimitiveType.Sphere, dc + Vector3.up * 1.6f, 0f, new Vector3(8.6f, 9f, 8.6f), terracotta);
        for (int i = 0; i < 8; i++)
        {
            Transform rib = Decor(f, "Facade_DomeRib", PrimitiveType.Cube, dc + Vector3.up * 3.6f, i * 22.5f, new Vector3(0.14f, 4.2f, 8.75f), white);
            rib.localScale = new Vector3(0.14f, 6.8f, 0.2f);
            rib.localPosition = dc + Vector3.up * 3.5f + Quaternion.Euler(0f, i * 45f, 0f) * new Vector3(0f, 0f, 3.6f);
            rib.localRotation = Quaternion.Euler(0f, i * 45f, 0f) * Quaternion.Euler(-28f, 0f, 0f);
        }
        Decor(f, "Facade_Lantern", PrimitiveType.Cylinder, dc + Vector3.up * 6.4f, 0f, new Vector3(1.2f, 0.8f, 1.2f), white);
        Decor(f, "Facade_LanternBall", PrimitiveType.Sphere, dc + Vector3.up * 7.5f, 0f, Vector3.one * 0.5f, new Color(0.9f, 0.75f, 0.35f));
    }

    // ---------------- Madrid: Prado ----------------
    private void Prado(Transform f, MuseumTheme th)
    {
        Color brick = new Color(0.72f, 0.42f, 0.32f), granite = new Color(0.8f, 0.78f, 0.74f), bronze = new Color(0.3f, 0.25f, 0.18f);
        Cladding(f, brick);
        for (float z = -14f; z <= 13f; z += 3.2f)
        {
            if (Mathf.Abs(z) < 3f) continue;
            Decor(f, "Facade_Niche", PrimitiveType.Cube, new Vector3(-6.3f, 2.2f, z), 0f, new Vector3(0.08f, 2.2f, 1f), granite);
            Decor(f, "Facade_NicheIn", PrimitiveType.Cube, new Vector3(-6.33f, 2.1f, z), 0f, new Vector3(0.05f, 1.8f, 0.7f), new Color(0.25f, 0.22f, 0.2f));
        }
        Decor(f, "Facade_Cornice", PrimitiveType.Cube, new Vector3(-6.45f, 4.1f, -1f), 0f, new Vector3(0.6f, 0.3f, 30.4f), granite);
        // Dor sütunlu giriş, düz saçak ve attika
        Decor(f, "Facade_PorchBase", PrimitiveType.Cube, new Vector3(-8.2f, 0.15f, 0f), 0f, new Vector3(4.4f, 0.3f, 12.5f), granite);
        for (int i = 0; i < 6; i++)
        {
            float z = -5.5f + i * 2.2f;
            Pillar(f, new Vector3(-9.8f, 0.3f, z), 0.33f, 4.2f, granite, Mathf.Abs(z) > 1.5f);
        }
        Decor(f, "Facade_PorchEntablature", PrimitiveType.Cube, new Vector3(-8.1f, 4.75f, 0f), 0f, new Vector3(4.2f, 0.7f, 12.5f), granite);
        Decor(f, "Facade_Attic", PrimitiveType.Cube, new Vector3(-8.1f, 5.5f, 0f), 0f, new Vector3(3.6f, 0.8f, 10f), granite * 0.95f);
        // Önünde Velázquez heykeli
        Decor(f, "Facade_StatueBase", PrimitiveType.Cube, new Vector3(-15.5f, 0.9f, 7f), 0f, new Vector3(1.6f, 1.8f, 1.6f), granite);
        Decor(f, "Facade_StatueBody", PrimitiveType.Capsule, new Vector3(-15.5f, 2.6f, 7f), 0f, new Vector3(0.6f, 0.9f, 0.5f), bronze);
        Decor(f, "Facade_StatueHead", PrimitiveType.Sphere, new Vector3(-15.5f, 3.75f, 7f), 0f, Vector3.one * 0.38f, bronze);
    }

    // ---------------- İstanbul: Topkapı kapısı + Ayasofya kubbesi ----------------
    private void Topkapi(Transform f, MuseumTheme th)
    {
        Color marble = new Color(0.93f, 0.92f, 0.88f), lead = new Color(0.45f, 0.5f, 0.56f), turquoise = new Color(0.2f, 0.6f, 0.65f), gold = new Color(0.95f, 0.8f, 0.3f);
        Cladding(f, marble);
        Decor(f, "Facade_TileBand", PrimitiveType.Cube, new Vector3(-6.29f, 3.6f, -1f), 0f, new Vector3(0.05f, 0.4f, 30.2f), turquoise);
        // Bab-üs Selam: iki sekizgen kule, konik kurşun külahlar, aralarında mazgallı duvar
        foreach (float z in new[] { -3.3f, 3.3f })
        {
            Decor(f, "Facade_GateTower", PrimitiveType.Cylinder, new Vector3(-7.8f, 3.5f, z), 0f, new Vector3(2.6f, 3.5f, 2.6f), marble);
            MeshPart(f, "Facade_TowerCone", ConeMesh(1.45f, 3.6f, 8), new Vector3(-7.8f, 7f, z), Mat(lead));
            Decor(f, "Facade_ConeTip", PrimitiveType.Sphere, new Vector3(-7.8f, 10.7f, z), 0f, Vector3.one * 0.3f, gold);
        }
        Decor(f, "Facade_GateWall", PrimitiveType.Cube, new Vector3(-7.8f, 4.3f, 0f), 0f, new Vector3(1.6f, 1.4f, 4f), marble);
        for (float z = -1.6f; z <= 1.7f; z += 0.8f)
            Decor(f, "Facade_Merlon", PrimitiveType.Cube, new Vector3(-7.8f, 5.25f, z), 0f, new Vector3(1.6f, 0.5f, 0.4f), marble);
        Transform arch = Decor(f, "Facade_GateArch", PrimitiveType.Cylinder, new Vector3(-8.62f, 3.4f, 0f), 0f, new Vector3(2.2f, 0.05f, 2.2f), turquoise);
        arch.localRotation = Quaternion.Euler(0f, 0f, 90f);
        // Ayasofya: büyük kurşun kubbe + yarım kubbeler + dört minare
        Vector3 dc = new Vector3(15f, WallH + 0.24f, -1f);
        Decor(f, "Facade_Drum", PrimitiveType.Cylinder, dc + Vector3.up * 0.6f, 0f, new Vector3(11f, 0.6f, 11f), marble);
        Decor(f, "Facade_Dome", PrimitiveType.Sphere, dc + Vector3.up * 1.2f, 0f, new Vector3(10.6f, 6.4f, 10.6f), lead);
        Decor(f, "Facade_HalfDomeW", PrimitiveType.Sphere, dc + new Vector3(-6.5f, 0.2f, 0f), 0f, new Vector3(6f, 4f, 6f), lead);
        Decor(f, "Facade_HalfDomeE", PrimitiveType.Sphere, dc + new Vector3(6.5f, 0.2f, 0f), 0f, new Vector3(6f, 4f, 6f), lead);
        Decor(f, "Facade_DomeFinial", PrimitiveType.Sphere, dc + Vector3.up * 4.5f, 0f, Vector3.one * 0.5f, gold);
        foreach (Vector2 m in new[] { new Vector2(-5.2f, -15.2f), new Vector2(-5.2f, 13.2f), new Vector2(37.2f, -15.2f), new Vector2(37.2f, 13.2f) })
        {
            Decor(f, "Facade_Minaret", PrimitiveType.Cylinder, new Vector3(m.x, 6.5f, m.y), 0f, new Vector3(0.7f, 6.5f, 0.7f), marble);
            Decor(f, "Facade_Balcony", PrimitiveType.Cylinder, new Vector3(m.x, 9.5f, m.y), 0f, new Vector3(1.1f, 0.1f, 1.1f), marble);
            MeshPart(f, "Facade_MinaretCone", ConeMesh(0.4f, 2.4f, 8), new Vector3(m.x, 13f, m.y), Mat(lead));
        }
    }

    // ---------------- Kahire: Mısır Müzesi + dikilitaş, sfenks, piramit ----------------
    private void EgyptianMuseum(Transform f, MuseumTheme th)
    {
        Color pink = new Color(0.88f, 0.58f, 0.5f), sand = new Color(0.86f, 0.74f, 0.52f), cream = new Color(0.93f, 0.88f, 0.76f), gold = new Color(0.95f, 0.8f, 0.3f);
        Cladding(f, pink);
        Windows(f, 2.2f, 1.6f, 0.8f, 3f, cream, new Color(0.25f, 0.22f, 0.2f));
        // Kemerli büyük giriş kütlesi ve küçük kubbe
        Decor(f, "Facade_PortalBlock", PrimitiveType.Cube, new Vector3(-7f, 3f, 0f), 0f, new Vector3(1.6f, 6f, 7f), pink);
        Decor(f, "Facade_PortalFrame", PrimitiveType.Cube, new Vector3(-7.82f, 2.6f, 0f), 0f, new Vector3(0.05f, 3.6f, 3.4f), cream);
        Transform arch = Decor(f, "Facade_PortalArch", PrimitiveType.Cylinder, new Vector3(-7.83f, 4.2f, 0f), 0f, new Vector3(3.4f, 0.05f, 3.4f), cream);
        arch.localRotation = Quaternion.Euler(0f, 0f, 90f);
        foreach (float z in new[] { -2.6f, 2.6f }) Pillar(f, new Vector3(-8.3f, 0f, z), 0.28f, 4.8f, cream, true);
        Decor(f, "Facade_PortalDome", PrimitiveType.Sphere, new Vector3(-6.8f, 6f, 0f), 0f, new Vector3(3.4f, 2.2f, 3.4f), pink * 0.95f);
        // Bahçe: iki dikilitaş, sfenks, küçük piramit
        foreach (float z in new[] { -6f, 6f })
        {
            Decor(f, "Facade_Obelisk", PrimitiveType.Cube, new Vector3(-12.5f, 2.6f, z), 0f, new Vector3(0.55f, 5.2f, 0.55f), sand);
            MeshPart(f, "Facade_ObeliskTip", PyramidMesh(0.55f, 0.55f, 0.6f, 0f, 0f), new Vector3(-12.5f, 5.2f, z), Mat(gold));
        }
        Vector3 s = new Vector3(-16f, 0f, -6.5f);
        Decor(f, "Facade_SphinxBody", PrimitiveType.Cube, s + new Vector3(0f, 0.6f, 0f), 0f, new Vector3(1.2f, 1.2f, 3.4f), sand);
        Decor(f, "Facade_SphinxPaws", PrimitiveType.Cube, s + new Vector3(0f, 0.25f, 2.1f), 0f, new Vector3(1.1f, 0.5f, 1.2f), sand);
        Decor(f, "Facade_SphinxHead", PrimitiveType.Cube, s + new Vector3(0f, 1.6f, 1.3f), 0f, new Vector3(0.9f, 0.9f, 0.8f), sand);
        Decor(f, "Facade_SphinxNemes", PrimitiveType.Cube, s + new Vector3(0f, 1.5f, 1.1f), 0f, new Vector3(1.2f, 1.1f, 0.5f), new Color(0.25f, 0.35f, 0.6f));
        MeshPart(f, "Facade_Pyramid", PyramidMesh(5f, 5f, 3.6f, 0f, 0f), new Vector3(-16.5f, 0f, 9f), Mat(sand));
    }

    // ---------------- Tokyo: Tokyo Ulusal Müzesi ----------------
    private void TokyoMuseum(Transform f, MuseumTheme th)
    {
        Color concrete = new Color(0.84f, 0.8f, 0.72f), tile = new Color(0.25f, 0.3f, 0.32f), red = new Color(0.8f, 0.18f, 0.12f), dark = new Color(0.12f, 0.1f, 0.1f), stoneC = new Color(0.62f, 0.62f, 0.6f);
        Cladding(f, concrete);
        Windows(f, 2.2f, 1.5f, 1f, 2.8f, dark, new Color(0.3f, 0.35f, 0.38f));
        // Büyük kiremit kırma çatı ("imparatorluk tacı"), saçak taşkın
        MeshPart(f, "Facade_HipRoof", PyramidMesh(47f, 33f, 4.6f, 30f, 12f), new Vector3(16f, WallH + 0.24f, -1f), Mat(tile));
        Decor(f, "Facade_RidgeCap", PrimitiveType.Cube, new Vector3(16f, WallH + 4.9f, -1f), 0f, new Vector3(30.4f, 0.3f, 0.6f), dark);
        // Girişte saçaklı sundurma
        Transform eave = Decor(f, "Facade_PorchRoof", PrimitiveType.Cube, new Vector3(-7.4f, 3.7f, 0f), 0f, new Vector3(3f, 0.18f, 7f), tile);
        eave.localRotation = Quaternion.Euler(0f, 0f, 14f);
        foreach (float z in new[] { -3f, 3f }) Pillar(f, new Vector3(-8.4f, 0f, z), 0.18f, 3.3f, red, true);
        // Kırmızı torii ve taş fenerler
        foreach (float z in new[] { -2.5f, 2.5f }) Pillar(f, new Vector3(-15f, 0f, z), 0.22f, 4.3f, red, true);
        Decor(f, "Facade_ToriiTop", PrimitiveType.Cube, new Vector3(-15f, 4.45f, 0f), 0f, new Vector3(0.45f, 0.3f, 7.2f), dark);
        Decor(f, "Facade_ToriiBeam", PrimitiveType.Cube, new Vector3(-15f, 3.7f, 0f), 0f, new Vector3(0.3f, 0.22f, 5.8f), red);
        foreach (float z in new[] { -4.5f, 4.5f })
            foreach (float x in new[] { -10.5f, -12.5f })
            {
                Decor(f, "Facade_LanternPost", PrimitiveType.Cylinder, new Vector3(x, 0.6f, z), 0f, new Vector3(0.25f, 0.6f, 0.25f), stoneC);
                Decor(f, "Facade_LanternBox", PrimitiveType.Cube, new Vector3(x, 1.4f, z), 0f, new Vector3(0.5f, 0.45f, 0.5f), new Color(1f, 0.9f, 0.6f));
                MeshPart(f, "Facade_LanternCap", PyramidMesh(0.8f, 0.8f, 0.4f, 0f, 0f), new Vector3(x, 1.62f, z), Mat(stoneC));
            }
    }

    // =====================================================================
    //  Mesh yardımcıları
    // =====================================================================

    private Transform MeshPart(Transform parent, string name, Mesh mesh, Vector3 localPos, Material m)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        return go.transform;
    }

    /// <summary>Kesik piramit (topW / topD = 0 ise sivri). Taban merkezi orijinde, düz yüzlü.</summary>
    private Mesh PyramidMesh(float bw, float bd, float h, float tw, float td)
    {
        Vector3[] b = { new Vector3(-bw / 2, 0, -bd / 2), new Vector3(bw / 2, 0, -bd / 2), new Vector3(bw / 2, 0, bd / 2), new Vector3(-bw / 2, 0, bd / 2) };
        Vector3[] t = { new Vector3(-tw / 2, h, -td / 2), new Vector3(tw / 2, h, -td / 2), new Vector3(tw / 2, h, td / 2), new Vector3(-tw / 2, h, td / 2) };
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            int v = verts.Count;
            verts.Add(b[i]); verts.Add(b[j]); verts.Add(t[j]); verts.Add(t[i]);
            tris.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 });
        }
        if (tw > 0f && td > 0f)
        {
            int v = verts.Count;
            verts.AddRange(t);
            tris.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 });
        }
        return Finish(verts, tris, "Pyramid");
    }

    /// <summary>Üçgen prizma (beşik çatı / alınlık): x boyunca derinlik, z boyunca genişlik. topWidth > 0 ise kesik (mansart).</summary>
    private Transform Prism(Transform parent, string name, Vector3 basePos, float depth, float width, float rise, float topFrac, Color c)
    {
        float hd = depth / 2, hw = width / 2, tw = hw * topFrac;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        void Quad(Vector3 a, Vector3 bb, Vector3 cc, Vector3 d)
        {
            int v = verts.Count;
            verts.Add(a); verts.Add(bb); verts.Add(cc); verts.Add(d);
            tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
        }
        Vector3 A(float x, float y, float z) => new Vector3(x, y, z);
        // eğimli yüzler
        Quad(A(-hd, 0, -hw), A(-hd, rise, -tw), A(hd, rise, -tw), A(hd, 0, -hw));
        Quad(A(hd, 0, hw), A(hd, rise, tw), A(-hd, rise, tw), A(-hd, 0, hw));
        if (tw > 0.01f) Quad(A(-hd, rise, -tw), A(-hd, rise, tw), A(hd, rise, tw), A(hd, rise, -tw));
        // uç yüzler (üçgen / yamuk)
        Quad(A(-hd, 0, hw), A(-hd, rise, tw), A(-hd, rise, -tw), A(-hd, 0, -hw));
        Quad(A(hd, 0, -hw), A(hd, rise, -tw), A(hd, rise, tw), A(hd, 0, hw));
        return MeshPart(parent, name, Finish(verts, tris, "Prism"), basePos, Mat(c));
    }

    /// <summary>n kenarlı koni (külah).</summary>
    private Mesh ConeMesh(float r, float h, int sides)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            int v = verts.Count;
            verts.Add(new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r));
            verts.Add(new Vector3(0f, h, 0f));
            verts.Add(new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r));
            tris.AddRange(new[] { v, v + 1, v + 2 });
        }
        return Finish(verts, tris, "Cone");
    }

    private Mesh Finish(List<Vector3> verts, List<int> tris, string name)
    {
        var m = new Mesh { name = name };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        meshes.Add(m);
        return m;
    }

    /// <summary>İki nokta arasında ince kiriş (piramidin kenar çerçevesi gibi).</summary>
    private void Beam(Transform parent, string name, Vector3 a, Vector3 b, float thick, Color c)
    {
        Transform t = Decor(parent, name, PrimitiveType.Cube, (a + b) * 0.5f, 0f, new Vector3(thick, thick, Vector3.Distance(a, b)), c);
        t.localRotation = Quaternion.LookRotation(b - a);
    }

    /// <summary>Yarı saydam cam malzemesi.</summary>
    private Material Glass(Color c)
    {
        Material m = ArtworkLook.MakeLit(c);
        VisionConeVisual.MakeTransparent(m);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.9f);
        m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        mats.Add(m);
        return m;
    }

    private void Pillar(Transform parent, Vector3 pos, float radius, float height, Color c, bool collider)
    {
        Transform t = NewGroup(parent, "Facade_Pillar");
        t.localPosition = pos;
        Decor(t, "Shaft", PrimitiveType.Cylinder, new Vector3(0f, height * 0.5f, 0f), 0f, new Vector3(radius * 2f, height * 0.5f, radius * 2f), c);
        Decor(t, "Base", PrimitiveType.Cube, new Vector3(0f, 0.1f, 0f), 0f, new Vector3(radius * 2.6f, 0.2f, radius * 2.6f), c * 0.92f);
        Decor(t, "Top", PrimitiveType.Cube, new Vector3(0f, height - 0.08f, 0f), 0f, new Vector3(radius * 2.6f, 0.16f, radius * 2.6f), c * 0.92f);
        if (collider)
        {
            CapsuleCollider col = t.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, height * 0.5f, 0f);
            col.radius = radius;
            col.height = height;
        }
    }


    // ---------------- Sokak ----------------

    private static readonly Rect[] StreetBlocked =
    {
        Rect.MinMaxRect(-40.5f, 0.5f, -26.5f, 8.5f),   // kamyon
        Rect.MinMaxRect(-37f, -23f, -21f, -9f),        // garaj
        Rect.MinMaxRect(-16f, -21f, -9f, -16.5f),      // karaborsa
        Rect.MinMaxRect(-28.5f, -2.8f, -6f, 2.8f),     // kamyon → müze yolu
        Rect.MinMaxRect(-31.5f, -10f, -26.5f, 1f),     // kamyon → garaj yolu
        Rect.MinMaxRect(-15.2f, -24f, -12.8f, 16f),    // yol çizgisi
        Rect.MinMaxRect(-17.5f, -10f, -6f, 10f),      // cephe (ve önündeki meydan)
        Rect.MinMaxRect(-16f, -16.5f, -9f, -9f),       // karaborsaya giden ara sokak
    };

    private void BuildStreetProps(System.Random rng, int themeIndex)
    {
        Transform s = NewGroup(exterior, "StreetProps");
        var placed = new List<Vector2> { new Vector2(-9f, 4f), new Vector2(-20f, -6f), new Vector2(-9f, -12f),
                                          new Vector2(-7.2f, 5f), new Vector2(-7.2f, -5f), new Vector2(-7.3f, 9f), new Vector2(-7.3f, -9f) };

        int trees = 5 + rng.Next(5);
        for (int k = 0; k < trees; k++)
        {
            if (!PickStreetPoint(rng, placed, 3f, out Vector2 p)) continue;
            Tree(s, p, rng, themeIndex);
            placed.Add(p);
        }
        int benches = 1 + rng.Next(3);
        for (int k = 0; k < benches; k++)
        {
            if (!PickStreetPoint(rng, placed, 2.5f, out Vector2 p)) continue;
            Transform b = NewGroup(s, "Street_Bench");
            b.localPosition = new Vector3(p.x, 0f, p.y);
            b.localRotation = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);
            BoxCollider col = b.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.3f, 0f);
            col.size = new Vector3(1.8f, 0.6f, 0.6f);
            Color wood = new Color(0.45f, 0.3f, 0.18f);
            Decor(b, "Seat", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), 0f, new Vector3(1.8f, 0.08f, 0.5f), wood);
            Decor(b, "Back", PrimitiveType.Cube, new Vector3(0f, 0.75f, -0.24f), 0f, new Vector3(1.8f, 0.5f, 0.06f), wood);
            Decor(b, "Frame", PrimitiveType.Cube, new Vector3(0f, 0.22f, 0f), 0f, new Vector3(1.6f, 0.44f, 0.4f), new Color(0.15f, 0.15f, 0.16f));
            placed.Add(p);
        }

        // Yol kenarına park etmiş arabalar
        int cars = rng.Next(3);
        for (int k = 0; k < cars; k++)
        {
            float z = rng.Next(2) == 0 ? Range(rng, -8.5f, -3.5f) : Range(rng, 9.5f, 14f);
            Vector2 p = new Vector2(-17f, z);
            if (TooClose(p, placed, 4.5f) || InExtraBlocked(p)) continue;
            Car(s, p, rng);
            placed.Add(p);
        }
    }

    private bool InExtraBlocked(Vector2 p)
    {
        foreach (Rect r in extraBlocked) if (r.Contains(p)) return true;
        return false;
    }

    private bool PickStreetPoint(System.Random rng, List<Vector2> placed, float spacing, out Vector2 p)
    {
        for (int tries = 0; tries < 40; tries++)
        {
            p = new Vector2(Range(rng, -38.5f, -7f), Range(rng, -23f, 15f));
            bool ok = true;
            foreach (Rect r in StreetBlocked) if (r.Contains(p)) { ok = false; break; }
            foreach (Rect r in extraBlocked) if (r.Contains(p)) { ok = false; break; }
            if (!ok || TooClose(p, placed, spacing)) continue;
            return true;
        }
        p = Vector2.zero;
        return false;
    }

    private void Tree(Transform parent, Vector2 p, System.Random rng, int themeIndex)
    {
        Transform t = NewGroup(parent, "Street_Tree");
        t.localPosition = new Vector3(p.x, 0f, p.y);
        CapsuleCollider col = t.gameObject.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 1f, 0f);
        col.radius = 0.2f;
        col.height = 2f;
        Color trunk = new Color(0.4f, 0.27f, 0.17f);
        float h = Range(rng, 2.2f, 3.4f);

        if (themeIndex == 6) // Kahire: palmiye
        {
            Decor(t, "Trunk", PrimitiveType.Cylinder, new Vector3(0f, h * 0.75f, 0f), 0f, new Vector3(0.25f, h * 0.75f, 0.25f), new Color(0.55f, 0.42f, 0.28f));
            for (int i = 0; i < 5; i++)
            {
                Transform leaf = Decor(t, "Frond", PrimitiveType.Cube, new Vector3(0f, h * 1.5f, 0f), i * 72f, new Vector3(0.35f, 0.05f, 2.2f), new Color(0.25f, 0.55f, 0.25f));
                leaf.localRotation = Quaternion.Euler(18f, i * 72f, 0f);
            }
            return;
        }

        Color crown = themeIndex == 7 ? new Color(0.98f, 0.72f, 0.82f)          // Tokyo: sakura
                    : themeIndex == 3 || themeIndex == 4 ? new Color(0.36f, 0.5f, 0.28f) // Akdeniz
                    : new Color(0.26f, 0.52f, 0.28f);
        Decor(t, "Trunk", PrimitiveType.Cylinder, new Vector3(0f, h * 0.5f, 0f), 0f, new Vector3(0.28f, h * 0.5f, 0.28f), trunk);
        if (themeIndex == 3 && rng.Next(2) == 0) // Floransa: servi ağacı
        {
            Decor(t, "Crown", PrimitiveType.Capsule, new Vector3(0f, h + 1.2f, 0f), 0f, new Vector3(0.8f, 1.8f, 0.8f), new Color(0.2f, 0.4f, 0.22f));
            return;
        }
        float r = Range(rng, 1.4f, 2.2f);
        Decor(t, "Crown", PrimitiveType.Sphere, new Vector3(0f, h + r * 0.35f, 0f), 0f, new Vector3(r, r * 0.9f, r), crown);
        Decor(t, "Crown2", PrimitiveType.Sphere, new Vector3(r * 0.25f, h + r * 0.6f, -r * 0.15f), 0f, Vector3.one * r * 0.6f, crown * 1.08f);
    }

    private void Car(Transform parent, Vector2 p, System.Random rng)
    {
        Color[] paints = { new Color(0.75f, 0.15f, 0.15f), new Color(0.15f, 0.35f, 0.7f), new Color(0.92f, 0.92f, 0.9f), new Color(0.2f, 0.2f, 0.22f), new Color(0.95f, 0.75f, 0.2f) };
        Color paint = paints[rng.Next(paints.Length)];
        Transform c = NewGroup(parent, "Street_Car");
        c.localPosition = new Vector3(p.x, 0f, p.y);
        BoxCollider col = c.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.7f, 0f);
        col.size = new Vector3(1.8f, 1.4f, 4f);
        Decor(c, "Body", PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f), 0f, new Vector3(1.8f, 0.6f, 4f), paint);
        Decor(c, "Cabin", PrimitiveType.Cube, new Vector3(0f, 1.15f, -0.2f), 0f, new Vector3(1.6f, 0.55f, 2.1f), paint * 0.95f);
        Decor(c, "Glass", PrimitiveType.Cube, new Vector3(0f, 1.15f, -0.2f), 0f, new Vector3(1.62f, 0.4f, 2.0f), new Color(0.2f, 0.27f, 0.35f));
        foreach (float x in new[] { -0.85f, 0.85f })
            foreach (float z in new[] { -1.3f, 1.3f })
            {
                Transform w = Decor(c, "Wheel", PrimitiveType.Cylinder, new Vector3(x, 0.32f, z), 0f, new Vector3(0.62f, 0.1f, 0.62f), new Color(0.08f, 0.08f, 0.08f));
                w.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
    }

    private void TintGround(int themeIndex)
    {
        Color[] grounds =
        {
            new Color(0.42f, 0.42f, 0.44f), new Color(0.26f, 0.26f, 0.28f), new Color(0.45f, 0.34f, 0.29f), new Color(0.56f, 0.5f, 0.44f),
            new Color(0.5f, 0.45f, 0.4f), new Color(0.4f, 0.38f, 0.36f), new Color(0.66f, 0.57f, 0.42f), new Color(0.34f, 0.35f, 0.38f),
        };
        Color c = grounds[themeIndex % grounds.Length];
        if (block == null) block = new MaterialPropertyBlock();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject.name != "Ground_Street") continue;
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }
    }

    // ---------------- Günün saati ----------------

    private void ApplyLighting(System.Random rng, int themeIndex)
    {
        if (!lightSaved)
        {
            lightSaved = true;
            sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            }
            if (sun != null) { sunColor = sun.color; sunIntensity = sun.intensity; sunRot = sun.transform.rotation; }
            ambientIntensity = RenderSettings.ambientIntensity;
            fogOn = RenderSettings.fog;
            fogColor = RenderSettings.fogColor;
            fogDensity = RenderSettings.fogDensity;
            fogMode = RenderSettings.fogMode;
        }

        // 0 gece (%50), 1 akşam (%30), 2 gündüz (%20)
        double roll = rng.NextDouble();
        int tod = roll < 0.5 ? 0 : roll < 0.8 ? 1 : 2;
        float yaw = Range(rng, 0f, 360f);

        if (sun != null)
        {
            switch (tod)
            {
                case 0:
                    sun.color = new Color(0.55f, 0.63f, 0.92f);
                    sun.intensity = sunIntensity * 0.18f;
                    sun.transform.rotation = Quaternion.Euler(40f, yaw, 0f);
                    break;
                case 1:
                    sun.color = new Color(1f, 0.62f, 0.4f);
                    sun.intensity = sunIntensity * 0.65f;
                    sun.transform.rotation = Quaternion.Euler(14f, yaw, 0f);
                    break;
                default:
                    sun.color = sunColor;
                    sun.intensity = sunIntensity;
                    sun.transform.rotation = sunRot;
                    break;
            }
        }

        RenderSettings.ambientIntensity = ambientIntensity * (tod == 0 ? 0.55f : tod == 1 ? 0.8f : 1f);
        if (tod == 2)
        {
            RenderSettings.fog = fogOn;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogMode = fogMode;
        }
        else
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = tod == 0 ? new Color(0.07f, 0.09f, 0.16f) : new Color(0.55f, 0.38f, 0.34f);
            RenderSettings.fogDensity = tod == 0 ? 0.011f : 0.006f;
        }

        // Sokak lambaları gece daha parlak
        foreach (Light l in GetComponentsInChildren<Light>(true))
        {
            if (l.transform.parent == null || !l.transform.parent.name.StartsWith("StreetLamp")) continue;
            l.intensity = tod == 0 ? 6f : tod == 1 ? 4f : 2f;
        }
    }

    // =====================================================================
}
