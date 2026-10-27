using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// İşaretleme (ping): orta fare tuşu veya F → baktığın yere ekip arkadaşlarının da gördüğü bir işaret koyar.
/// (Önünde itilebilecek biri varsa F itmedir, işaret koymaz — PushSystem.)
/// Bir eşyaya / esere bakıyorsan işaretin üstünde adı yazar. İşaretler 8 sn sonra kaybolur.
/// Kurulum: sahnedeki ChatManager objesine eklenir (Test Museum oluşturucu otomatik ekler).
/// </summary>
public class PingSystem : NetworkBehaviour
{
    [SerializeField] private float maxDistance = 40f;
    [SerializeField] private float lifetime = 8f;
    [SerializeField] private float cooldown = 0.6f;

    private struct Ping
    {
        public Vector3 Position;
        public ulong Owner;
        public int ItemIndex;
        public float Until;
    }

    private readonly Dictionary<ulong, Ping> pings = new Dictionary<ulong, Ping>();
    private readonly Dictionary<ulong, float> serverCooldowns = new Dictionary<ulong, float>();
    private float nextLocalPing;

    private void Update()
    {
        if (!IsSpawned || PlayerInventory.Local == null) return;
        if (ChatUI.IsTyping || SessionMenuUI.IsPaused || PlayerInventory.IsPanelOpen || PlayerController.InputLocked) return;
        if (Cursor.lockState != CursorLockMode.Locked || Time.unscaledTime < nextLocalPing) return;

        bool pressed = (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame && !PlayerAvatar.PreviewActive) ||
                       (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame && !PushSystem.HasTargetInFront);
        if (!pressed) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Ignore))
            return;

        int itemIndex = -1;
        ItemDatabase db = ItemDatabase.Instance;
        if (db != null)
        {
            WorldItem wi = hit.collider.GetComponentInParent<WorldItem>();
            ArtworkDisplay display = hit.collider.GetComponentInParent<ArtworkDisplay>();
            if (wi != null && wi.Item != null) itemIndex = db.IndexOf(wi.Item);
            else if (display != null && display.Artwork != null) itemIndex = db.IndexOf(display.Artwork);
        }

        nextLocalPing = Time.unscaledTime + cooldown;
        PingRpc(hit.point + hit.normal * 0.05f, itemIndex);
    }

    /// <summary>Haritadan işaret (kamyon monitörü — BodyCam): verilen dünya noktasına ekibin gördüğü bir işaret koyar.</summary>
    public bool RequestPingAt(Vector3 position, int itemIndex = -1)
    {
        if (!IsSpawned || Time.unscaledTime < nextLocalPing) return false;
        nextLocalPing = Time.unscaledTime + cooldown;
        PingRpc(position, itemIndex);
        return true;
    }

    /// <summary>Haritadan (kamyon, kat planı) bir güvenliğe tıklayınca: o güvenliğin konumuna ekibin gördüğü "Güvenlik" işareti koyar.
    /// İşaret dünyada kısa süre (GuardPingSeconds) kalır; güvenlik hareket etse de işaret ilk konumunda durur.</summary>
    public bool RequestGuardPing(Vector3 position)
    {
        return RequestPingAt(position + Vector3.up * 1.9f, GuardPingIndex);
    }

    public const int GuardPingIndex = -2;
    /// <summary>Bir güvenlik pinglenince (herkeste) tetiklenir: kat planı o güvenliği birkaç sn herkese gösterir.</summary>
    public static System.Action<Vector3> GuardPinged;
    private const float GuardPingSeconds = 5f;

    [Rpc(SendTo.Server)]
    private void PingRpc(Vector3 position, int itemIndex, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (serverCooldowns.TryGetValue(sender, out float t) && Time.time < t) return;
        serverCooldowns[sender] = Time.time + cooldown * 0.8f;
        ShowPingRpc(position, sender, itemIndex);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ShowPingRpc(Vector3 position, ulong owner, int itemIndex)
    {
        pings[owner] = new Ping { Position = position, Owner = owner, ItemIndex = itemIndex, Until = Time.unscaledTime + (itemIndex == GuardPingIndex ? GuardPingSeconds : lifetime) };
        if (itemIndex == GuardPingIndex && GuardPinged != null) GuardPinged(position);
        GameAudio.Play2D(SoundId.Ping, 0.6f);
    }

    private void OnGUI()
    {
        if (!IsSpawned || pings.Count == 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        float sc = UITheme.Scale;
        float now = Time.unscaledTime;
        foreach (Ping p in pings.Values)
        {
            if (now > p.Until) continue;

            Vector3 sp = cam.WorldToScreenPoint(p.Position);
            if (sp.z <= 0f) continue;

            Color color = Color.white;
            string who = "";
            foreach (PlayerNetworkSetup ps in PlayerNetworkSetup.All)
            {
                if (ps != null && ps.OwnerClientId == p.Owner) { color = ps.PlayerColor; who = ps.PlayerName; }
            }

            bool guardPing = p.ItemIndex == GuardPingIndex;
            // Pirinç baklava; güvenlik işareti Danger; içinde oyuncu rengi noktası
            Color mark = guardPing ? UITheme.Danger : UITheme.BrassLight;
            float fade = Mathf.Clamp01((p.Until - now) / 1.5f);
            float bob = Mathf.Sin(now * 5f) * 3f;
            Vector2 c = new Vector2(sp.x, Screen.height - sp.y + bob);
            float half = 8f * sc;

            // Elmas işaret
            Matrix4x4 old = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, c);
            UIUtil.DrawRect(new Rect(c.x - half - 2f, c.y - half - 2f, (half + 2f) * 2f, (half + 2f) * 2f), UITheme.WithAlpha(UITheme.CharDeep, 0.85f * fade));
            UIUtil.DrawRect(new Rect(c.x - half, c.y - half, half * 2f, half * 2f), UITheme.WithAlpha(mark, fade));
            if (!guardPing)
                UIUtil.DrawRect(new Rect(c.x - 3f, c.y - 3f, 6f, 6f), new Color(color.r, color.g, color.b, fade));
            GUI.matrix = old;

            // Etiket: üstte ad (Caption), altında mesafe (Mono)
            string title = guardPing ? Loc.T("ping.guard") : who;
            if (!guardPing && p.ItemIndex >= 0 && ItemDatabase.Instance != null)
            {
                ItemDefinition d = ItemDatabase.Instance.Get(p.ItemIndex);
                if (d != null) title = d.DisplayName + " · " + who;
            }
            string dist = Mathf.RoundToInt(sp.z) + " m";
            GUIStyle ts = UITheme.Style(TextRole.Caption, guardPing ? UITheme.DangerLight : UITheme.Ivory, TextAnchor.MiddleCenter);
            GUIStyle ds = UITheme.Style(TextRole.Mono, UITheme.BrassLight, TextAnchor.MiddleCenter);
            float tw = Mathf.Max(ts.CalcSize(new GUIContent(title)).x, ds.CalcSize(new GUIContent(dist)).x) + 16f;
            float th = Mathf.Round(36f * sc);
            Rect r = new Rect(c.x - tw * 0.5f, c.y - half * 1.5f - 8f - th, tw, th);
            Color oldC = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            UIUtil.DrawRect(r, UITheme.WithAlpha(UITheme.CharDeep, 0.78f * fade));
            UIUtil.DrawRect(new Rect(r.x, r.yMax - 2f, r.width, 2f), UITheme.WithAlpha(mark, fade));
            GUI.Label(new Rect(r.x, r.y + 1f, r.width, th * 0.5f), title, ts);
            GUI.Label(new Rect(r.x, r.y + th * 0.5f - 2f, r.width, th * 0.5f), dist, ds);
            GUI.color = oldC;
        }
    }
}
