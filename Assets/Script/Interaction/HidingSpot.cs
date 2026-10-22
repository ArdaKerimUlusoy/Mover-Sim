using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SAHTE KAPI (eskiden dolap). Müzede artık dolap yok: boya spreyiyle (SprayTool) duvara sahte bir kapı çizilir,
/// bu obje o kapı olur. Sahnedeki eski dolap objeleri (ağ kimlikleri için) "boş kapı yuvası" olarak bekler:
/// boyanmamışken görünmez ve etkileşime kapalıdır, sunucu boyanınca konumunu / yönünü / bitiş anını herkese senkronlar.
/// Kapı UpgradeCatalog.SprayDoorSeconds sonra silinir (içindeki oyuncu dışarı çıkar).
/// Saklanma noktası (GDD Bölüm 5). E ile girilir, E ile çıkılır.
/// İçerideyken güvenlikler ve kameralar seni göremez, diğer oyuncular da görmez.
/// Aynı anda tek kişi saklanabilir.
/// Kurulum: dolap objesine NetworkObject + bu bileşen. İç ve çıkış noktaları otomatik hesaplanır
/// (istersen "Inside Point" / "Exit Point" için boş objeler verebilirsin).
/// </summary>
public class HidingSpot : Interactable
{
    private const ulong Nobody = ulong.MaxValue;

    [SerializeField] private Transform insidePoint;
    [SerializeField] private Transform exitPoint;

    private readonly NetworkVariable<ulong> occupant = new NetworkVariable<ulong>(Nobody);

    // Boyanmış sahte kapı: bitiş anı (sunucu saati, 0 = boyanmamış), duvardaki konumu ve odaya bakan yönü
    private readonly NetworkVariable<double> paintedUntil = new NetworkVariable<double>(0);
    private readonly NetworkVariable<Vector3> paintPos = new NetworkVariable<Vector3>(Vector3.zero);
    private readonly NetworkVariable<float> paintYaw = new NetworkVariable<float>(0f);

    private bool shownPainted = true;
    private Vector3 shownPos;
    private float shownYaw = float.NaN;
    private float floorLocalY = float.NaN;
    private Vector3 originalScale;
    private Transform knob;
    private MaterialPropertyBlock block;

    private static readonly Vector3 DoorScale = new Vector3(1.0f, 2.1f, 0.04f);
    private static readonly Color DoorColor = new Color(0.2f, 0.13f, 0.08f);

    private double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : 0;

    /// <summary>Şu an duvarda boyalı bir sahte kapı mı? (Değilse görünmez, kullanılamaz.)</summary>
    public bool IsPainted => IsSpawned && paintedUntil.Value > 0 && Now < paintedUntil.Value;
    public float PaintRemaining => IsPainted ? (float)(paintedUntil.Value - Now) : 0f;

    /// <summary>Sunucu: boşta (boyanmamış ve içi boş) bir kapı yuvası.</summary>
    public static HidingSpot ServerFindFree()
    {
        foreach (HidingSpot h in All)
            if (h != null && h.IsSpawned && h.IsServer && !h.IsPainted && !h.IsOccupied) return h;
        return null;
    }

    /// <summary>Sunucu: bu noktada (yakınında) zaten boyalı bir kapı var mı?</summary>
    public static bool ServerDoorNear(Vector3 pos, float radius)
    {
        foreach (HidingSpot h in All)
            if (h != null && h.IsPainted && Vector3.Distance(h.paintPos.Value, pos) < radius) return true;
        return false;
    }

    /// <summary>Kapının zemin yüksekliği (dünya y): eski dolabın sahnedeki yüksekliği.</summary>
    public float DoorCenterY
    {
        get
        {
            if (float.IsNaN(floorLocalY)) floorLocalY = transform.localPosition.y;
            return transform.parent != null ? transform.parent.TransformPoint(new Vector3(0f, floorLocalY, 0f)).y : floorLocalY;
        }
    }

    /// <summary>Sunucu: duvara sahte kapı çiz (pos: duvar yüzeyinin hemen önü, yaw: odaya bakan yön).</summary>
    public void ServerPaint(Vector3 pos, float yaw, float seconds)
    {
        if (!IsServer) return;
        paintPos.Value = pos;
        paintYaw.Value = yaw;
        paintedUntil.Value = Now + seconds;
        ApplyPlacement(true);
    }

    /// <summary>Sunucu: kapıyı sil (içindeki dışarı çıkar).</summary>
    public void ServerErase()
    {
        if (!IsServer) return;
        PlayerStealth occ = ServerOccupant();
        if (occ != null) ServerExit(occ, occ.OwnerClientId, occ.NetworkObject);
        else occupant.Value = Nobody;
        paintedUntil.Value = 0;
        ApplyPlacement(false);
    }

    private void Awake()
    {
        originalScale = transform.localScale;
        floorLocalY = transform.localPosition.y;
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer && paintedUntil.Value > 0 && Now >= paintedUntil.Value) ServerErase(); // boya silindi
        ApplyPlacement(IsPainted);
    }

    /// <summary>Boyalıysa duvarda düz koyu bir kapı; değilse görünmez ve çarpışmasız (zeminin altına park edilir).</summary>
    private void ApplyPlacement(bool painted)
    {
        Vector3 pos = painted ? paintPos.Value : new Vector3(transform.position.x, -60f, transform.position.z);
        float yaw = painted ? paintYaw.Value : 0f;
        if (painted == shownPainted && pos == shownPos && Mathf.Approximately(yaw, shownYaw)) return;
        shownPainted = painted;
        shownPos = pos;
        shownYaw = yaw;

        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        transform.localScale = painted ? DoorScale : originalScale;
        foreach (Collider c in GetComponents<Collider>()) c.enabled = painted;
        Renderer r = GetComponent<Renderer>();
        if (r != null)
        {
            r.enabled = painted;
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", DoorColor);
            block.SetColor("_Color", DoorColor);
            r.SetPropertyBlock(block);
        }
        if (painted && knob == null)
        {
            GameObject k = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            k.name = "PaintedKnob";
            Destroy(k.GetComponent<Collider>());
            k.transform.SetParent(transform, false);
            // Ebeveyn düz ve ince ölçekli: topuz yuvarlak kalsın diye ölçek telafisi
            k.transform.localScale = new Vector3(0.08f / DoorScale.x, 0.08f / DoorScale.y, 0.08f / DoorScale.z);
            k.transform.localPosition = new Vector3(0.36f, 0f, 0.6f);
            Renderer kr = k.GetComponent<Renderer>();
            if (r != null) kr.sharedMaterial = r.sharedMaterial;
            var kb = new MaterialPropertyBlock();
            kb.SetColor("_BaseColor", new Color(0.85f, 0.7f, 0.3f));
            kb.SetColor("_Color", new Color(0.85f, 0.7f, 0.3f));
            kr.SetPropertyBlock(kb);
            knob = k.transform;
        }
        if (knob != null) knob.gameObject.SetActive(painted);
    }

    public static readonly System.Collections.Generic.List<HidingSpot> All = new System.Collections.Generic.List<HidingSpot>();
    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    /// <summary>Sunucu: içinde bu oyuncu varsa dolabı açıp onu dışarı çıkarır (güvenlik saklandığını gördü). Çıkardıysa true.</summary>
    public bool ServerEvict(PlayerStealth stealth)
    {
        if (!IsServer || stealth == null || occupant.Value != stealth.OwnerClientId) return false;
        ServerExit(stealth, stealth.OwnerClientId, stealth.NetworkObject);
        return true;
    }

    /// <summary>Sunucu: bütün dolaplardaki oyuncuları çıkarır (doluluk + hidden temizlenir, sahibine ExitRpc gider:
    /// kontroller geri gelir). Yeni görev başında çağrılır; ardından oyuncular ayrıca ışınlanır.</summary>
    public static void ServerReleaseAll()
    {
        foreach (HidingSpot h in new System.Collections.Generic.List<HidingSpot>(All))
        {
            if (h == null || !h.IsSpawned || !h.IsServer) continue;
            if (h.IsOccupied)
            {
                PlayerStealth occ = h.ServerOccupant();
                if (occ != null) h.ServerExit(occ, occ.OwnerClientId, occ.NetworkObject);
                else h.occupant.Value = Nobody; // sahibi bulunamadı: doluluğu yine de temizle
            }
            h.paintedUntil.Value = 0; // yeni görev: duvardaki boyalı kapılar silinir
        }
    }

    /// <summary>Sunucu: bu oyuncunun saklandığı yer (yoksa null).</summary>
    public static HidingSpot FindOccupiedBy(PlayerStealth stealth)
    {
        if (stealth == null) return null;
        foreach (HidingSpot h in All)
            if (h != null && h.IsSpawned && h.occupant.Value == stealth.OwnerClientId) return h;
        return null;
    }

    public bool IsOccupied => occupant.Value != Nobody;

    /// <summary>Sunucu: içinde saklanan oyuncu (yoksa null). Güvenlik dolabı açınca kullanılır.</summary>
    public PlayerStealth ServerOccupant()
    {
        if (occupant.Value == Nobody) return null;
        foreach (PlayerStealth p in PlayerStealth.All)
            if (p != null && p.IsSpawned && p.OwnerClientId == occupant.Value) return p;
        return null;
    }

    /// <summary>Herkes: dolabı bir güvenlik açtı (kapı sesi).</summary>
    public void ServerPlayOpened()
    {
        if (IsServer) OpenedRpc();
    }

    [Rpc(SendTo.Everyone)]
    private void OpenedRpc() => GameAudio.PlayAt(SoundId.DoorOpen, transform.position, 0.8f);

    public override void OnNetworkSpawn()
    {
        if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        shownPainted = !IsPainted; // ilk karede yerleşimi mutlaka uygula
        ApplyPlacement(IsPainted);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnClientDisconnected(ulong id)
    {
        if (occupant.Value == id) occupant.Value = Nobody;
    }

    // Sahte kapının "içi": kapının hemen önü (duvarın içine gömülmesin); başkaları seni göremez
    private Vector3 InsidePos => insidePoint != null ? insidePoint.position : transform.position + transform.forward * 0.12f + Vector3.up * 0.1f;
    private Vector3 ExitPos => exitPoint != null ? exitPoint.position : transform.position + transform.forward * 1.2f + Vector3.up * 0.2f;
    private float FacingYaw => transform.eulerAngles.y;

    public override bool CanInteract(GameObject player)
    {
        if (!base.CanInteract(player)) return false;
        if (!IsPainted) return false; // boyanmamış kapı yuvası
        if (!IsOccupied) return true;
        NetworkObject no = player != null ? player.GetComponent<NetworkObject>() : null;
        return no != null && no.OwnerClientId == occupant.Value;
    }

    public override string GetPrompt(PlayerInteractor player)
    {
        if (!IsPainted) return "";
        if (IsOccupied) return Loc.T("hide.occupied");
        return Loc.T("hide.door", Mathf.CeilToInt(PaintRemaining));
    }

    protected override void ServerInteract(ulong clientId, NetworkObject player)
    {
        PlayerStealth stealth = player.GetComponent<PlayerStealth>();
        if (stealth == null) return;

        if (occupant.Value == Nobody)
        {
            if (!IsPainted) return;
            occupant.Value = clientId;
            stealth.ServerSetHidden(true);
            EnterRpc(player, InsidePos, FacingYaw, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
        else if (occupant.Value == clientId)
        {
            ServerExit(stealth, clientId, player);
        }
    }

    private void ServerExit(PlayerStealth stealth, ulong clientId, NetworkObject player)
    {
        occupant.Value = Nobody;
        stealth.ServerSetHidden(false);
        ExitRpc(player, ExitPos, FacingYaw, RpcTarget.Single(clientId, RpcTargetUse.Temp)); // konum sunucudan: kapı bu arada silinse de doğru yere çıkar
    }

    /// <summary>Saklanan oyuncu içeriden E'ye basınca (dolabın içinden ışın dolabı göremez).</summary>
    public void RequestExit()
    {
        if (IsSpawned) ExitRequestRpc();
    }

    [Rpc(SendTo.Server)]
    private void ExitRequestRpc(RpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        if (occupant.Value != id) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out NetworkClient client) || client.PlayerObject == null) return;
        PlayerStealth stealth = client.PlayerObject.GetComponent<PlayerStealth>();
        if (stealth != null) ServerExit(stealth, id, client.PlayerObject);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void EnterRpc(NetworkObjectReference playerRef, Vector3 pos, float yaw, RpcParams rpcParams)
    {
        if (!playerRef.TryGet(out NetworkObject no)) return;
        PlayerStealth stealth = no.GetComponent<PlayerStealth>();
        if (stealth == null) return;
        stealth.LocalSetHidingSpot(this, pos, yaw);
        GameAudio.PlayAt(SoundId.DoorClose, transform.position, 0.6f);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ExitRpc(NetworkObjectReference playerRef, Vector3 pos, float yaw, RpcParams rpcParams)
    {
        if (!playerRef.TryGet(out NetworkObject no)) return;
        PlayerStealth stealth = no.GetComponent<PlayerStealth>();
        if (stealth == null) return;
        stealth.LocalSetHidingSpot(null, pos, yaw);
        GameAudio.PlayAt(SoundId.DoorOpen, transform.position, 0.6f);
    }
}
