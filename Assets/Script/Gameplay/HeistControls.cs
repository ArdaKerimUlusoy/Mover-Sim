using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Müzedeki oyuncunun yeni tuşları ve göstergeleri:
///   H : kayıt kamerasını aç / kapat (bakış açın kamyondaki ekrana gider — PlayerStealth.Camcorder, LiveFeed)
///   T : boya spreyi (basılı tut) — kameranın lensine nişan al → 30 sn kör; duvara nişan al → sahte kapı çiz, içine saklan
///   M : (kamyondayken) müzenin kat planı — FloorPlanUI
/// Sol altta kayıt / pil / dikkat göstergesi ve sprey sayısı. Sahneye bir şey eklemeye gerek yok.
/// </summary>
public class HeistControls : MonoBehaviour
{
    public const Key RecordKey = Key.H;
    public const Key SprayKey = Key.T;

    /// <summary>Yerel oyuncu şu an boya sıkıyor mu? (PlayerInteractor bunu "suç" sayar.)</summary>
    public static bool IsSpraying { get; private set; }

    private static HeistControls instance;

    private enum TargetKind { None, Camera, Wall }
    private TargetKind target;
    private RoomCamera targetCam;
    private Vector3 wallPoint, wallNormal;
    private float hold;
    private bool waitRelease;
    private TargetKind holdKind;
    private RoomCamera holdCam;
    private Vector3 holdWallPoint;

    private static readonly RaycastHit[] hits = new RaycastHit[8];
    private GUIStyle small, bold;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("HeistControls");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HeistControls>();
    }

    private static bool InputAllowed(PlayerStealth me)
    {
        if (ChatUI.IsTyping || SessionMenuUI.IsPaused || PlayerInventory.IsPanelOpen || WorkbenchUI.IsOpen || BriefingUI.IsOpen ||
            FloorPlanUI.IsOpen || PlayerController.InputLocked || LockpickSystem.IsPicking) return false;
        if (Cursor.lockState != CursorLockMode.Locked) return false;
        if (me.IsJailed || me.CurrentHidingSpot != null || PushSystem.LocalKnockedOut || me.IsKnockedOutNet) return false;
        if (MuseumSession.Instance != null && MuseumSession.Instance.SequenceActive) return false;
        return true;
    }

    private void Update()
    {
        PlayerStealth me = PlayerStealth.Local;
        PlayerInventory inv = me != null ? me.GetComponent<PlayerInventory>() : null;
        Keyboard kb = Keyboard.current;
        if (me == null || inv == null || !me.IsSpawned || kb == null) { IsSpraying = false; target = TargetKind.None; return; }

        bool allowed = InputAllowed(me);
        bool inTruck = FloorPlanUI.LocalInTruck;

        // ---- Kayıt kamerası ---- (birleştirme: kayıt BodyCam'de; bu kısım FeatureMerge.FriendCamcorder ile açılır)
        if (FeatureMerge.FriendCamcorder && allowed && kb[RecordKey].wasPressedThisFrame)
        {
            if (me.IsRecording) me.RequestRecording(false);
            else if (inTruck) inv.ShowLocalToast(Loc.T("cam.intruck"));
            else if (me.BatterySeconds <= 0.05f) inv.ShowLocalToast(Loc.T("cam.dead"));
            else me.RequestRecording(true);
        }

        // ---- Boya spreyi ---- (birleştirme: boya fırçada; bu kısım FeatureMerge.FriendSpray ile açılır)
        target = TargetKind.None;
        if (!FeatureMerge.FriendSpray) { IsSpraying = false; return; }
        if (allowed && !inTruck && inv.HasSpray) FindTarget();

        if (!kb[SprayKey].isPressed) { waitRelease = false; hold = 0f; IsSpraying = false; return; }
        if (waitRelease || !allowed || target == TargetKind.None) { hold = 0f; IsSpraying = false; return; }

        // Hedef değişirse baştan
        bool same = hold > 0f && holdKind == target && (target == TargetKind.Camera ? holdCam == targetCam : Vector3.Distance(holdWallPoint, wallPoint) < 0.6f);
        if (!same)
        {
            hold = 0f;
            holdKind = target;
            holdCam = targetCam;
            holdWallPoint = wallPoint;
        }
        hold += Time.deltaTime;
        IsSpraying = true;
        if (hold >= HoldDuration(target))
        {
            if (target == TargetKind.Camera && targetCam != null) inv.RequestSprayCamera(targetCam.name);
            else if (target == TargetKind.Wall) inv.RequestSprayDoor(wallPoint, wallNormal);
            waitRelease = true;
            hold = 0f;
            IsSpraying = false;
        }
    }

    private static float HoldDuration(TargetKind k) => k == TargetKind.Camera ? UpgradeCatalog.SprayCameraHold : UpgradeCatalog.SprayDoorHold;

    private void FindTarget()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 o = cam.transform.position, f = cam.transform.forward;

        // 1) Kamera lensi: nişangahın yakınında, menzilde ve arada engel yok
        float best = 8f;
        foreach (RoomCamera rc in RoomCamera.All)
        {
            if (rc == null || !MuseumSession.IsLevelActive(rc.requiredLevel) || rc.Blinded) continue;
            Vector3 to = rc.LensPosition - o;
            float d = to.magnitude;
            if (d > 6.5f || d < 0.2f) continue;
            float ang = Vector3.Angle(f, to);
            if (ang > best) continue;
            if (Physics.Raycast(o, to / d, d - 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue; // duvar / tavan arada
            best = ang;
            targetCam = rc;
            target = TargetKind.Camera;
        }
        if (target == TargetKind.Camera) return;

        // 2) Duvar: müzenin içinde, yakındaki dik bir duvar yüzeyi
        int n = Physics.RaycastNonAlloc(new Ray(o, f), hits, 2.6f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        RaycastHit first = default;
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].collider.GetComponentInParent<PlayerStealth>() == PlayerStealth.Local) continue; // kendi gövdem
            if (hits[i].distance < nearest) { nearest = hits[i].distance; first = hits[i]; any = true; }
        }
        if (!any || !PlayerInventory.IsPaintableWall(first.collider) || Mathf.Abs(first.normal.y) > 0.3f) return;
        MuseumLayout layout = MuseumLayout.Instance;
        if (layout == null || !layout.PlanBounds.Contains(new Vector2(first.point.x + first.normal.x * 0.5f, first.point.z + first.normal.z * 0.5f))) return;
        wallPoint = first.point;
        wallNormal = first.normal;
        target = TargetKind.Wall;
    }

    // ---------------- Arayüz ----------------

    private void OnGUI()
    {
        PlayerStealth me = PlayerStealth.Local;
        PlayerInventory inv = me != null ? me.GetComponent<PlayerInventory>() : null;
        if (me == null || inv == null || !me.IsSpawned || SessionMenuUI.IsPaused || WorkbenchUI.IsOpen || FloorPlanUI.IsOpen || me.IsJailed) return;
        if (small == null)
        {
            small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false };
            small.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            bold = new GUIStyle(small) { fontSize = 14, fontStyle = FontStyle.Bold };
        }
        if (!FeatureMerge.FriendCamcorder && !FeatureMerge.FriendSpray)
        {
            // Birleştirme: kayıt / boya göstergesi BodyCam ve fırçada. Kamyonda sadece kat planı ipucu kalır.
            if (FloorPlanUI.LocalInTruck)
            {
                string th = Loc.T("heist.hint.truck");
                Rect hr = UIUtil.HudRect(UIUtil.HudCorner.BottomLeft, UIUtil.HudOrder.TruckHint, small.CalcSize(new GUIContent(th)).x + 28f, 26f);
                UIUtil.Panel(hr, 0.85f);
                small.normal.textColor = UIUtil.Cream;
                GUI.Label(new Rect(hr.x + 13f, hr.y + 5f, hr.width - 18f, 16f), th, small);
            }
            return;
        }

        bool inTruck = FloorPlanUI.LocalInTruck;

        // Sol alt: kamera / pil / dikkat / sprey
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.BottomLeft, UIUtil.HudOrder.TruckHint, 270f, 98f);
        UIUtil.Panel(r);
        float cap = Mathf.Max(1f, me.BatteryCapacity);
        float bat = me.BatterySeconds;
        string batText = $"{Mathf.FloorToInt(bat / 60f)}:{Mathf.FloorToInt(bat % 60f):00}";
        if (me.IsRecording)
        {
            bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
            bold.normal.textColor = new Color(1f, 0.35f, 0.3f);
            GUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 20f), (blink ? "● " : "   ") + Loc.T("cam.rec", batText), bold);
        }
        else
        {
            bold.normal.textColor = Color.white;
            GUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 20f), Loc.T(inTruck ? "cam.charging" : "cam.off", batText), bold);
        }
        Rect bar = new Rect(r.x + 10f, r.y + 26f, r.width - 20f, 6f);
        UIUtil.DrawRect(bar, new Color(1f, 1f, 1f, 0.15f));
        UIUtil.DrawRect(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(bat / cap), bar.height), bat / cap < 0.25f ? new Color(1f, 0.4f, 0.3f) : new Color(0.45f, 0.85f, 0.5f));

        // Dikkat: kesintisiz kayıt süresi (9 sn'ye kadar kimse fark etmez)
        float grace = UpgradeCatalog.RecordGraceSeconds;
        float att = me.RecordAttention;
        string attText = att >= grace ? Loc.T("cam.noticed") : me.IsRecording ? Loc.T("cam.calm", Mathf.CeilToInt(grace - att)) : Loc.T("cam.ready");
        small.normal.textColor = att >= grace ? new Color(1f, 0.45f, 0.4f) : new Color(0.85f, 0.85f, 0.85f);
        GUI.Label(new Rect(r.x + 10f, r.y + 36f, r.width - 20f, 18f), attText, small);
        Rect abar = new Rect(r.x + 10f, r.y + 55f, r.width - 20f, 4f);
        UIUtil.DrawRect(abar, new Color(1f, 1f, 1f, 0.12f));
        UIUtil.DrawRect(new Rect(abar.x, abar.y, abar.width * Mathf.Clamp01(att / grace), abar.height), att >= grace ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.8f, 0.3f));

        small.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        GUI.Label(new Rect(r.x + 10f, r.y + 62f, r.width - 20f, 16f), Loc.T("spray.count", inv.SprayCount), small);
        GUI.Label(new Rect(r.x + 10f, r.y + 78f, r.width - 20f, 16f), Loc.T(inTruck ? "heist.hint.truck" : "heist.hint"), small);

        if (inTruck) return;

        // Eserin dibinde dikilme uyarısı
        if (me.LingerSeconds > UpgradeCatalog.LingerGraceSeconds - 2.5f)
        {
            bool bad = me.IsLingeringConspicuous;
            UIUtil.DrawPrompt(Loc.T(bad ? "linger.bad" : "linger.warn"), UIUtil.CenterSlot(26f) + 60f, bad ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.85f, 0.45f), 15);
        }

        // Sprey hedefi / basılı tutma halkası
        if (target == TargetKind.None) return;
        if (hold > 0f)
        {
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            UIUtil.DrawRing(c, 26f, hold / HoldDuration(target), new Color(0.35f, 0.95f, 0.45f));
            UIUtil.DrawPrompt(Loc.T(target == TargetKind.Camera ? "spray.doing.cam" : "spray.doing.wall"), UIUtil.CenterSlot(), new Color(0.5f, 1f, 0.55f), 17);
        }
        else
        {
            string key = target == TargetKind.Camera ? "spray.prompt.cam" : "spray.prompt.wall";
            UIUtil.DrawPrompt(Loc.T(key, inv.SprayCount), UIUtil.CenterSlot(), new Color(0.6f, 1f, 0.65f), 16);
        }
    }
}
