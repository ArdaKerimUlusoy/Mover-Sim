using Unity.Netcode;
using UnityEngine;

/// <summary>
/// KAYIT KAMERASI (müzedeki oyuncu) + "eserin başında dikilme" şüphesi.
///  - Müzedeki oyuncu H ile kaydı açar/kapatır: bakış açısı kamyondaki ekrana / atölyeye canlı gelir (LiveFeed).
///    Kamyondaki kişi orijinali artık yanında görmez; bu ekrana bakarak çizer.
///  - Kayıt sırasında kafasında kırmızı KAYIT IŞIĞI yanıp söner (herkes görür).
///    İlk UpgradeCatalog.RecordGraceSeconds (9 sn) kimse dikkat etmez; sonra kayıt "göze batar":
///    görülürse şüphe dolar, ışığı gören ziyaretçi gidip güvenliğe söyler (şüphe artar, güvenlik bakmaya gelir).
///    Göze batınca kaydı bırakıp şüphe barı inene kadar beklemek gerekir; sonra yine 9 sn rahat kayıt.
///  - PİL: başlangıçta 1 dk (UpgradeCatalog.BatteryCapacity), kayıt açıkken azalır; kamyona dönünce kendiliğinden dolar.
///    Garaj mağazasından "Kamera Pili" upgrade'i ile uzar. Kamyonun içinde kayıt alınmaz.
///  - Bir eserin dibinde (1.7 m) UpgradeCatalog.LingerGraceSeconds'tan uzun durmak da şüphe çeker (görülürse).
/// Hepsi SUNUCU yetkili: istemci yalnızca "kaydı aç/kapat" ister; pil, dikkat ve süreleri sunucu sayar.
/// </summary>
public partial class PlayerStealth
{
    private readonly NetworkVariable<bool> recording = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<ushort> batteryDeci = new NetworkVariable<ushort>((ushort)(UpgradeCatalog.BatteryBaseSeconds * 10f));
    private readonly NetworkVariable<byte> attentionDeci = new NetworkVariable<byte>(0); // kesintisiz kayıt süresi (sn × 10)
    private readonly NetworkVariable<byte> lingerDeci = new NetworkVariable<byte>(0);    // eser başında durma süresi (sn × 10)

    private const float LingerDistance = 1.7f;

    // Sunucu
    private float serverBattery = -1f;
    private float serverAttention;
    private float serverLinger;

    public bool IsRecording => recording.Value;
    public float BatterySeconds => batteryDeci.Value / 10f;
    public float BatteryCapacity => UpgradeCatalog.BatteryCapacity(inventory != null ? inventory.GetUpgradeLevel(UpgradeType.CameraBattery) : 0);
    /// <summary>Kesintisiz kayıt süresi (sn). RecordGraceSeconds'ı geçince kayıt göze batar.</summary>
    public float RecordAttention => attentionDeci.Value / 10f;
    public bool IsRecordingConspicuous => recording.Value && RecordAttention >= UpgradeCatalog.RecordGraceSeconds;
    public float LingerSeconds => lingerDeci.Value / 10f;
    public bool IsLingeringConspicuous => LingerSeconds >= UpgradeCatalog.LingerGraceSeconds;

    /// <summary>Kamyona canlı yayın yapan oyuncu (yerel oyuncu hariç tutulabilir). Birden çoksa sıradaki için index.</summary>
    public static PlayerStealth FindRecorder(int index, bool excludeLocal, out int count)
    {
        count = 0;
        PlayerStealth pick = null, first = null;
        foreach (PlayerStealth p in All)
        {
            if (p == null || !p.IsSpawned || !p.IsRecording) continue;
            if (excludeLocal && p == Local) continue;
            if (first == null) first = p;
            if (count == index) pick = p;
            count++;
        }
        return pick != null ? pick : first;
    }

    // ---------------- Sahibi ----------------

    /// <summary>Sahibi: kaydı aç / kapat (H).</summary>
    public void RequestRecording(bool on)
    {
        if (IsOwner && IsSpawned) SetRecordingRpc(on);
    }

    [Rpc(SendTo.Server)]
    private void SetRecordingRpc(bool on, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (!on) { recording.Value = false; return; }
        if (!ServerCanRecord(out string why))
        {
            if (inventory != null) inventory.ServerNotify(why);
            return;
        }
        recording.Value = true;
        if (inventory != null) inventory.ServerPlaySound(SoundId.UiClick, transform.position);
    }

    // ---------------- Sunucu ----------------

    private bool InTruck => TruckZone.Instance != null && TruckZone.Instance.Contains(transform.position);

    private bool ServerCanRecord(out string reasonKey)
    {
        reasonKey = "";
        if (IsJailed || hidden.Value || knockedOut.Value) { reasonKey = "cam.cant"; return false; }
        if (InTruck) { reasonKey = "cam.intruck"; return false; }
        if (serverBattery <= 0.05f) { reasonKey = "cam.dead"; return false; }
        return true;
    }

    private void ServerInitCamcorder()
    {
        serverBattery = BatteryCapacity;
        batteryDeci.Value = (ushort)Mathf.RoundToInt(serverBattery * 10f);
        recording.Value = false;
    }

    private void ServerTickCamcorder()
    {
        float dt = Time.deltaTime;
        float cap = BatteryCapacity;
        if (serverBattery < 0f) serverBattery = cap;
        bool inTruck = InTruck;

        if (recording.Value)
        {
            if (!ServerCanRecord(out _)) recording.Value = false;
            else
            {
                serverBattery -= dt;
                if (serverBattery <= 0f)
                {
                    serverBattery = 0f;
                    recording.Value = false;
                    if (inventory != null) inventory.ServerNotify("cam.dead");
                }
            }
        }
        else if (inTruck) serverBattery = Mathf.Min(cap, serverBattery + UpgradeCatalog.BatteryRechargePerSecond * dt); // kamyonda şarj olur
        serverBattery = Mathf.Clamp(serverBattery, 0f, cap);

        // Kesintisiz kayıt süresi: kayıtta artar. Göze battıysa (süre doldu) şüphe barı inmeden sıfırlanmaz:
        // kaydı bırak, gözden uzak dur, bar insin → yine birkaç saniye rahat kayıt.
        if (recording.Value) serverAttention = Mathf.Min(25f, serverAttention + dt);
        else if (serverAttention < UpgradeCatalog.RecordGraceSeconds || suspicion.Value < 0.15f)
            serverAttention = Mathf.Max(0f, serverAttention - dt * 1.5f);

        // Eserin dibinde dikilme
        bool near = false;
        if (!IsJailed && !hidden.Value)
        {
            Vector3 me = transform.position;
            foreach (ArtworkDisplay d in ArtworkDisplay.All)
            {
                if (d == null || !d.IsActiveForMission) continue;
                Vector3 to = d.transform.position - me;
                to.y = 0f;
                if (to.sqrMagnitude < LingerDistance * LingerDistance) { near = true; break; }
            }
        }
        serverLinger = near ? Mathf.Min(25f, serverLinger + dt) : Mathf.Max(0f, serverLinger - dt * 2f);

        ushort b = (ushort)Mathf.RoundToInt(serverBattery * 10f);
        if (batteryDeci.Value != b) batteryDeci.Value = b;
        byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(serverAttention * 10f), 0, 255);
        if (attentionDeci.Value != a) attentionDeci.Value = a;
        byte l = (byte)Mathf.Clamp(Mathf.RoundToInt(serverLinger * 10f), 0, 255);
        if (lingerDeci.Value != l) lingerDeci.Value = l;
    }

    /// <summary>Sunucu: şüpheyi doğrudan artırır (ör. kayıt ışığını gören ziyaretçi güvenliğe söyledi). Dolarsa alarm.</summary>
    public void ServerAddSuspicion(float amount)
    {
        if (!IsServer || IsJailed || hidden.Value) return;
        suspicion.Value = Mathf.Min(1f, suspicion.Value + amount);
        lastSeenTime = Time.time; // hemen azalmaya başlamasın
        if (suspicion.Value >= 1f && SecurityManager.Instance != null)
            SecurityManager.Instance.ServerTriggerAlarm(transform.position, "alarm.recorded", this);
    }

    // ---------------- Kayıt ışığı (herkeste) ----------------

    private Transform recLight;
    private Material recMat;

    private void TickRecLight()
    {
        if (recLight == null)
        {
            if (!recording.Value) return;
            PlayerAvatar avatar = GetComponentInChildren<PlayerAvatar>();
            Transform head = avatar != null ? avatar.HeadBone : null;
            if (head == null) return;

            // Kafanın yanında küçük kamera gövdesi + kırmızı ışık
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "BodyCam";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(head, false);
            body.transform.localPosition = new Vector3(0.3f, 0.22f, 0.12f);
            body.transform.localScale = new Vector3(0.1f, 0.1f, 0.18f);
            recMat = ArtworkLook.MakeLit(new Color(1f, 0.1f, 0.08f));
            if (recMat.HasProperty("_EmissionColor"))
            {
                recMat.EnableKeyword("_EMISSION");
                recMat.SetColor("_EmissionColor", new Color(3f, 0.2f, 0.15f));
            }
            Material dark = ArtworkLook.MakeLit(new Color(0.08f, 0.08f, 0.09f));
            body.GetComponent<Renderer>().sharedMaterial = dark;

            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "RecLight";
            Destroy(dot.GetComponent<Collider>());
            dot.transform.SetParent(body.transform, false);
            dot.transform.localPosition = new Vector3(0f, 0.55f, 0.35f);
            dot.transform.localScale = new Vector3(0.6f, 0.6f, 0.35f);
            dot.GetComponent<Renderer>().sharedMaterial = recMat;
            recLight = body.transform;
            // Kendi ekranında kafandaki kamera görüşünü kapatmasın (yalnızca gölgesi)
            if (IsOwner)
                foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        bool on = recording.Value && !hidden.Value;
        if (recLight.gameObject.activeSelf != on) recLight.gameObject.SetActive(on);
        if (on)
        {
            Transform dot = recLight.Find("RecLight");
            // Rahat kayıtta yavaş, göze batınca hızlı yanıp söner
            float period = IsRecordingConspicuous ? 0.5f : 1.2f;
            if (dot != null) dot.gameObject.SetActive(Mathf.Repeat(Time.time, period) < period * 0.6f);
        }
    }
}
