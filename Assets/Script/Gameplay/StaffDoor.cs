using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Staff Only odasının kilitli kapısı (MuseumLayout her haritada kurar, ağ nesnesi yoktur).
/// Durum MuseumSession.StaffDoorOpen'dan okunur (herkeste aynı). Kilitliyken ne oyuncu ne güvenlik geçebilir
/// (güvenlikler kapıyı açamaz; NavMeshObstacle yolu keser). Sadece maymuncukla açılır.
/// Açma işi: LockpickSystem.
/// </summary>
public class StaffDoor : MonoBehaviour
{
    public static readonly List<StaffDoor> All = new List<StaffDoor>();

    private const float GuardOpenRange = 2.4f;
    private const float SwingSpeed = 2.2f; // 0→1 / sn

    private Transform hinge;
    private Collider block;
    private Transform lockLight;
    private float openYaw;
    private float amount;      // 0 kapalı, 1 açık (görsel)
    private bool lastWantOpen;
    private bool lastUnlocked;
    private MaterialPropertyBlock mpb;

    // Tabela / lamba / yazı
    private Transform lamp;
    private Light lampLight;
    private Vector3 signPos, signOut;
    private bool hasSign;
    private const float SignMaxDistance = 18f;

    private static readonly List<GuardAI> guards = new List<GuardAI>();
    private static float nextGuardScan;

    /// <summary>Kapı açıldı mı (maymuncukla)?</summary>
    public static bool Unlocked => MuseumSession.Instance != null && MuseumSession.Instance.StaffDoorOpen;

    public Vector3 Center => block != null ? block.bounds.center : transform.position + Vector3.up * 1.4f;

    public void Setup(Transform hingeT, Collider blocker, Transform led, float yaw)
    {
        hinge = hingeT;
        block = blocker;
        lockLight = led;
        openYaw = yaw;
        lastUnlocked = !Unlocked; // ilk karede ışığı boya
        Refresh(true);
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    /// <summary>Kapı üstü tabela: lamba, ışık, yazının dünya konumu, kapının dışa bakan yönü.</summary>
    public void SetupSign(Transform lampT, Light light, Vector3 worldPos, Vector3 localOutward)
    {
        lamp = lampT;
        lampLight = light;
        signPos = worldPos;
        signOut = transform.TransformDirection(localOutward);
        hasSign = true;
    }

    private void Update()
    {
        Refresh(false);
        UpdateLamp();
    }

    private void UpdateLamp()
    {
        if (lampLight == null) return;
        if (Unlocked)
        {
            lampLight.color = new Color(0.2f, 1f, 0.3f);
            lampLight.intensity = 0.8f;
            if (lamp != null) lamp.localScale = Vector3.one * 0.2f;
            return;
        }
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
        lampLight.color = new Color(1f, 0.15f, 0.1f);
        lampLight.intensity = 0.6f + 2.2f * pulse;
        if (lamp != null) lamp.localScale = Vector3.one * (0.18f + 0.06f * pulse);
    }

    /// <summary>Kapının üstünde "STAFF ONLY" yazısı (yalnızca dışarıdan ve duvar arkasından görünmez).</summary>
    private void OnGUI()
    {
        if (!hasSign || SessionMenuUI.IsPaused) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 toCam = cam.transform.position - signPos;
        float dist = toCam.magnitude;
        if (dist > SignMaxDistance || Vector3.Dot(toCam, signOut) < 0f) return; // odanın içinden / arkadan gösterme
        Vector3 sp = cam.WorldToScreenPoint(signPos);
        if (sp.z <= 0f) return;
        if (Physics.Linecast(cam.transform.position, signPos, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) &&
            hit.distance < dist - 0.5f && hit.collider.GetComponentInParent<PlayerStealth>() == null) return; // duvar arkası

        // Plaket tabela: Danger şeritli koyu kart, başlık BodyStrong, kilitliyse altında Caption (Ochre) ve Lock ikonu
        float sc = UITheme.Scale;
        float alpha = Mathf.Clamp01((SignMaxDistance - dist) / 4f);
        string title = Loc.T("label.staffonly");
        string sub = Unlocked ? null : Loc.T("label.staffonly.locked");
        GUIStyle ts = UITheme.Style(TextRole.BodyStrong, UITheme.DangerLight, TextAnchor.MiddleCenter);
        GUIStyle ss = UITheme.Style(TextRole.Caption, UITheme.Ochre, TextAnchor.MiddleCenter);
        float ic = Mathf.Round(18f * sc);
        float w = Mathf.Min(Screen.width - 20f, Mathf.Max(ts.CalcSize(new GUIContent(title)).x + ic + 40f,
                  sub != null ? ss.CalcSize(new GUIContent(sub)).x + 28f : 0f));
        float h = Mathf.Round((sub != null ? 52f : 32f) * sc);
        Rect r = new Rect(Mathf.Round(sp.x - w * 0.5f), Mathf.Round(Screen.height - sp.y - h * 0.5f), w, h);

        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        UIUtil.Panel(r, PanelKind.Plaque, 0.92f);
        UIUtil.DrawRect(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, 2f), UITheme.WithAlpha(UITheme.Danger, alpha));
        float rowH = Mathf.Round(26f * sc);
        float tw = ts.CalcSize(new GUIContent(title)).x;
        float tx = r.center.x - (tw + ic + 6f) * 0.5f;
        UIUtil.Icon(new Rect(tx, r.y + 6f + (rowH - ic) * 0.5f, ic, ic), IconId.Lock, UITheme.DangerLight);
        GUI.Label(new Rect(tx + ic + 6f, r.y + 6f, tw + 4f, rowH), title, ts);
        if (sub != null)
            GUI.Label(new Rect(r.x + 8f, r.y + 6f + rowH, r.width - 16f, Mathf.Round(18f * sc)), sub, ss);
        GUI.color = old;
    }

    private void Refresh(bool snap)
    {
        bool unlocked = Unlocked;
        bool wantOpen = unlocked; // güvenlikler kapıyı açamaz: sadece maymuncukla açılır

        if (block != null && block.enabled == unlocked)
        {
            block.enabled = !unlocked;
            // Kilitliyken güvenlikler de geçemez (NavMesh'te delik); açılınca yol açılır
            UnityEngine.AI.NavMeshObstacle ob = block.GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (ob != null) ob.enabled = !unlocked;
        }

        if (wantOpen != lastWantOpen && !snap)
            GameAudio.PlayAt(wantOpen ? SoundId.DoorOpen : SoundId.DoorClose, Center, 0.6f);
        lastWantOpen = wantOpen;

        float target = wantOpen ? 1f : 0f;
        amount = snap ? target : Mathf.MoveTowards(amount, target, SwingSpeed * Time.deltaTime);
        if (hinge != null) hinge.localRotation = Quaternion.Euler(0f, openYaw * Mathf.SmoothStep(0f, 1f, amount), 0f);

        if (unlocked != lastUnlocked && lockLight != null)
        {
            lastUnlocked = unlocked;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            Color c = unlocked ? new Color(0.15f, 0.9f, 0.25f) : new Color(0.9f, 0.1f, 0.1f);
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            foreach (Renderer r in lockLight.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
        }
    }

    private bool GuardNear()
    {
        if (Time.unscaledTime >= nextGuardScan)
        {
            nextGuardScan = Time.unscaledTime + 1f;
            guards.Clear();
            guards.AddRange(FindObjectsByType<GuardAI>(FindObjectsSortMode.None));
        }
        Vector3 c = transform.position;
        foreach (GuardAI g in guards)
        {
            if (g == null || !g.isActiveAndEnabled || !g.Online) continue;
            Vector3 p = g.transform.position;
            if (Mathf.Abs(p.y - c.y) > 2f) continue;
            p.y = c.y;
            if ((p - c).sqrMagnitude < GuardOpenRange * GuardOpenRange) return true;
        }
        return false;
    }
}
