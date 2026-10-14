using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// F ile İTME: önündeki takım arkadaşını, müzedeki ziyaretçiyi ya da güvenliği itersin.
///  - Takım arkadaşı: biraz savrulur. Yaptığı E basılı tutma (eser değiştirme, çalma, kilit, konsol...) SIFIRLANIR:
///    E'yi bırakıp baştan basması gerekir. Atölyedeyse atölyeden çıkar (Esc'ye basmış gibi: tablo / büst / yüzük
///    yarım haliyle çantasına girer, tezgahta "Devam et" ile sürdürür). Saklanan / hapisteki oyuncu itilmez.
///  - Ziyaretçi: savrulur.
///  - Güvenlik: savrulur ve seni şüpheyle süzüp yerine bakar (güvenliği itmek dikkat çeker).
///    Aynı güvenliği kısa sürede 2–3 kez itersen sana JOPLA VURUR: birkaç saniye yerde baygın kalırsın
///    ("Polis sana saldırdı!"), elindeki iş bölünür.
///  - İtmek ses çıkarır (yakındaki güvenlikler duyabilir).
/// Ağ: istemci → sunucu "SS_PushReq", sunucu → itilen oyuncunun sahibine "SS_Pushed" (ağ nesnesi gerekmez).
/// Önünde itilecek biri varsa F işaret (ping) koymaz, iter. Sahneye bir şey eklemeye gerek yok.
/// </summary>
public class PushSystem : MonoBehaviour
{
    public static PushSystem Instance { get; private set; }

    /// <summary>Önümde itilebilecek biri var mı? (PingSystem F tuşunu buna göre bırakır.)</summary>
    public static bool HasTargetInFront { get; private set; }

    private const string ReqMsg = "SS_PushReq";
    private const string PushedMsg = "SS_Pushed";
    private const string KnockMsg = "SS_Knocked";

    // Jop: aynı güvenliği bu süre içinde 2–3 kez itersen vurur
    private const float StrikeWindow = 20f;
    private const float KnockSeconds = 2.6f;

    private struct PushCount { public int count, needed; public float last; }
    private readonly Dictionary<(ulong guard, ulong client), PushCount> guardPushes = new Dictionary<(ulong, ulong), PushCount>();

    /// <summary>Yerel oyuncu jopla vuruldu: bu ana kadar baygın.</summary>
    public static float KnockedUntil { get; private set; } = -1f;
    public static bool LocalKnockedOut => Time.time < KnockedUntil;

    private const float Range = 2.2f;          // istemci: ne kadar yakındakini itebilirsin
    private const float ServerRange = 3.2f;    // sunucu: hile koruması (gecikme payıyla)
    private const float MaxAngle = 40f;
    private const float Cooldown = 0.8f;
    private const float PlayerPushSpeed = 6f;  // itilen oyuncunun savrulma hızı (m/s, hızla söner)
    private const float AgentPushDistance = 1.1f;
    private const float AgentPushSeconds = 0.25f;
    private const float PushNoiseRadius = 3.5f;

    private enum TargetKind : byte { None = 0, Player = 1, Guard = 2, Visitor = 3 }

    private struct Target
    {
        public TargetKind kind;
        public ulong id;      // oyuncu / güvenlik: NetworkObjectId, ziyaretçi: numarası
        public Vector3 pos;
    }

    private struct Shove
    {
        public NavMeshAgent agent;
        public Vector3 velocity;
        public float until;
    }

    private CustomMessagingManager regCmm;
    private NetworkManager regNm;
    private float nextPush;
    private Target current;
    private readonly List<Shove> shoves = new List<Shove>();
    private readonly Dictionary<ulong, float> serverCooldown = new Dictionary<ulong, float>();
    private GUIStyle hintStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("PushSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<PushSystem>();
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        Unregister();
        if (Instance == this) Instance = null;
    }

    // =====================================================================
    //  Döngü
    // =====================================================================

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool active = nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient) && nm.CustomMessagingManager != null;
        if (!active)
        {
            if (regCmm != null) Unregister();
            HasTargetInFront = false;
            shoves.Clear();
            return;
        }
        if (regCmm != nm.CustomMessagingManager) Register(nm);

        if (nm.IsServer) TickShoves();

        // Yerel oyuncu: önündeki hedef + F
        current = FindTarget(out bool canAct);
        HasTargetInFront = canAct && current.kind != TargetKind.None;

        Keyboard kb = Keyboard.current;
        if (!HasTargetInFront || kb == null || !kb.fKey.wasPressedThisFrame || Time.unscaledTime < nextPush) return;
        nextPush = Time.unscaledTime + Cooldown;

        Camera cam = Camera.main;
        Vector3 dir = cam != null ? cam.transform.forward : Vector3.forward;
        dir.y = 0f;
        SendRequest(nm, current, dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward);
    }

    /// <summary>Kendi oyuncunun önündeki (bakış açısındaki) en yakın oyuncu / güvenlik / ziyaretçi.</summary>
    private Target FindTarget(out bool canAct)
    {
        canAct = false;
        var none = new Target { kind = TargetKind.None };
        PlayerStealth me = PlayerStealth.Local;
        Camera cam = Camera.main;
        if (me == null || cam == null || !me.IsSpawned) return none;
        if (ChatUI.IsTyping || SessionMenuUI.IsPaused || PlayerInventory.IsPanelOpen || WorkbenchUI.IsOpen || BriefingUI.IsOpen || PlayerController.InputLocked) return none;
        if (Cursor.lockState != CursorLockMode.Locked || me.IsJailed || me.CurrentHidingSpot != null || LocalKnockedOut) return none;
        canAct = true;

        Vector3 from = me.transform.position;
        Vector3 fwd = cam.transform.forward;
        Target best = none;
        float bestD = Range;

        // Takım arkadaşları
        foreach (PlayerStealth p in PlayerStealth.All)
        {
            if (p == null || p == me || !p.IsSpawned || p.IsHidden || p.IsJailed) continue;
            Consider(me, cam, TargetKind.Player, p.NetworkObjectId, p.transform.position, ref best, ref bestD);
        }
        // Güvenlikler
        foreach (GuardAI g in FindGuards())
        {
            if (g == null || !g.IsSpawned || !g.Online) continue;
            Consider(me, cam, TargetKind.Guard, g.NetworkObjectId, g.transform.position, ref best, ref bestD);
        }
        // Ziyaretçiler
        VisitorManager vm = VisitorManager.Instance;
        if (vm != null && vm.FindInFront(from, fwd, Range, MaxAngle, out byte vid, out Vector3 vpos))
            Consider(me, cam, TargetKind.Visitor, vid, vpos, ref best, ref bestD);

        return best;
    }

    /// <summary>Aday daha yakın ve önündeyse (arada duvar yoksa) en iyi hedef olur.</summary>
    private static void Consider(PlayerStealth me, Camera cam, TargetKind k, ulong id, Vector3 pos, ref Target best, ref float bestD)
    {
        Vector3 from = me.transform.position;
        Vector3 fwd = cam.transform.forward;
        Vector3 flatFwd = new Vector3(fwd.x, 0f, fwd.z);
        Vector3 to = pos - from;
        to.y = 0f;
        float d = to.magnitude;
        if (d > bestD || d < 0.05f || Vector3.Angle(flatFwd, to) > MaxAngle) return;

        // Arada duvar varsa itilmez
        Vector3 eye = cam.transform.position;
        Vector3 aim = pos + Vector3.up * 0.5f;
        if (Physics.Linecast(eye, aim, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            bool self = hit.collider.transform.IsChildOf(me.transform);
            bool isPerson = hit.collider.GetComponentInParent<PlayerStealth>() != null || hit.collider.GetComponentInParent<GuardAI>() != null;
            if (!self && !isPerson && hit.distance < Vector3.Distance(eye, aim) - 0.6f) return;
        }
        bestD = d;
        best = new Target { kind = k, id = id, pos = pos };
    }

    // Güvenliklerin listesi istemcide yok (SecurityManager.Guards sadece sunucuda dolar): sahnedekileri ara ara topla
    private readonly List<GuardAI> guardCache = new List<GuardAI>();
    private float nextGuardScan;

    private List<GuardAI> FindGuards()
    {
        if (Time.unscaledTime >= nextGuardScan)
        {
            nextGuardScan = Time.unscaledTime + 1f;
            guardCache.Clear();
            guardCache.AddRange(FindObjectsByType<GuardAI>(FindObjectsSortMode.None));
        }
        return guardCache;
    }

    // =====================================================================
    //  Ağ
    // =====================================================================

    private void Register(NetworkManager nm)
    {
        Unregister();
        regNm = nm;
        regCmm = nm.CustomMessagingManager;
        regCmm.RegisterNamedMessageHandler(ReqMsg, OnRequest);
        regCmm.RegisterNamedMessageHandler(PushedMsg, OnPushed);
        regCmm.RegisterNamedMessageHandler(KnockMsg, OnKnocked);
    }

    private void Unregister()
    {
        if (regCmm != null)
        {
            try
            {
                regCmm.UnregisterNamedMessageHandler(ReqMsg);
                regCmm.UnregisterNamedMessageHandler(PushedMsg);
                regCmm.UnregisterNamedMessageHandler(KnockMsg);
            }
            catch (Exception) { }
        }
        regCmm = null;
        regNm = null;
    }

    private void SendRequest(NetworkManager nm, Target t, Vector3 dir)
    {
        if (nm.IsServer)
        {
            ServerPush(nm.LocalClientId, t.kind, t.id, dir);
            return;
        }
        using (var w = new FastBufferWriter(32, Allocator.Temp))
        {
            w.WriteValueSafe((byte)t.kind);
            w.WriteValueSafe(t.id);
            w.WriteValueSafe(dir);
            regCmm.SendNamedMessage(ReqMsg, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
    }

    private void OnRequest(ulong sender, FastBufferReader reader)
    {
        if (regNm == null || !regNm.IsServer) return;
        try
        {
            reader.ReadValueSafe(out byte kind);
            reader.ReadValueSafe(out ulong id);
            reader.ReadValueSafe(out Vector3 dir);
            ServerPush(sender, (TargetKind)kind, id, dir);
        }
        catch (Exception) { }
    }

    // =====================================================================
    //  Sunucu
    // =====================================================================

    private void ServerPush(ulong senderClient, TargetKind kind, ulong id, Vector3 dir)
    {
        NetworkManager nm = regNm != null ? regNm : NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (!nm.ConnectedClients.TryGetValue(senderClient, out NetworkClient client) || client.PlayerObject == null) return;
        if (serverCooldown.TryGetValue(senderClient, out float t) && Time.time - t < Cooldown * 0.8f) return;

        PlayerStealth pusher = client.PlayerObject.GetComponent<PlayerStealth>();
        if (pusher == null || pusher.IsJailed || pusher.IsHidden) return;

        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : pusher.transform.forward;
        Vector3 from = pusher.transform.position;
        Vector3 hitPos;

        switch (kind)
        {
            case TargetKind.Player:
            {
                if (!nm.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject no) || no == null) return;
                PlayerStealth target = no.GetComponent<PlayerStealth>();
                if (target == null || target == pusher || target.IsJailed || target.IsHidden) return;
                if (Vector3.Distance(from, target.transform.position) > ServerRange) return;
                hitPos = target.transform.position;
                Vector3 push = dir * PlayerPushSpeed + Vector3.up * 1.5f;
                if (target.OwnerClientId == nm.LocalClientId) ApplyPushedLocally(push);
                else
                {
                    using (var w = new FastBufferWriter(16, Allocator.Temp))
                    {
                        w.WriteValueSafe(push);
                        regCmm.SendNamedMessage(PushedMsg, target.OwnerClientId, w, NetworkDelivery.Reliable);
                    }
                }
                break;
            }

            case TargetKind.Guard:
            {
                if (!nm.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject no) || no == null) return;
                GuardAI guard = no.GetComponent<GuardAI>();
                if (guard == null || !guard.Online) return;
                if (Vector3.Distance(from, guard.transform.position) > ServerRange) return;
                hitPos = guard.transform.position;
                AddShove(guard.GetComponent<NavMeshAgent>(), dir);
                guard.ServerInvestigate(from, false); // itilen güvenlik dönüp seni süzer
                if (CountGuardPush(guard.NetworkObjectId, senderClient))
                {
                    // Sabrı taştı: jopla vurur
                    ServerKnockOut(nm, client, pusher, guard);
                    serverCooldown[senderClient] = Time.time;
                    return;
                }
                break;
            }

            case TargetKind.Visitor:
            {
                VisitorManager vm = VisitorManager.Instance;
                NavMeshAgent agent = vm != null ? vm.ServerAgent((byte)id) : null;
                if (agent == null || Vector3.Distance(from, agent.transform.position) > ServerRange) return;
                hitPos = agent.transform.position;
                AddShove(agent, dir);
                break;
            }

            default:
                return;
        }

        serverCooldown[senderClient] = Time.time;
        NoiseSystem.Emit(hitPos, PushNoiseRadius);
        PlayerInventory inv = client.PlayerObject.GetComponent<PlayerInventory>();
        if (inv != null) inv.ServerPlaySound(SoundId.ItemThud, hitPos);
    }

    /// <summary>Sunucu: bu güvenliği bu oyuncu kaçıncı kez itti? Sınıra (2 ya da 3) gelince true.</summary>
    private bool CountGuardPush(ulong guardId, ulong clientId)
    {
        var key = (guardId, clientId);
        if (!guardPushes.TryGetValue(key, out PushCount pc) || Time.time - pc.last > StrikeWindow)
            pc = new PushCount { count = 0, needed = UnityEngine.Random.Range(2, 4) };
        pc.count++;
        pc.last = Time.time;
        if (pc.count >= pc.needed)
        {
            guardPushes.Remove(key);
            return true;
        }
        guardPushes[key] = pc;
        return false;
    }

    /// <summary>Sunucu: güvenlik oyuncuya jopla vurur → oyuncu birkaç saniye baygın.</summary>
    private void ServerKnockOut(NetworkManager nm, NetworkClient client, PlayerStealth victim, GuardAI guard)
    {
        Vector3 at = victim.transform.position;
        NoiseSystem.Emit(at, PushNoiseRadius);
        PlayerInventory inv = client.PlayerObject.GetComponent<PlayerInventory>();
        if (inv != null)
        {
            inv.ServerPlaySound(SoundId.ItemThud, at);
            inv.ServerPlaySound(SoundId.GuardAlert, guard.transform.position);
        }
        if (victim.OwnerClientId == nm.LocalClientId) ApplyKnockLocally(KnockSeconds);
        else
        {
            using (var w = new FastBufferWriter(8, Allocator.Temp))
            {
                w.WriteValueSafe(KnockSeconds);
                regCmm.SendNamedMessage(KnockMsg, victim.OwnerClientId, w, NetworkDelivery.Reliable);
            }
        }
    }

    private void OnKnocked(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId) return;
        try
        {
            reader.ReadValueSafe(out float seconds);
            ApplyKnockLocally(Mathf.Clamp(seconds, 0.5f, 6f));
        }
        catch (Exception) { }
    }

    /// <summary>Jopla vuruldum: yaptığım iş bölünür, yere yığılırım, ekranda "Polis sana saldırdı!".</summary>
    private static void ApplyKnockLocally(float seconds)
    {
        PlayerStealth me = PlayerStealth.Local;
        if (me == null || me.IsJailed || me.CurrentHidingSpot != null) return;
        if (WorkbenchUI.IsOpen) WorkbenchUI.Interrupt();
        PlayerInteractor interactor = me.GetComponent<PlayerInteractor>();
        if (interactor != null) interactor.InterruptHold();
        LockpickSystem.Interrupt();
        PlayerController pc = me.GetComponent<PlayerController>();
        if (pc != null) pc.KnockOut(seconds);
        KnockedUntil = Time.time + seconds;
        GameAudio.Play2D(SoundId.ItemThud, 1f);
    }

    private void AddShove(NavMeshAgent agent, Vector3 dir)
    {
        if (agent == null || !agent.enabled) return;
        shoves.Add(new Shove { agent = agent, velocity = dir * (AgentPushDistance / AgentPushSeconds), until = Time.time + AgentPushSeconds });
    }

    /// <summary>Sunucu: itilen güvenlik / ziyaretçi kısa süre NavMesh üzerinde kayar.</summary>
    private void TickShoves()
    {
        for (int i = shoves.Count - 1; i >= 0; i--)
        {
            Shove s = shoves[i];
            if (s.agent == null || !s.agent.enabled || !s.agent.isOnNavMesh || Time.time >= s.until)
            {
                shoves.RemoveAt(i);
                continue;
            }
            s.agent.Move(s.velocity * Time.deltaTime);
        }
    }

    // =====================================================================
    //  İtilen oyuncu (kendi bilgisayarında)
    // =====================================================================

    private void OnPushed(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId) return;
        try
        {
            reader.ReadValueSafe(out Vector3 push);
            ApplyPushedLocally(push);
        }
        catch (Exception) { }
    }

    /// <summary>Beni ittiler: yaptığım iş bölünür, biraz savrulurum.</summary>
    private static void ApplyPushedLocally(Vector3 push)
    {
        PlayerStealth me = PlayerStealth.Local;
        if (me == null || me.IsJailed || me.CurrentHidingSpot != null) return;

        // Atölyedeysem: masadan koparıldım → atölyeden çık (yaptığım kadarı çantaya girer, sonra "Devam et")
        if (WorkbenchUI.IsOpen) WorkbenchUI.Interrupt();

        // E basılı tutuyordum (eser değiştirme, çalma, kilit...): baştan başlamalıyım
        PlayerInteractor interactor = me.GetComponent<PlayerInteractor>();
        if (interactor != null) interactor.InterruptHold();
        LockpickSystem.Interrupt(); // maymuncukla kilit açıyordum: baştan

        PlayerController pc = me.GetComponent<PlayerController>();
        if (pc != null && pc.enabled)
        {
            pc.AddPush(push);
            // Sarsıntı: bakış biraz kayar (baktığı eseri / kişiyi kaçırır)
            me.transform.Rotate(0f, UnityEngine.Random.Range(-14f, 14f), 0f, Space.World);
        }

        if (PlayerInventory.Local != null) PlayerInventory.Local.ShowLocalToast(Loc.T("push.pushed"));
        GameAudio.Play2D(SoundId.ItemThud, 0.7f);
    }

    // =====================================================================
    //  Arayüz: önünde itilebilecek biri varsa küçük ipucu
    // =====================================================================

    private GUIStyle knockStyle;

    private void OnGUI()
    {
        // Baygınken: ekran kararır, "Polis sana saldırdı!"
        float knockLeft = KnockedUntil - Time.time;
        if (knockLeft > -0.6f)
        {
            float a = knockLeft > 0f ? 0.55f : 0.55f * (1f + knockLeft / 0.6f); // uyanırken açılır
            UIUtil.DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.15f, 0f, 0f, Mathf.Clamp01(a)));
            if (knockLeft > 0f)
            {
                if (knockStyle == null)
                    knockStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                knockStyle.normal.textColor = new Color(1f, 0.3f, 0.25f);
                Rect tr = new Rect(0f, Screen.height * 0.38f, Screen.width, 44f);
                GUI.Label(new Rect(tr.x + 2f, tr.y + 2f, tr.width, tr.height), Loc.T("push.struck"), ShadowOf(knockStyle));
                GUI.Label(tr, Loc.T("push.struck"), knockStyle);
            }
            return;
        }

        if (!HasTargetInFront || current.kind == TargetKind.None) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(current.pos + Vector3.up * 1.0f);
        if (sp.z <= 0f) return;
        if (hintStyle == null)
        {
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        }
        Rect r = new Rect(sp.x - 50f, Screen.height - sp.y - 10f, 100f, 20f);
        UIUtil.DrawRect(r, new Color(0f, 0f, 0f, 0.45f));
        GUI.Label(r, Loc.T("push.hint"), hintStyle);
    }

    private GUIStyle knockShadow;

    private GUIStyle ShadowOf(GUIStyle st)
    {
        if (knockShadow == null) knockShadow = new GUIStyle(st);
        knockShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        return knockShadow;
    }
}
