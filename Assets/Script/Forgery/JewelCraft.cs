using UnityEngine;

/// <summary>
/// Kuyumcu tezgahında yapılan yüzük (ve müzedeki orijinalin "kusursuz" hali).
///   1) Döküm : kalıp (ince halka / kalın halka / mühür) + metal seçilir, eriyik metal kalıba dökülür.
///              Doldurma oranı kalıbın çizgisine ne kadar yakınsa halka o kadar doğru kalınlıkta çıkar.
///   2) Taşlar: taşlar halkanın üzerinde doğru açılara (ve doğru renkte) yerleştirilir.
///   3) Cila  : fareyle ovularak parlatılır.
/// Ağdan ForgeryData.pars olarak gider: [kalıp, metal, taş sayısı, taşlar×4, açılar×4, doluluk, cila].
/// </summary>
public sealed class JewelCraft
{
    public const int Max = JewelLook.MaxStones;
    public const int ParsLength = 3 + Max * 2 + 2;

    public int style, metal;
    public float fill = 0f, polish = 0f;
    public int count;
    public readonly int[] gems = new int[Max];
    public readonly float[] angles = new float[Max]; // derece, 90 = tepe, 0 = sağ

    /// <summary>Kalıbın doğru doluluk çizgisi (halka kalınlığı).</summary>
    public static float TargetFill(int style) => style == 1 ? 0.8f : style == 2 ? 0.62f : 0.42f;

    public byte[] ToPars()
    {
        var p = new byte[ParsLength];
        p[0] = (byte)Mathf.Clamp(style, 0, 255);
        p[1] = (byte)Mathf.Clamp(metal, 0, 255);
        p[2] = (byte)Mathf.Clamp(count, 0, Max);
        for (int i = 0; i < Max; i++)
        {
            p[3 + i] = (byte)Mathf.Clamp(gems[i], 0, 255);
            p[3 + Max + i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(angles[i], 360f) / 360f * 255f), 0, 255);
        }
        p[3 + Max * 2] = (byte)Mathf.RoundToInt(Mathf.Clamp01(fill * 0.5f) * 255f); // 0..2 aralığı (taşma görünsün)
        p[4 + Max * 2] = (byte)Mathf.RoundToInt(Mathf.Clamp01(polish) * 255f);
        return p;
    }

    public static bool TryFromPars(byte[] p, out JewelCraft c)
    {
        c = null;
        if (p == null || p.Length < ParsLength) return false;
        c = new JewelCraft { style = p[0], metal = p[1], count = Mathf.Clamp(p[2], 0, Max) };
        for (int i = 0; i < Max; i++)
        {
            c.gems[i] = p[3 + i];
            c.angles[i] = p[3 + Max + i] / 255f * 360f;
        }
        c.fill = p[3 + Max * 2] / 255f * 2f;
        c.polish = p[4 + Max * 2] / 255f;
        return true;
    }

    /// <summary>Orijinal yüzüğün kendisi (kusursuz döküm ve cila).</summary>
    public static JewelCraft FromLook(JewelLook l)
    {
        var c = new JewelCraft { style = l.style, metal = l.metal, count = l.stoneCount, fill = TargetFill(l.style), polish = 1f };
        for (int i = 0; i < Max; i++)
        {
            c.gems[i] = l.stones[i];
            c.angles[i] = l.angles[i];
        }
        return c;
    }

    /// <summary>
    /// 0..1 benzerlik: döküm %35 (kalıp, metal, doluluk), taşlar %45 (renk + açı), cila %20.
    /// Fazla taş ceza alır; yanlış renk ama doğru yerdeki taş biraz puan alır.
    /// </summary>
    public static float Score(JewelCraft c, JewelLook t)
    {
        if (c == null || t == null) return 0f;

        float castChoice = (c.style == t.style ? 0.5f : 0f) + (c.metal == t.metal ? 0.5f : 0f);
        float fillAcc = Mathf.Clamp01(1f - Mathf.Abs(c.fill - TargetFill(t.style)) / 0.3f);
        float cast = castChoice * 0.65f + fillAcc * 0.35f;

        var used = new bool[Max];
        float stoneSum = 0f;
        for (int i = 0; i < t.stoneCount; i++)
        {
            float best = 0f;
            int bestJ = -1;
            for (int j = 0; j < c.count; j++)
            {
                if (used[j]) continue;
                float diff = Mathf.Abs(Mathf.DeltaAngle(c.angles[j], t.angles[i]));
                float credit = 0f;
                if (diff <= 25f) credit = c.gems[j] == t.stones[i] ? 1f - diff / 25f * 0.6f : 0.25f * (1f - diff / 25f);
                if (credit > best) { best = credit; bestJ = j; }
            }
            if (bestJ >= 0) used[bestJ] = true;
            stoneSum += best;
        }
        float stones = t.stoneCount > 0 ? stoneSum / t.stoneCount : 1f;
        int extra = Mathf.Max(0, c.count - t.stoneCount);
        stones = Mathf.Clamp01(stones - extra * 0.15f);

        return Mathf.Clamp01(0.35f * cast + 0.45f * stones + 0.2f * Mathf.Clamp01(c.polish));
    }
}
