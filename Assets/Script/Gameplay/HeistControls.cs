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
        if (Event.current.type != EventType.Repaint) return;

        float sc = UITheme.Scale;
        float pad = UITheme.S3 * sc;
        if (!FeatureMerge.FriendCamcorder && !FeatureMerge.FriendSpray)
        {
            // Birleştirme: kayıt / boya göstergesi BodyCam ve fırçada. Kamyonda sadece kat planı ipucu kalır.
            if (FloorPlanUI.LocalInTruck)
            {
                string th = Loc.T("heist.hint.truck");
                float hw = HudKit.KeyLineWidth(th) + pad * 2f + 6f;
                Rect hr = UIUtil.HudRect(UIUtil.HudCorner.BottomLeft, UIUtil.HudOrder.TruckHint, hw, 30f * sc);
                UIUtil.Panel(hr, PanelKind.Plaque, 0.9f);
                HudKit.KeyLine(new Rect(hr.x + pad, hr.y + (hr.height - 22f * sc) * 0.5f, hw - pad * 2f, 22f * sc), th, UITheme.IvoryDim, true);
            }
            return;
        }

        bool inTruck = FloorPlanUI.LocalInTruck;
        float w = 300f * sc;
        float innerW = w - pad * 2f - 26f * sc; // sol simge sütunu
        float tx = 26f * sc;

        // Metinler (gerçek durumdan)
        float cap = Mathf.Max(1f, me.BatteryCapacity);
        float bat = me.BatterySeconds;
        string batText = $"{Mathf.FloorToInt(bat / 60f)}:{Mathf.FloorToInt(bat % 60f):00}";
        bool rec = me.IsRecording;
        string recText = rec ? Loc.T("cam.rec", batText) : Loc.T(inTruck ? "cam.charging" : "cam.off", batText);

        float grace = UpgradeCatalog.RecordGraceSeconds;
        float att = me.RecordAttention;
        bool noticed = att >= grace;
        string attText = noticed ? Loc.T("cam.noticed") : rec ? Loc.T("cam.calm", Mathf.CeilToInt(grace - att)) : Loc.T("cam.ready");
        string sprayText = Loc.T("spray.count", inv.SprayCount);
        string hintText = Loc.T(inTruck ? "heist.hint.truck" : "heist.hint");

        // Dikkat satırı yalnızca kayıttayken ya da dikkat birikmişken gösterilir (HUD sade kalsın)
        bool showAtt = rec || att > 0.05f;
        float recH = HudKit.KeyLine(new Rect(0f, 0f, innerW, 0f), recText, UITheme.Ivory, false, TextRole.BodyStrong);
        float attH = showAtt ? UIUtil.MeasureHeight(attText, TextRole.Caption, innerW) : 0f;
        float sprayH = HudKit.KeyLine(new Rect(0f, 0f, innerW + tx, 0f), sprayText, UITheme.IvoryDim, false);
        float hintH = HudKit.KeyLine(new Rect(0f, 0f, innerW + tx, 0f), hintText, UITheme.IvoryDim, false);
        float barH = 8f * sc;
        float h = pad + recH + 4f * sc + barH
                + (showAtt ? 6f * sc + attH + 3f * sc + 5f * sc : 0f)
                + 6f * sc + sprayH + hintH + pad;

        // Sol alt: kamera / pil / dikkat / sprey
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.BottomLeft, UIUtil.HudOrder.TruckHint, w, h);
        UIUtil.Panel(r, PanelKind.Plaque, 0.94f);
        float x = r.x + pad, y = r.y + pad;
        float iconS = 20f * sc;

        // Kayıt rozeti: kayıttayken kırmızı nabızlı Camera simgesi + REC rozeti
        Color camCol = rec ? Color.Lerp(UITheme.Danger, UITheme.DangerLight, UIUtil.Pulse(5f)) : UITheme.IvoryDim;
        UIUtil.Icon(new Rect(x, y + (recH - iconS) * 0.5f, iconS, iconS), IconId.Camera, camCol);
        HudKit.KeyLine(new Rect(x + tx, y, innerW, recH), recText, rec ? UITheme.DangerLight : UITheme.Ivory, true, TextRole.BodyStrong);
        y += recH + 4f * sc;

        // Pil: dolu Sage, %25 altı Danger
        UIUtil.ProgressBar(new Rect(x, y, w - pad * 2f, barH), Mathf.Clamp01(bat / cap), bat / cap < 0.25f ? UITheme.Danger : UITheme.Sage);
        y += barH;

        if (showAtt)
        {
            // Dikkat: kesintisiz kayıt süresi (9 sn'ye kadar kimse fark etmez)
            y += 6f * sc;
            UIUtil.Text(new Rect(x, y, w - pad * 2f, attH), attText, TextRole.Caption, noticed ? UITheme.DangerLight : UITheme.IvoryDim);
            y += attH + 3f * sc;
            UIUtil.ProgressBar(new Rect(x, y, w - pad * 2f, 5f * sc), Mathf.Clamp01(att / grace), noticed ? UITheme.Danger : UITheme.Ochre);
            y += 5f * sc;
        }

        y += 6f * sc;
        HudKit.KeyLine(new Rect(x, y, w - pad * 2f, sprayH), sprayText, UITheme.IvoryDim, true);
        y += sprayH;
        HudKit.KeyLine(new Rect(x, y, w - pad * 2f, hintH), hintText, UITheme.IvoryDim, true);

        if (inTruck) return;

        // Eserin dibinde dikilme uyarısı
        if (me.LingerSeconds > UpgradeCatalog.LingerGraceSeconds - 2.5f)
        {
            bool bad = me.IsLingeringConspicuous;
            UIUtil.DrawPrompt(Loc.T(bad ? "linger.bad" : "linger.warn"), UIUtil.CenterSlot(26f) + 60f, bad ? UITheme.DangerLight : UITheme.Ochre, 15);
        }

        // Sprey hedefi / basılı tutma halkası
        if (target == TargetKind.None) return;
        if (hold > 0f)
        {
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            UIUtil.DrawRing(c, 26f, hold / HoldDuration(target), UITheme.BrassLight);
            UIUtil.DrawPrompt(Loc.T(target == TargetKind.Camera ? "spray.doing.cam" : "spray.doing.wall"), UIUtil.CenterSlot(), UITheme.BrassLight, 17);
        }
        else
        {
            string key = target == TargetKind.Camera ? "spray.prompt.cam" : "spray.prompt.wall";
            UIUtil.DrawPrompt(Loc.T(key, inv.SprayCount), UIUtil.CenterSlot(), UITheme.Ivory, 16);
        }
    }
}
