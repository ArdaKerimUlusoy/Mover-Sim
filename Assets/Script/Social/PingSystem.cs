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
    private GUIStyle labelStyle, shadowStyle;

    private void Update()
    {
        if (!IsSpawned || PlayerInventory.Local == null) return;
        if (ChatUI.IsTyping || SessionMenuUI.IsPaused || PlayerInventory.IsPanelOpen) return;
        if (Cursor.lockState != CursorLockMode.Locked || Time.unscaledTime < nextLocalPing) return;

        bool pressed = (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame) ||
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
        pings[owner] = new Ping { Position = position, Owner = owner, ItemIndex = itemIndex, Until = Time.unscaledTime + lifetime };
        GameAudio.Play2D(SoundId.Ping, 0.6f);
    }

    private void OnGUI()
    {
        if (!IsSpawned || pings.Count == 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };
            shadowStyle = new GUIStyle(labelStyle);
            shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        }

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

            float fade = Mathf.Clamp01((p.Until - now) / 1.5f);
            float bob = Mathf.Sin(now * 5f) * 3f;
            Vector2 c = new Vector2(sp.x, Screen.height - sp.y + bob);

            // Elmas işaret
            Matrix4x4 old = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, c);
            UIUtil.DrawRect(new Rect(c.x - 9f, c.y - 9f, 18f, 18f), new Color(0f, 0f, 0f, 0.6f * fade));
            UIUtil.DrawRect(new Rect(c.x - 7f, c.y - 7f, 14f, 14f), new Color(color.r, color.g, color.b, fade));
            GUI.matrix = old;

            string text = who + "  " + Mathf.RoundToInt(sp.z) + "m";
            if (p.ItemIndex >= 0 && ItemDatabase.Instance != null)
            {
                ItemDefinition d = ItemDatabase.Instance.Get(p.ItemIndex);
                if (d != null) text = d.DisplayName + "\n" + text;
            }

            Rect r = new Rect(c.x - 120f, c.y - 52f, 240f, 40f);
            Color oldC = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, shadowStyle);
            labelStyle.normal.textColor = color;
            GUI.Label(r, text, labelStyle);
            GUI.color = oldC;
        }
    }
}
