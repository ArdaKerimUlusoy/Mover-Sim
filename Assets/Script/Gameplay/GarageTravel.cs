using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Garaj (ayrı sahne) ile kamyon arasındaki geçiş kapısı. Bakıp E'ye basınca yerel oyuncu karşı tarafa ışınlanır.
///  - ToTruck : garajın kapısı → kamyonun içindeki doğma noktaları (soyguna çıkış)
///  - ToGarage: (kullanılmıyor) görev ortasında garaja dönüş yok; garaja görev bitince dönülür
/// Ağ nesnesi gerektirmez: oyuncu hareketi zaten sahibinde (PlayerStealth.LocalTeleport), konum herkese NetworkTransform ile gider.
/// Tek ışın + tek tuş; her karede yalnızca yerel oyuncu için çalışır (ucuz).
/// </summary>
[RequireComponent(typeof(Collider))]
public class GarageTravel : MonoBehaviour
{
    public enum Destination { ToTruck, ToGarage }

    [SerializeField] private Destination destination = Destination.ToTruck;
    [SerializeField, Min(0.5f)] private float maxDistance = 3f;

    private static GarageTravel target;     // yerel oyuncunun şu an baktığı kapı
    private static int targetFrame = -1;
    private static Camera cachedCam;
    private static PlayerStealth cachedFor;
    private Collider col;
    private float nextAllowed;

    public void Setup(Destination d) => destination = d;

    private void Awake()
    {
        col = GetComponent<Collider>();
        // Görev ortasında garaja dönüş yok (garaja görev bitince, kamyon [K] ile kapanınca dönülür):
        // eski kurulumdan kalan kamyon içi "Garaja dön" kapısı oyunda kaldırılır
        if (destination == Destination.ToGarage) Destroy(gameObject);
    }

    private void OnDisable()
    {
        if (target == this) target = null;
    }

    private static Camera LocalCamera(PlayerStealth me)
    {
        if (cachedFor != me || cachedCam == null)
        {
            cachedFor = me;
            cachedCam = me != null ? me.GetComponentInChildren<Camera>(true) : null;
            if (cachedCam == null) cachedCam = Camera.main;
        }
        return cachedCam;
    }

    private static bool InputFree =>
        !ChatUI.IsTyping && !SessionMenuUI.IsPaused && !PlayerInventory.IsPanelOpen && !PlayerController.InputLocked &&
        !BodyCam.PanelOpen && !WorkbenchUI.IsOpen && !BriefingUI.IsOpen && Cursor.lockState == CursorLockMode.Locked;

    private void Update()
    {
        PlayerStealth me = PlayerStealth.Local;
        if (target == this) target = null;
        if (me == null || !me.IsSpawned || me.IsJailed || me.CurrentHidingSpot != null || col == null) return;

        Vector3 p = me.transform.position;
        Vector3 cp = col.ClosestPoint(p);
        if ((cp - p).sqrMagnitude > (maxDistance + 2f) * (maxDistance + 2f)) return; // uzaktaysak ışın bile atma

        Camera cam = LocalCamera(me);
        if (cam == null) return;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!col.Raycast(ray, out RaycastHit hit, maxDistance + 0.6f)) return;
        // Arada başka bir şey (duvar) varsa sayma
        if (Physics.Raycast(ray, out RaycastHit first, hit.distance - 0.05f, ~0, QueryTriggerInteraction.Ignore) &&
            first.collider != col && !first.collider.transform.IsChildOf(me.transform)) return;

        target = this;
        targetFrame = Time.frameCount;

        Keyboard kb = Keyboard.current;
        if (kb == null || !InputFree || !kb.eKey.wasPressedThisFrame || Time.unscaledTime < nextAllowed) return;
        nextAllowed = Time.unscaledTime + 1f;

        string why = BlockReason(me);
        if (why != null)
        {
            PlayerInventory inv = me.GetComponent<PlayerInventory>();
            if (inv != null) inv.ShowLocalToast(why);
            GameAudio.Play2D(SoundId.UiClick, 0.4f);
            return;
        }
        Travel(me);
    }

    /// <summary>Geçiş şu an yasaksa nedeni (çeviri metni), serbestse null.</summary>
    private string BlockReason(PlayerStealth me)
    {
        MuseumSession ms = MuseumSession.Instance;
        if (ms != null && ms.SequenceActive) return Loc.T("garage.travel.busy");
        if (destination == Destination.ToGarage)
        {
            SecurityManager sec = SecurityManager.Instance;
            if (sec != null && sec.AlarmActive) return Loc.T("garage.travel.alarm");
            if (GarageSpawnPoint.All.Count == 0) return Loc.T("garage.travel.none");
        }
        else if (SpawnPoint.All.Count == 0) return Loc.T("garage.travel.none");
        ObjectCarrier carrier = me.GetComponent<ObjectCarrier>();
        if (carrier != null && carrier.IsHolding) return Loc.T("garage.travel.carry");
        return null;
    }

    private void Travel(PlayerStealth me)
    {
        Transform t;
        if (destination == Destination.ToGarage)
        {
            GarageSpawnPoint gp = GarageSpawnPoint.For(me.OwnerClientId);
            t = gp != null ? gp.transform : null;
        }
        else
        {
            SpawnPoint sp = SpawnPoint.All[(int)(me.OwnerClientId % (ulong)SpawnPoint.All.Count)];
            t = sp.transform;
        }
        if (t == null) return;
        GameAudio.Play2D(SoundId.UiClick, 0.6f);
        me.LocalTeleport(t.position + Vector3.up, t.eulerAngles.y);
        target = null;
    }

    private void OnGUI()
    {
        if (target != this || targetFrame < Time.frameCount - 1 || !InputFree) return;
        if (Event.current.type != EventType.Repaint) return;
        string key = destination == Destination.ToTruck ? "garage.exit.prompt" : "garage.return.prompt";
        UIUtil.DrawPrompt(Loc.T(key), UIUtil.CenterSlot(), Color.white);
    }
}
