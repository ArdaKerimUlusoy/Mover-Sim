using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// MuseumLayout'un parçası: Staff Only odasının kapısına KİLİTLİ bir kapı kurar (her haritada tohumdan, ağ nesnesi yok).
///  - Kilitliyken oyuncular geçemez; garajdan maymuncuk alan oyuncu E'yi basılı tutup açar (LockpickSystem).
///  - Açılınca bütün görev boyunca açık kalır (durum MuseumSession'da, herkese eşitlenir).
///  - Güvenlikler kapıyı HİÇBİR ŞEKİLDE açamaz / geçemez: kilitliyken NavMesh'te delik açar (NavMeshObstacle).
///    Maymuncukla açılınca delik kalkar, güvenlikler de girebilir.
///  - Dışarıdan belli olsun diye: kırmızı tabela, yanıp sönen kırmızı lamba, yerde sarı-siyah şerit, üstünde "STAFF ONLY" yazısı.
/// </summary>
public partial class MuseumLayout
{
    private void BuildStaffDoor(DoorEdge d, PRoom room, MuseumTheme th)
    {
        Vector2 along2 = d.vertical ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
        Vector2 inward2 = room.Center - d.Center;
        inward2 = d.vertical ? new Vector2(Mathf.Sign(inward2.x), 0f) : new Vector2(0f, Mathf.Sign(inward2.y));
        Vector3 along = new Vector3(along2.x, 0f, along2.y);
        Vector3 inward = new Vector3(inward2.x, 0f, inward2.y);

        const float Width = 2.36f, Height = 2.76f, Thick = 0.08f;

        Transform root = NewGroup(interior, "StaffDoor");
        root.localPosition = L(d.Center);
        root.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        // Oyuncuyu durduran duvar (kapı açılınca kapanır). Dönmez: kapı kanadı yalnızca görsel.
        GameObject block = new GameObject("StaffDoorBlock");
        block.transform.SetParent(root, false);
        block.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        BoxCollider bc = block.AddComponent<BoxCollider>();
        bc.size = d.vertical ? new Vector3(0.16f, 2.8f, 2.4f) : new Vector3(2.4f, 2.8f, 0.16f);
        // Kilitliyken güvenlikler de geçemez: NavMesh'te delik açar (açılınca StaffDoor kapatır)
        UnityEngine.AI.NavMeshObstacle obstacle = block.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
        obstacle.center = Vector3.zero;
        obstacle.size = d.vertical ? new Vector3(0.4f, 2.8f, 2.6f) : new Vector3(2.6f, 2.8f, 0.4f);
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;

        // Menteşe kapının bir yanında; kanat menteşenin çocuğu
        Transform hinge = NewGroup(root, "StaffDoorHinge");
        hinge.localPosition = -along * (Width * 0.5f);
        Vector3 c = along * (Width * 0.5f);

        Color wood = Color.Lerp(th.trim, new Color(0.25f, 0.2f, 0.18f), 0.55f);
        Vector3 panelSize = d.vertical ? new Vector3(Thick, Height, Width) : new Vector3(Width, Height, Thick);
        Decor(hinge, "StaffDoorLeaf", PrimitiveType.Cube, c + Vector3.up * (Height * 0.5f + 0.01f), 0f, panelSize, wood);

        // İki yüzde: kırmızı "STAFF ONLY" şeridi, kilit kutusu (ışığı kırmızı / yeşil), kol
        Transform lockLight = null;
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 face = inward * side * (Thick * 0.5f + 0.012f);
            Vector3 stripe = d.vertical ? new Vector3(0.02f, 0.22f, Width * 0.7f) : new Vector3(Width * 0.7f, 0.22f, 0.02f);
            Decor(hinge, "StaffDoorStripe", PrimitiveType.Cube, c + face + Vector3.up * 1.85f, 0f, stripe, new Color(0.75f, 0.1f, 0.12f));

            Vector3 handleAt = c + along * (Width * 0.5f - 0.22f) + face * 2.2f + Vector3.up * 1.05f;
            Vector3 lockSize = d.vertical ? new Vector3(0.05f, 0.16f, 0.1f) : new Vector3(0.1f, 0.16f, 0.05f);
            Decor(hinge, "StaffDoorLock", PrimitiveType.Cube, handleAt + Vector3.up * 0.16f, 0f, lockSize, new Color(0.2f, 0.2f, 0.22f));
            Transform led = Decor(hinge, "StaffDoorLight", PrimitiveType.Cube, handleAt + Vector3.up * 0.2f + face * 0.6f, 0f,
                                  new Vector3(0.035f, 0.035f, 0.035f), new Color(0.9f, 0.1f, 0.1f));
            if (lockLight == null) lockLight = led;
            else led.SetParent(lockLight, true); // iki ışık birlikte boyanır
            Vector3 handleSize = d.vertical ? new Vector3(0.05f, 0.04f, 0.16f) : new Vector3(0.16f, 0.04f, 0.05f);
            Decor(hinge, "StaffDoorHandle", PrimitiveType.Cube, handleAt - along * 0.06f, 0f, handleSize, new Color(0.75f, 0.72f, 0.65f));
        }

        // Kanat odanın içine doğru açılır
        float openYaw = Vector3.SignedAngle(along, inward, Vector3.up) * 0.95f;

        // ---- Dışarıdan belli olsun: kapı üstünde kırmızı tabela, yanıp sönen uyarı lambası, yerde sarı-siyah şerit ----
        Vector3 outward = -inward;
        Color red = new Color(0.8f, 0.08f, 0.1f);
        Vector3 plateAt = outward * 0.17f + Vector3.up * 3.35f;
        Decor(root, "StaffSignPlate", PrimitiveType.Cube, plateAt, 0f, d.vertical ? new Vector3(0.03f, 0.5f, 1.9f) : new Vector3(1.9f, 0.5f, 0.03f), red);
        foreach (float yy in new[] { -0.19f, 0.19f })
            Decor(root, "StaffSignLine", PrimitiveType.Cube, plateAt + outward * 0.02f + Vector3.up * yy, 0f,
                  d.vertical ? new Vector3(0.01f, 0.04f, 1.7f) : new Vector3(1.7f, 0.04f, 0.01f), new Color(0.95f, 0.95f, 0.9f));

        // Uyarı lambası (iki yüzde) + kırmızı ışık: kilitliyken yanıp söner, açılınca yeşil
        Transform lamp = Decor(root, "StaffLamp", PrimitiveType.Sphere, outward * 0.2f + Vector3.up * 3.76f, 0f, new Vector3(0.22f, 0.22f, 0.22f), new Color(1f, 0.15f, 0.12f));
        GameObject lightGo = new GameObject("StaffLampLight");
        lightGo.transform.SetParent(root, false);
        lightGo.transform.localPosition = outward * 0.6f + Vector3.up * 3.5f;
        Light lampLight = lightGo.AddComponent<Light>();
        lampLight.type = LightType.Point;
        lampLight.range = 6f;
        lampLight.intensity = 2f;
        lampLight.color = new Color(1f, 0.15f, 0.1f);
        lampLight.shadows = LightShadows.None;

        // Yerde, kapının dışında sarı-siyah tehlike şeridi
        const int Stripes = 6;
        for (int k = 0; k < Stripes; k++)
        {
            float t = (k + 0.5f) / Stripes - 0.5f;
            Vector3 at = along * (t * 2.4f) + outward * 0.55f + Vector3.up * 0.006f;
            Vector3 size = d.vertical ? new Vector3(0.5f, 0.012f, 2.4f / Stripes) : new Vector3(2.4f / Stripes, 0.012f, 0.5f);
            Decor(root, "StaffFloorTape", PrimitiveType.Cube, at, 0f, size, k % 2 == 0 ? new Color(0.95f, 0.78f, 0.1f) : new Color(0.08f, 0.08f, 0.08f));
        }

        StaffDoor door = root.gameObject.AddComponent<StaffDoor>();
        door.Setup(hinge, bc, lockLight, openYaw);
        door.SetupSign(lamp, lampLight, root.TransformPoint(outward * 0.4f + Vector3.up * 3.35f), outward);
    }
}
