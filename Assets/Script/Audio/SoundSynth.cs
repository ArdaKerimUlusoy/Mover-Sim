using System;
using UnityEngine;

/// <summary>
/// Ses dosyası olmadan geçici sesler üretir (basit dalga sentezi).
/// Gerçek sesler SoundBank ile verilince bunlar kullanılmaz.
/// </summary>
public static class SoundSynth
{
    private const int Rate = 44100;
    private const float TwoPi = Mathf.PI * 2f;

    public static AudioClip[] Create(SoundId id)
    {
        switch (id)
        {
            case SoundId.Footstep:
                return new[] { Footstep(1), Footstep(2), Footstep(3), Footstep(4) };
            case SoundId.Pickup:
                return new[] { Make("Pickup", 0.16f, t =>
                {
                    float f = Mathf.Lerp(600f, 1150f, t / 0.1f);
                    return (Mathf.Sin(TwoPi * f * t) * 0.6f + Mathf.Sin(TwoPi * f * 2f * t) * 0.15f) * Env(t, 0.005f, 18f) * 0.5f;
                }) };
            case SoundId.Drop:
            case SoundId.ItemThud:
            {
                System.Random r = new System.Random(7);
                float lp = 0f;
                return new[] { Make(id.ToString(), 0.28f, t =>
                {
                    lp += (Noise(r) - lp) * 0.08f;
                    return (Mathf.Sin(TwoPi * 105f * t) * Env(t, 0.002f, 22f) * 0.7f + lp * Env(t, 0.001f, 35f) * 1.6f) * 0.7f;
                }) };
            }
            case SoundId.BagOpen:
                return new[] { Zipper("BagOpen", 0.32f, 55f, 11) };
            case SoundId.BagClose:
                return new[] { Zipper("BagClose", 0.2f, 75f, 12) };
            case SoundId.CoinThrow:
            {
                System.Random r = new System.Random(3);
                float lp = 0f;
                return new[] { Make("CoinThrow", 0.2f, t =>
                {
                    lp += (Noise(r) - lp) * Mathf.Lerp(0.05f, 0.25f, t / 0.2f);
                    return lp * Mathf.Sin(Mathf.PI * t / 0.2f) * 0.5f;
                }) };
            }
            case SoundId.CoinLand:
            {
                System.Random r = new System.Random(5);
                return new[] { Make("CoinLand", 0.6f, t =>
                {
                    float ping = Mathf.Sin(TwoPi * 2637f * t) * Mathf.Exp(-t * 9f) * 0.5f
                               + Mathf.Sin(TwoPi * 3951f * t) * Mathf.Exp(-t * 13f) * 0.3f
                               + Mathf.Sin(TwoPi * 5274f * t) * Mathf.Exp(-t * 18f) * 0.2f;
                    float click = t < 0.006f ? Noise(r) * (1f - t / 0.006f) : 0f;
                    return (ping + click) * 0.35f;
                }) };
            }
            case SoundId.HoldComplete:
                return new[] { Make("HoldComplete", 0.5f, t =>
                {
                    float a = Mathf.Sin(TwoPi * 880f * t) * Env(t, 0.005f, 8f);
                    float t2 = t - 0.08f;
                    float b = t2 > 0f ? Mathf.Sin(TwoPi * 1320f * t2) * Env(t2, 0.005f, 7f) : 0f;
                    return (a + b) * 0.25f;
                }) };
            case SoundId.Steal:
                return new[] { Make("Steal", 0.3f, t =>
                {
                    int step = Mathf.Min(2, (int)(t / 0.08f));
                    float lt = t - step * 0.08f;
                    float f = step == 0 ? 900f : step == 1 ? 700f : 520f;
                    return Mathf.Sin(TwoPi * f * lt) * Env(lt, 0.003f, 30f) * 0.35f;
                }) };
            case SoundId.UiClick:
                return new[] { Make("UiClick", 0.05f, t => Mathf.Sin(TwoPi * 1500f * t) * Mathf.Exp(-t * 90f) * 0.3f) };
            case SoundId.DoorOpen:
                return new[] { Creak("DoorOpen", 0.45f, 180f, 260f, 21) };
            case SoundId.DoorClose:
            {
                System.Random r = new System.Random(22);
                float lp = 0f;
                return new[] { Make("DoorClose", 0.3f, t =>
                {
                    lp += (Noise(r) - lp) * 0.12f;
                    return (Mathf.Sin(TwoPi * 80f * t) * 0.6f + lp) * Env(t, 0.002f, 18f) * 0.6f;
                }) };
            }
            default:
                return Array.Empty<AudioClip>();
        }
    }

    private static AudioClip Footstep(int seed)
    {
        System.Random r = new System.Random(seed * 31);
        float lp = 0f;
        float cutoff = 0.12f + seed * 0.02f;
        float thumpHz = 80f + seed * 8f;
        return Make("Footstep" + seed, 0.13f, t =>
        {
            lp += (Noise(r) - lp) * cutoff;
            return (lp * 1.8f * Env(t, 0.002f, 40f) + Mathf.Sin(TwoPi * thumpHz * t) * Env(t, 0.003f, 35f) * 0.35f) * 0.6f;
        });
    }

    private static AudioClip Zipper(string name, float duration, float teethPerSecond, int seed)
    {
        System.Random r = new System.Random(seed);
        float prev = 0f;
        return Make(name, duration, t =>
        {
            float n = Noise(r);
            float hp = n - prev; // yüksek frekans, "cırt" dokusu
            prev = n;
            float teeth = Mathf.Sin(TwoPi * teethPerSecond * t) > 0.3f ? 1f : 0.25f;
            float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((duration - t) / 0.05f);
            return hp * teeth * env * 0.35f;
        });
    }

    private static AudioClip Creak(string name, float duration, float f0, float f1, int seed)
    {
        System.Random r = new System.Random(seed);
        float phase = 0f;
        return Make(name, duration, t =>
        {
            float f = Mathf.Lerp(f0, f1, t / duration) + (float)(r.NextDouble() - 0.5) * 40f;
            phase += TwoPi * f / Rate;
            float saw = (phase / TwoPi) % 1f * 2f - 1f;
            float env = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((duration - t) / 0.1f);
            return saw * env * 0.15f;
        });
    }

    private static float Env(float t, float attack, float decay)
    {
        float a = attack <= 0f ? 1f : Mathf.Clamp01(t / attack);
        return a * Mathf.Exp(-t * decay);
    }

    private static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

    private static AudioClip Make(string name, float duration, Func<float, float> sample)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(duration * Rate));
        float[] data = new float[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("Synth_" + name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
