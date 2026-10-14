using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Müzeyi gezen ziyaretçiler (her biri farklı görünüşlü). Kararları SUNUCU verir, konumlar saniyede ~8 kez
/// MuseumSession üzerinden herkese gönderilir (ağ nesnesi gerekmez, sahne değişmez).
///   - Eserlerin önünde durup bakarlar, odalar arasında dolaşırlar (Staff Only ve güvenlik odasına girmezler).
///   - Bir oyuncuyu suç işlerken (eser değiştirirken, çantadan çalarken, kilit / konsol kurcalarken) görürlerse
///     başlarında "!" çıkar, en yakın güvenliğe koşup haber verirler → alarm çalar, güvenlikler hırsızı kovalar.
///     Yakında güvenlik yoksa bağırırlar (birkaç saniye sonra alarm).
/// MuseumSession'ın objesine kendiliğinden eklenir.
/// </summary>
public class VisitorManager : MonoBehaviour
{
    public static VisitorManager Instance { get; private set; }

    private const float SendInterval = 0.12f;
    private const float SightRange = 9f;
    private const float SightAngle = 140f;

    private enum Mode : byte { Walk, Look, Witness, Report, Cooldown }

    private sealed class Visitor
    {
        public byte id;
        public int seed;
        public Transform t;
        public VisitorLook look;
        public NavMeshAgent agent;          // sadece sunucuda
        public Mode mode;
        public float until, nextSight, nextRepath;
        public PlayerStealth culprit;
        public Vector3 crimePos;
        public GuardAI targetGuard;
        // istemci: hedef konum
        public Vector3 netPos;
        public float netYaw;
    }

    private readonly List<Visitor> visitors = new List<Visitor>();
    private int builtLayoutVersion = -1;
    private float sendTimer;

    private void Awake() => Instance = this;

    /// <summary>Herkes: önündeki (görüş açısındaki) en yakın ziyaretçi. İtmek için (PushSystem).</summary>
    public bool FindInFront(Vector3 from, Vector3 forward, float range, float maxAngle, out byte id, out Vector3 pos)
    {
        id = 0;
        pos = Vector3.zero;
        float best = float.MaxValue;
        Vector3 flatFwd = new Vector3(forward.x, 0f, forward.z);
        foreach (Visitor v in visitors)
        {
            if (v.t == null) continue;
            Vector3 to = v.t.position - from;
            to.y = 0f;
            float d = to.magnitude;
            if (d > range || d < 0.01f || Vector3.Angle(flatFwd, to) > maxAngle) continue;
            if (d < best) { best = d; id = v.id; pos = v.t.position; }
        }
        return best < float.MaxValue;
    }

    /// <summary>Sunucu: ziyaretçinin NavMeshAgent'ı (itilince kaydırmak için). Yoksa null.</summary>
    public NavMeshAgent ServerAgent(byte id)
    {
        foreach (Visitor v in visitors) if (v.id == id) return v.agent;
        return null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Clear();
    }

    private static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer;

    private void Update()
    {
        MuseumSession ms = MuseumSession.Instance;
        MuseumLayout layout = MuseumLayout.Instance;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) { if (visitors.Count > 0) Clear(); return; }
        if (ms == null || !ms.Ready || layout == null) return;

        if (IsServer)
        {
            if (builtLayoutVersion != layout.Version && RuntimeNavMeshBaker.Ready && layout.VisitorPoints.Count > 0)
            {
                builtLayoutVersion = layout.Version;
                ServerSpawn(Mathf.Clamp(4 + ms.Level, 5, 10), ms.Seed, layout);
            }
            ServerTick(layout);

            sendTimer -= Time.deltaTime;
            if (sendTimer <= 0f)
            {
                sendTimer = SendInterval;
                ms.ServerSendVisitors(Pack(ms.Seed));
            }
        }
        else
        {
            // İstemci: gelen konumlara yumuşakça git
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            foreach (Visitor v in visitors)
            {
                if (v.t == null) continue;
                if ((v.t.position - v.netPos).sqrMagnitude > 25f) v.t.position = v.netPos;
                else v.t.position = Vector3.Lerp(v.t.position, v.netPos, k);
                v.t.rotation = Quaternion.Slerp(v.t.rotation, Quaternion.Euler(0f, v.netYaw, 0f), k);
            }
        }
    }

    // ---------------- Sunucu ----------------

    private void ServerSpawn(int count, int seed, MuseumLayout layout)
    {
        Clear();
        var rng = new System.Random(seed ^ 0x7151);
        for (int i = 0; i < count; i++)
        {
            Vector3 start = layout.VisitorPoints[rng.Next(layout.VisitorPoints.Count)];
            if (i < 2) start = layout.LobbyWorld;
            start += new Vector3((float)rng.NextDouble() * 2f - 1f, 0f, (float)rng.NextDouble() * 2f - 1f);
            if (NavMesh.SamplePosition(start, out NavMeshHit hit, 3f, NavMesh.AllAreas)) start = hit.position;

            Visitor v = Create((byte)i, seed, start, 0f);
            NavMeshAgent a = v.t.gameObject.AddComponent<NavMeshAgent>();
            a.baseOffset = 0f;
            a.radius = 0.3f;
            a.height = 1.8f;
            a.speed = 1.2f + (float)rng.NextDouble() * 0.4f;
            a.angularSpeed = 300f;
            a.acceleration = 6f;
            a.avoidancePriority = 60 + i;
            if (!a.isOnNavMesh) a.Warp(start);
            v.agent = a;
            v.mode = Mode.Look;
            v.until = Time.time + (float)rng.NextDouble() * 4f;
            visitors.Add(v);
        }
    }

    private Visitor Create(byte id, int seed, Vector3 pos, float yaw)
    {
        GameObject go = new GameObject("Visitor_" + id);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        VisitorLook look = go.AddComponent<VisitorLook>();
        look.Build(seed, id);
        return new Visitor { id = id, seed = seed, t = go.transform, look = look, netPos = pos, netYaw = yaw };
    }

    private void ServerTick(MuseumLayout layout)
    {
        SecurityManager sm = SecurityManager.Instance;
        foreach (Visitor v in visitors)
        {
            if (v.agent == null || !v.agent.isOnNavMesh) continue;
            float now = Time.time;

            // Göz: sakin dolaşırken suç işleyen birini görüyor mu?
            if ((v.mode == Mode.Walk || v.mode == Mode.Look) && now >= v.nextSight)
            {
                v.nextSight = now + 0.25f;
                PlayerStealth seen = SeeCrime(v);
                if (seen != null && (sm == null || !sm.AlarmActive))
                {
                    v.mode = Mode.Witness;
                    v.culprit = seen;
                    v.crimePos = seen.transform.position;
                    v.until = now + 0.9f;
                    v.agent.ResetPath();
                    if (seen.TryGetComponent(out PlayerInventory inv)) inv.ServerPlaySound(SoundId.GuardHuh, v.t.position);
                }
            }

            switch (v.mode)
            {
                case Mode.Walk:
                    v.agent.speed = sm != null && sm.AlarmActive ? 2.2f : 1.3f;
                    if (!v.agent.pathPending && v.agent.remainingDistance < 0.6f)
                    {
                        v.mode = Mode.Look;
                        v.until = now + Random.Range(3f, 8f);
                    }
                    break;

                case Mode.Look:
                    if (now >= v.until)
                    {
                        Vector3 p = layout.VisitorPoints[Random.Range(0, layout.VisitorPoints.Count)];
                        p += new Vector3(Random.Range(-0.8f, 0.8f), 0f, Random.Range(-0.8f, 0.8f));
                        if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)) v.agent.SetDestination(hit.position);
                        v.mode = Mode.Walk;
                    }
                    break;

                case Mode.Witness:
                    if (v.culprit != null) Face(v, v.culprit.transform.position);
                    if (now >= v.until)
                    {
                        v.targetGuard = NearestGuard(v.t.position);
                        v.mode = Mode.Report;
                        v.until = now + (v.targetGuard != null ? 25f : 2.5f);
                        v.agent.speed = 4f;
                    }
                    break;

                case Mode.Report:
                    if (sm != null && sm.AlarmActive) { Calm(v, now); break; } // başkası haber verdi
                    if (v.targetGuard == null || !v.targetGuard.Online)
                    {
                        // Yakında güvenlik yok: bağırır, birkaç saniye sonra alarm
                        if (now >= v.until) { Report(v); Calm(v, now); }
                        break;
                    }
                    if (now >= v.nextRepath)
                    {
                        v.nextRepath = now + 0.4f;
                        v.agent.SetDestination(v.targetGuard.transform.position);
                    }
                    if (Vector3.Distance(v.t.position, v.targetGuard.transform.position) < 2.4f || now >= v.until)
                    {
                        Report(v);
                        Calm(v, now);
                    }
                    break;

                case Mode.Cooldown:
                    if (now >= v.until) { v.mode = Mode.Look; v.until = now + 1f; }
                    break;
            }
        }
    }

    private void Report(Visitor v)
    {
        SecurityManager sm = SecurityManager.Instance;
        if (sm == null || sm.AlarmActive) return;
        PlayerStealth culprit = v.culprit != null && v.culprit.IsSpawned && !v.culprit.IsJailed ? v.culprit : null;
        sm.ServerTriggerAlarm(culprit != null ? culprit.transform.position : v.crimePos, "alarm.witness", culprit);
    }

    private void Calm(Visitor v, float now)
    {
        v.mode = Mode.Cooldown;
        v.until = now + 6f;
        v.culprit = null;
        v.targetGuard = null;
        v.agent.speed = 1.3f;
        v.agent.ResetPath();
    }

    private static void Face(Visitor v, Vector3 target)
    {
        Vector3 d = target - v.t.position;
        d.y = 0f;
        if (d.sqrMagnitude > 0.01f) v.t.rotation = Quaternion.Slerp(v.t.rotation, Quaternion.LookRotation(d), Time.deltaTime * 8f);
    }

    private static PlayerStealth SeeCrime(Visitor v)
    {
        Vector3 eye = v.t.position + Vector3.up * 1.6f;
        foreach (PlayerStealth p in PlayerStealth.All)
        {
            if (p == null || !p.IsSpawned || !p.IsDoingCrime || p.IsHidden || p.IsJailed) continue;
            if (Vision.CanSee(eye, v.t.forward, SightRange, SightAngle, p)) return p;
        }
        return null;
    }

    private static GuardAI NearestGuard(Vector3 pos)
    {
        GuardAI best = null;
        float bestD = float.MaxValue;
        foreach (GuardAI g in SecurityManager.Guards)
        {
            if (g == null || !g.Online) continue;
            float d = Vector3.Distance(pos, g.transform.position);
            if (d < bestD) { bestD = d; best = g; }
        }
        return best;
    }

    // ---------------- Ağ paketi ----------------
    // [sayı][tohum(4)] + her ziyaretçi: [no][x,y,z cm (short×3)][yön][durum]

    private byte[] Pack(int seed)
    {
        var data = new byte[5 + visitors.Count * 9];
        data[0] = (byte)visitors.Count;
        System.BitConverter.GetBytes(seed).CopyTo(data, 1);
        int o = 5;
        foreach (Visitor v in visitors)
        {
            Vector3 p = v.t.position;
            data[o++] = v.id;
            WriteShort(data, ref o, p.x);
            WriteShort(data, ref o, p.y);
            WriteShort(data, ref o, p.z);
            data[o++] = (byte)Mathf.RoundToInt(Mathf.Repeat(v.t.eulerAngles.y, 360f) / 360f * 255f);
            byte state = v.mode == Mode.Witness ? VisitorLook.StateWitness : v.mode == Mode.Report ? VisitorLook.StateReport : VisitorLook.StateCalm;
            data[o++] = state;
            if (v.look != null) v.look.State = state;
        }
        return data;
    }

    private static void WriteShort(byte[] d, ref int o, float meters)
    {
        short s = (short)Mathf.Clamp(Mathf.RoundToInt(meters * 100f), short.MinValue, short.MaxValue);
        d[o++] = (byte)(s & 0xff);
        d[o++] = (byte)((s >> 8) & 0xff);
    }

    private static float ReadShort(byte[] d, ref int o)
    {
        short s = (short)(d[o] | (d[o + 1] << 8));
        o += 2;
        return s / 100f;
    }

    /// <summary>İstemci: sunucudan gelen ziyaretçi konumları.</summary>
    public void ClientApply(byte[] data)
    {
        if (IsServer || data == null || data.Length < 5) return;
        int count = data[0];
        int seed = System.BitConverter.ToInt32(data, 1);
        if (data.Length < 5 + count * 9) return;

        // Sayı ya da tohum değiştiyse (yeni harita) baştan kur
        bool rebuild = visitors.Count != count;
        foreach (Visitor v in visitors) if (v.seed != seed) rebuild = true;

        int o = 5;
        if (rebuild)
        {
            Clear();
            for (int i = 0; i < count; i++)
            {
                byte id = data[o];
                int p = o + 1;
                Vector3 pos = new Vector3(ReadShort(data, ref p), ReadShort(data, ref p), ReadShort(data, ref p));
                float yaw = data[p] / 255f * 360f;
                visitors.Add(Create(id, seed, pos, yaw));
                o += 9;
            }
            o = 5;
        }

        for (int i = 0; i < count; i++)
        {
            Visitor v = visitors[i];
            o++; // no
            v.netPos = new Vector3(ReadShort(data, ref o), ReadShort(data, ref o), ReadShort(data, ref o));
            v.netYaw = data[o++] / 255f * 360f;
            byte state = data[o++];
            if (v.look != null) v.look.State = state;
        }
    }

    private void Clear()
    {
        foreach (Visitor v in visitors) if (v.t != null) Destroy(v.t.gameObject);
        visitors.Clear();
    }
}
