using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Parmak ağırlıklarını şekle göre yeniden hesaplar (oyun açılırken, FBX'e dokunmadan; mesh kopyası üzerinde).
///
/// Neden: SoygunChar'ın el boyamasında (Blender'da ölçüldü)
///   - işaret / serçe parmağının kök boğumu çoğunlukla avuç kemiğine (HandPalm0x) bağlı → parmak kökten bükülmüyor,
///   - orta / yüzük parmağının orta boğumu neredeyse tamamen uç kemiğine bağlı (Middle_Intermediate'e 2 nokta,
///     Ring_Intermediate'e hiç yok) → orta boğum ayrı bükülemiyor, parmak kırık / ezik görünüyor,
///   - yüzük parmağının kökü %16 serçe parmağına bağlı → bir parmak bükülünce yanındaki de çekiliyor.
/// Düzeltme: her parmak noktası (ve aynı yerdeki dış çizgi kabuğu) en yakın parmağın ekseni boyunca konumuna göre
/// avuç / kök / orta / uç kemiğine bağlanır, eklemlerde yumuşak geçiş. Avucun içi (parmak kökünden geride) eskisi gibi.
/// </summary>
public static class FingerReskin
{
    private const float JointBlend = 0.07f; // eklemde geçiş yarı genişliği (parmak boyunun oranı)
    private const float PalmBlend = 0.15f;  // parmak kökünün gerisinde eski ağırlıklarla karışma bölgesi

    private static readonly Dictionary<Mesh, Mesh> cache = new Dictionary<Mesh, Mesh>();
    private static readonly HashSet<Mesh> fixedMeshes = new HashSet<Mesh>();

    private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };
    private static readonly string[] Palms = { "HandPalm01", "HandPalm02", "HandPalm03", "HandPalm04" };

    private struct Chain
    {
        public int palm, p, i, d;
        public Vector3 palmHead, P, T, axis;
        public float len, s1, s2;
    }

    public static void Apply(GameObject root)
    {
        if (root == null) return;
        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Apply(smr);
    }

    public static bool Apply(SkinnedMeshRenderer smr)
    {
        if (smr == null) return false;
        Mesh src = smr.sharedMesh;
        if (src == null) return false;
        if (fixedMeshes.Contains(src)) return true;
        if (cache.TryGetValue(src, out Mesh done) && done != null) { smr.sharedMesh = done; return true; }
        if (!src.isReadable) return false;

        Transform[] bones = smr.bones;
        Matrix4x4[] bind = src.bindposes;
        if (bones == null || bind == null || bones.Length != bind.Length || bones.Length == 0) return false;
        var index = new Dictionary<string, int>();
        for (int b = 0; b < bones.Length; b++) if (bones[b] != null && !index.ContainsKey(bones[b].name)) index[bones[b].name] = b;

        var chains = new List<Chain>[2];
        var region = new bool[bones.Length];
        var sideOf = new int[bones.Length];
        for (int sd = 0; sd < 2; sd++)
        {
            chains[sd] = new List<Chain>();
            string suf = sd == 0 ? ".L" : ".R";
            for (int f = 0; f < Fingers.Length; f++)
            {
                if (!index.TryGetValue(Palms[f] + suf, out int pa) || !index.TryGetValue(Fingers[f] + "_Proximal" + suf, out int p) ||
                    !index.TryGetValue(Fingers[f] + "_Intermediate" + suf, out int i) || !index.TryGetValue(Fingers[f] + "_Distal" + suf, out int d)) continue;
                Vector3 ph = Head(bind[pa]), P = Head(bind[p]), I = Head(bind[i]), D = Head(bind[d]);
                Vector3 T = D + (D - I);
                float L = Vector3.Distance(P, T);
                if (L < 1e-5f) continue;
                Vector3 ax = (T - P) / L;
                chains[sd].Add(new Chain
                {
                    palm = pa, p = p, i = i, d = d, palmHead = ph, P = P, T = T, axis = ax, len = L,
                    s1 = Vector3.Dot(I - P, ax) / L, s2 = Vector3.Dot(D - P, ax) / L,
                });
                foreach (int b in new[] { pa, p, i, d }) { region[b] = true; sideOf[b] = sd; }
            }
        }
        if (chains[0].Count == 0 && chains[1].Count == 0) return false;

        NativeArray<byte> bpv = src.GetBonesPerVertex();
        NativeArray<BoneWeight1> bw = src.GetAllBoneWeights();
        int vc = src.vertexCount;
        if (bpv.Length != vc) return false;
        Vector3[] verts = src.vertices;

        var outCount = new byte[vc];
        var outW = new List<BoneWeight1>(bw.Length + 64);
        var acc = new Dictionary<int, float>();
        var tmp = new List<KeyValuePair<int, float>>();
        int start = 0, changed = 0;

        for (int v = 0; v < vc; v++)
        {
            int n = bpv[v];
            float regionSum = 0f, sideR = 0f;
            for (int k = 0; k < n; k++)
            {
                BoneWeight1 w = bw[start + k];
                if (w.boneIndex >= 0 && w.boneIndex < region.Length && region[w.boneIndex])
                {
                    regionSum += w.weight;
                    if (sideOf[w.boneIndex] == 1) sideR += w.weight;
                }
            }

            bool done2 = false;
            if (regionSum >= 0.5f)
            {
                List<Chain> cl = chains[sideR >= regionSum * 0.5f ? 1 : 0];
                if (cl.Count > 0)
                {
                    Vector3 X = verts[v];
                    int best = 0; float bd = float.MaxValue;
                    for (int c = 0; c < cl.Count; c++)
                    {
                        Vector3 a = cl[c].palmHead, dd = cl[c].T - a;
                        float u = Mathf.Clamp01(Vector3.Dot(X - a, dd) / Mathf.Max(1e-12f, dd.sqrMagnitude));
                        float dist = (X - (a + dd * u)).sqrMagnitude;
                        if (dist < bd) { bd = dist; best = c; }
                    }
                    Chain ch = cl[best];
                    float s = Vector3.Dot(X - ch.P, ch.axis) / ch.len;
                    if (s >= -PalmBlend)
                    {
                        float a0 = Smooth((s + JointBlend) / (2f * JointBlend));
                        float a1 = Smooth((s - ch.s1 + JointBlend) / (2f * JointBlend));
                        float a2 = Smooth((s - ch.s2 + JointBlend) / (2f * JointBlend));
                        float kNew = Smooth((s + PalmBlend) / PalmBlend);
                        acc.Clear();
                        for (int k = 0; k < n; k++)
                        {
                            BoneWeight1 w = bw[start + k];
                            Add(acc, w.boneIndex, w.weight * (1f - kNew));
                        }
                        Add(acc, ch.palm, (1f - a0) * kNew);
                        Add(acc, ch.p, a0 * (1f - a1) * kNew);
                        Add(acc, ch.i, a1 * (1f - a2) * kNew);
                        Add(acc, ch.d, a2 * kNew);

                        tmp.Clear();
                        foreach (KeyValuePair<int, float> kv in acc) if (kv.Value > 1e-4f) tmp.Add(kv);
                        tmp.Sort((x, y) => y.Value.CompareTo(x.Value));
                        int m = Mathf.Min(4, tmp.Count);
                        float sum = 0f;
                        for (int k = 0; k < m; k++) sum += tmp[k].Value;
                        if (m > 0 && sum > 1e-6f)
                        {
                            for (int k = 0; k < m; k++) outW.Add(new BoneWeight1 { boneIndex = tmp[k].Key, weight = tmp[k].Value / sum });
                            outCount[v] = (byte)m;
                            done2 = true;
                            changed++;
                        }
                    }
                }
            }
            if (!done2)
            {
                for (int k = 0; k < n; k++) outW.Add(bw[start + k]);
                outCount[v] = (byte)n;
            }
            start += n;
        }
        if (changed == 0) return false;

        Mesh copy = Object.Instantiate(src);
        copy.name = src.name + "_Fingers";
        var nCount = new NativeArray<byte>(outCount, Allocator.Temp);
        var nW = new NativeArray<BoneWeight1>(outW.ToArray(), Allocator.Temp);
        copy.SetBoneWeights(nCount, nW);
        nCount.Dispose();
        nW.Dispose();
        cache[src] = copy;
        fixedMeshes.Add(copy);
        smr.sharedMesh = copy;
        return true;
    }

    private static Vector3 Head(Matrix4x4 bindpose) => bindpose.inverse.MultiplyPoint3x4(Vector3.zero);

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    private static void Add(Dictionary<int, float> d, int bone, float w)
    {
        if (w <= 0f) return;
        d.TryGetValue(bone, out float c);
        d[bone] = c + w;
    }
}
