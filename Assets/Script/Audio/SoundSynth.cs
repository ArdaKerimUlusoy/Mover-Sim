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
            case SoundId.Sell:
                return new[] { Make("Sell", 0.7f, t =>
                {
                    // Yazar kasa: "çın-çın"
                    float a = Mathf.Sin(TwoPi * 1568f * t) * Env(t, 0.003f, 9f);
                    float t2 = t - 0.09f;
                    float b = t2 > 0f ? (Mathf.Sin(TwoPi * 2093f * t2) + Mathf.Sin(TwoPi * 3136f * t2) * 0.4f) * Env(t2, 0.003f, 6f) : 0f;
                    return (a + b) * 0.22f;
                }) };
            case SoundId.Purchase:
                return new[] { Make("Purchase", 0.45f, t =>
                {
                    int step = Mathf.Min(2, (int)(t / 0.07f));
                    float lt = t - step * 0.07f;
                    float f = step == 0 ? 660f : step == 1 ? 880f : 1320f;
                    return Mathf.Sin(TwoPi * f * lt) * Env(lt, 0.004f, step == 2 ? 7f : 25f) * 0.3f;
                }) };
            case SoundId.Swap:
            {
                System.Random r = new System.Random(31);
                float lp = 0f;
                return new[] { Make("Swap", 0.35f, t =>
                {
                    lp += (Noise(r) - lp) * 0.1f;
                    float slide = lp * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.25f)) * 0.8f;
                    float t2 = t - 0.22f;
                    float click = t2 > 0f ? Mathf.Sin(TwoPi * 900f * t2) * Env(t2, 0.001f, 60f) * 0.5f : 0f;
                    return (slide + click) * 0.5f;
                }) };
            }
            case SoundId.Alarm:
                return new[] { Make("Alarm", 1.2f, t =>
                {
                    // İki tonlu siren
                    float f = (t % 0.6f) < 0.3f ? 880f : 660f;
                    float sq = Mathf.Sign(Mathf.Sin(TwoPi * f * t)) * 0.5f + Mathf.Sin(TwoPi * f * t) * 0.5f;
                    float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((1.2f - t) / 0.05f);
                    return sq * env * 0.18f;
                }) };
            case SoundId.Ping:
                return new[] { Make("Ping", 0.25f, t =>
                    (Mathf.Sin(TwoPi * 1760f * t) * 0.6f + Mathf.Sin(TwoPi * 2640f * t) * 0.3f) * Env(t, 0.002f, 16f) * 0.3f) };
            case SoundId.Caught:
                return new[] { Make("Caught", 0.9f, t =>
                {
                    // "Vah vah" inen tonlar
                    int step = Mathf.Min(2, (int)(t / 0.25f));
                    float lt = t - step * 0.25f;
                    float f = step == 0 ? 392f : step == 1 ? 370f : 349f;
                    float vib = step == 2 ? Mathf.Sin(TwoPi * 6f * lt) * 6f : 0f;
                    float saw = ((f + vib) * t % 1f) * 2f - 1f;
                    return saw * Env(lt, 0.01f, step == 2 ? 2.5f : 5f) * 0.2f;
                }) };
            case SoundId.GuardAlert:
                return new[] { Make("GuardAlert", 0.35f, t =>
                {
                    float a = Mathf.Sin(TwoPi * 1046f * t) + Mathf.Sin(TwoPi * 1568f * t) * 0.6f;
                    return a * Env(t, 0.002f, 9f) * 0.25f;
                }) };
            case SoundId.GuardHuh:
                return new[] { Make("GuardHuh", 0.3f, t =>
                {
                    float f = Mathf.Lerp(300f, 520f, t / 0.3f);
                    return (Mathf.Sin(TwoPi * f * t) + Mathf.Sin(TwoPi * f * 2f * t) * 0.3f) * Env(t, 0.02f, 8f) * 0.3f;
                }) };
            case SoundId.TruckEngine:
                {
                    // Eski kamyon motoru: kontak, gürleyen alçak ton, hafif hızlanma
                    System.Random er = new System.Random(77);
                    float ph = 0f;
                    return new[] { Make("TruckEngine", 2.4f, t =>
                    {
                        float rpm = t < 0.5f ? Mathf.Lerp(25f, 48f, t / 0.5f) + Mathf.Sin(TwoPi * 14f * t) * 6f : Mathf.Lerp(48f, 70f, (t - 0.5f) / 1.9f);
                        ph += TwoPi * rpm / Rate;
                        float puff = Mathf.Pow(Mathf.Abs(Mathf.Sin(ph * 0.5f)), 3f);
                        float body = Mathf.Sin(ph) * 0.5f + Mathf.Sin(ph * 2f) * 0.3f + Mathf.Sin(ph * 3f) * 0.15f;
                        float rumble = Noise(er) * 0.06f;
                        float env = Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((2.4f - t) / 0.4f);
                        return (body * (0.5f + puff * 0.5f) + rumble) * env * 0.45f;
                    }) };
                }
            case SoundId.PoliceSiren:
                {
                    // Sevimli "vııv-vııv": 1.6 sn'de iki kez yükselip alçalan yumuşak ton
                    float sp = 0f;
                    return new[] { Make("PoliceSiren", 1.6f, t =>
                    {
                        float k = (t % 0.8f) / 0.8f;
                        float f = 620f + Mathf.Sin(k * Mathf.PI) * 420f;
                        sp += TwoPi * f / Rate;
                        float tone = Mathf.Sin(sp) * 0.75f + Mathf.Sin(sp * 2f) * 0.15f;
                        float env = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((1.6f - t) / 0.05f);
                        return tone * env * 0.2f;
                    }) };
                }
            case SoundId.TruckBrake:
                {
                    // Lastik gıcırtısı + yumuşak tok vuruş
                    System.Random br = new System.Random(5);
                    float lp2 = 0f, bp = 0f;
                    return new[] { Make("TruckBrake", 0.9f, t =>
                    {
                        lp2 += (Noise(br) - lp2) * 0.25f;
                        bp += TwoPi * Mathf.Lerp(1500f, 700f, t / 0.9f) / Rate;
                        float screech = (Mathf.Sin(bp) * 0.5f + lp2 * 0.5f) * Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((0.9f - t) / 0.35f);
                        float thud = Mathf.Sin(TwoPi * 70f * t) * Env(t, 0.003f, 9f);
                        return screech * 0.22f + thud * 0.45f;
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
