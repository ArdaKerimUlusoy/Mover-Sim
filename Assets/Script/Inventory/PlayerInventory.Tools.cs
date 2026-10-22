using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Aletler: BOYA SPREYİ (adet) ve KAYIT KAMERASI PİLİ (upgrade seviyesi). Garaj mağazasından alınır.
/// Sprey (SprayTool, T basılı tut):
///   - Oda kamerasının lensine sık → kamera UpgradeCatalog.SprayCameraBlindSeconds (30 sn) kör (kamera odası kaldırıldı).
///   - Müze duvarına sık → duvara SAHTE KAPI çizilir (HidingSpot); içine girip saklanılır (dolaplar kaldırıldı).
/// Her sıkış bir kutu harcar. Sıkarken suç işliyorsun: güvenlik / ziyaretçi görürse tepki verir.
/// Hepsi SUNUCU yetkili: istemci hedefi söyler, sunucu mesafeyi, kutuyu ve duvarı doğrular.
/// </summary>
public partial class PlayerInventory
{
    private readonly NetworkVariable<byte> sprayCount = new NetworkVariable<byte>(0);   // boya spreyi kutusu
    private readonly NetworkVariable<byte> batteryLevel = new NetworkVariable<byte>(0); // kamera pili upgrade'i

    public int SprayCount => sprayCount.Value;
    public bool HasSpray => sprayCount.Value > 0;

    private const float SprayCameraRange = 6.5f;
    private const float SprayWallRange = 4f;
    private float lastSprayTime = -10f;

    /// <summary>Duvar sayılan çarpıştırıcı (müzenin iç bölmeleri ve dış duvarları).</summary>
    public static bool IsPaintableWall(Collider c)
    {
        if (c == null || c.isTrigger) return false;
        string n = c.gameObject.name;
        return n.StartsWith("Divider_R") || n.StartsWith("Wall_");
    }

    // ---------------- İstemci ----------------

    public void RequestSprayCamera(string cameraName)
    {
        if (IsOwner && IsSpawned && !string.IsNullOrEmpty(cameraName)) SprayCameraRpc(cameraName);
    }

    public void RequestSprayDoor(Vector3 point, Vector3 normal)
    {
        if (IsOwner && IsSpawned) SprayDoorRpc(point, normal);
    }

    // ---------------- Sunucu ----------------

    private bool ServerSprayAllowed()
    {
        if (rpcSenderInvalid) return false;
        if (sprayCount.Value == 0) { ServerNotify("spray.none"); return false; }
        if (Time.time - lastSprayTime < 0.5f) return false;
        PlayerStealth st = GetComponent<PlayerStealth>();
        if (st != null && (st.IsJailed || st.IsHidden)) return false;
        return true;
    }

    private bool rpcSenderInvalid;

    [Rpc(SendTo.Server)]
    private void SprayCameraRpc(string cameraName, RpcParams rpcParams = default)
    {
        rpcSenderInvalid = rpcParams.Receive.SenderClientId != OwnerClientId;
        if (!ServerSprayAllowed()) return;

        RoomCamera cam = RoomCamera.Find(cameraName);
        SecurityManager sm = SecurityManager.Instance;
        if (cam == null || sm == null || !MuseumSession.IsLevelActive(cam.requiredLevel)) return;
        if (Vector3.Distance(transform.position, cam.LensPosition) > SprayCameraRange + 1f) return;
        if (sm.IsCameraBlinded(cameraName)) { ServerNotify("spray.already"); return; }

        sprayCount.Value--;
        lastSprayTime = Time.time;
        PlayerNetworkSetup ps = GetComponent<PlayerNetworkSetup>();
        sm.ServerBlindCamera(cameraName, UpgradeCatalog.SprayCameraBlindSeconds, ps != null ? ps.PlayerName : "");
        ServerNotify("spray.camdone", Mathf.RoundToInt(UpgradeCatalog.SprayCameraBlindSeconds).ToString());
        ServerPlaySound(SoundId.CoinThrow, transform.position);
        NoiseSystem.Emit(transform.position, 3f);
    }

    [Rpc(SendTo.Server)]
    private void SprayDoorRpc(Vector3 point, Vector3 normal, RpcParams rpcParams = default)
    {
        rpcSenderInvalid = rpcParams.Receive.SenderClientId != OwnerClientId;
        if (!ServerSprayAllowed()) return;

        normal.y = 0f;
        if (normal.sqrMagnitude < 0.5f) return;
        normal.Normalize();
        // Eksene hizala (müzenin duvarları eksen hizalı)
        normal = Mathf.Abs(normal.x) > Mathf.Abs(normal.z) ? new Vector3(Mathf.Sign(normal.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(normal.z));

        Vector3 flat = point - transform.position;
        flat.y = 0f;
        if (flat.magnitude > SprayWallRange + 1f) return;

        MuseumLayout layout = MuseumLayout.Instance;
        if (layout == null) return;
        Rect b = layout.PlanBounds;
        if (!b.Contains(new Vector2(point.x, point.z)) && !b.Contains(new Vector2(point.x + normal.x, point.z + normal.z))) return;

        HidingSpot spot = HidingSpot.ServerFindFree();
        if (spot == null) { ServerNotify("spray.nodoorslot"); return; }
        float cy = spot.DoorCenterY;

        // Duvarı doğrula: kapının ortasından ve 4 köşesinden duvara ışın (kapı duvara sığmalı, kapı boşluğuna denk gelmemeli)
        Vector3 side = Vector3.Cross(Vector3.up, normal);
        Vector3 basePos = new Vector3(point.x, cy, point.z);
        Vector3 wallPoint = Vector3.zero;
        float[] dx = { 0f, -0.45f, 0.45f, -0.45f, 0.45f };
        float[] dy = { 0f, -0.9f, -0.9f, 0.9f, 0.9f };
        for (int i = 0; i < dx.Length; i++)
        {
            Vector3 o = basePos + side * dx[i] + Vector3.up * dy[i] + normal * 0.6f;
            if (!Physics.Raycast(o, -normal, out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore) || !IsPaintableWall(hit.collider))
            {
                ServerNotify("spray.badwall");
                return;
            }
            if (i == 0) wallPoint = hit.point;
        }
        if (HidingSpot.ServerDoorNear(wallPoint, 2.2f)) { ServerNotify("spray.toonear"); return; }

        sprayCount.Value--;
        lastSprayTime = Time.time;
        float yaw = Mathf.Atan2(normal.x, normal.z) * Mathf.Rad2Deg;
        spot.ServerPaint(new Vector3(wallPoint.x, cy, wallPoint.z) + normal * 0.03f, yaw, UpgradeCatalog.SprayDoorSeconds);
        ServerNotify("spray.doordone", Mathf.RoundToInt(UpgradeCatalog.SprayDoorSeconds).ToString());
        ServerPlaySound(SoundId.CoinThrow, transform.position);
        NoiseSystem.Emit(transform.position, 3f);
    }
}
