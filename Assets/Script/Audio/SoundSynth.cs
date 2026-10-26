using System;
using UnityEngine;

/// <summary>
/// Ses dosyası olmadan sesleri koddan üretir (basit sentez).
/// Her ses: yumuşak atak/bırakma zarfı + alçak geçiren filtre + tepe normalizasyonu ile üretilir,
/// böylece klik/patlama olmaz. Gerçek sesler SoundBank ile verilince bunlar kullanılmaz.
/// Üretim tek seferliktir (GameAudio önbelleğe alır).
/// </summary>
public static class SoundSynth
{
    private const int Rate = 44100;
    private const float TwoPi = Mathf.PI * 2f;

    /// <summary>Döngülü ortam sesi türleri (Ambience.cs kullanır).</summary>
    public enum AmbienceKind { MuseumHum, Tension, Truck }

    public static AudioClip[] Create(SoundId id)
    {
        switch (id)
        {
            case SoundId.Footstep:
                return new[] { FootHard(1), FootHard(2), FootHard(3), FootHard(4) };
            case SoundId.FootstepSoft:
                return new[] { FootSoft(1), FootSoft(2), FootSoft(3) };
            case SoundId.FootstepMetal:
                return new[] { FootMetal(1), FootMetal(2), FootMetal(3) };
            case SoundId.Pickup: return new[] { Pickup() };
            case SoundId.Drop: return new[] { Thud("Drop", 120f, 58f, 0.26f, 0.09f, 41) };
            case SoundId.ItemThud: return new[] { Thud("ItemThud", 95f, 50f, 0.3f, 0.06f, 7) };
            case SoundId.BagOpen: return new[] { Zipper("BagOpen", 0.34f, 46f, 11, true) };
            case SoundId.BagClose: return new[] { Zipper("BagClose", 0.22f, 62f, 12, false) };
            case SoundId.CoinThrow: return new[] { CoinThrow() };
            case SoundId.CoinLand: return new[] { CoinLand() };
            case SoundId.HoldComplete: return new[] { HoldComplete() };
            case SoundId.Steal: return new[] { Steal() };
            case SoundId.UiClick:
                return new[] { Make("UiClick", 0.06f, t => (Mathf.Sin(TwoPi * 1300f * t) + 0.25f * Mathf.Sin(TwoPi * 2600f * t)) * Env(t, 0.0015f, 85f), 0.3f, 6000f) };
            case SoundId.DoorOpen: return new[] { Creak("DoorOpen", 0.55f, 150f, 235f, 21) };
            case SoundId.DoorClose: return new[] { DoorClose() };
            case SoundId.Sell: return new[] { Sell() };
            case SoundId.Purchase: return new[] { Purchase() };
            case SoundId.Swap: return new[] { Swap() };
            case SoundId.Alarm: return new[] { Alarm() };
            case SoundId.Ping:
                return new[] { Make("Ping", 0.32f, t => Bell(t, 1760f, 12f) + 0.3f * Bell(t, 2640f, 16f), 0.32f, 8000f) };
            case SoundId.Caught: return new[] { Caught() };
            case SoundId.GuardAlert: return new[] { GuardAlert() };
            case SoundId.GuardHuh: return new[] { GuardHuh() };
            case SoundId.TruckEngine: return new[] { TruckEngine() };
            case SoundId.PoliceSiren: return new[] { PoliceSiren() };
            case SoundId.TruckBrake: return new[] { TruckBrake() };
            case SoundId.AlarmSting: return new[] { AlarmSting() };
            case SoundId.Spotted: return new[] { Spotted() };
            case SoundId.CoinToss: return new[] { CoinToss() };
            case SoundId.CoinLandSoft: return new[] { CoinLandSoft() };
            case SoundId.Slip: return new[] { Slip() };
            case SoundId.Pickpocket: return new[] { Pickpocket() };
            case SoundId.PaintThrow: return new[] { PaintThrow() };
            case SoundId.PaintSplat: return new[] { PaintSplat() };
            default:
                return Array.Empty<AudioClip>();
        }
    }

    // ================= Ayak sesleri =================

    /// <summary>Sert zemin (fayans/mermer): topuk tıkı + tok gövde + ayak ucu vuruşu.</summary>
    private static AudioClip FootHard(int seed)
    {
        System.Random r = new System.Random(seed * 31);
        float lp = 0f;
        float cut = 0.09f + seed * 0.02f;
        float thump = 78f + seed * 7f;
        return Make("Footstep" + seed, 0.2f, t =>
        {
            lp += (Noise(r) - lp) * cut;
            float heel = lp * Env(t, 0.001f, 36f) * 1.6f;
            float body = Mathf.Sin(TwoPi * thump * t) * Env(t, 0.002f, 38f) * 0.5f;
            float t2 = t - 0.08f;
            float toe = t2 > 0f ? lp * Env(t2, 0.001f, 55f) * 0.5f : 0f;
            return heel + body + toe;
        }, 0.5f, 2600f);
    }

    /// <summary>Halı / yumuşak zemin: tıksız, boğuk hışırtı.</summary>
    private static AudioClip FootSoft(int seed)
    {
        System.Random r = new System.Random(seed * 53);
        float lp = 0f, lp2 = 0f;
        return Make("FootstepSoft" + seed, 0.2f, t =>
        {
            lp += (Noise(r) - lp) * 0.07f;
            lp2 += (lp - lp2) * 0.25f;
            float body = Mathf.Sin(TwoPi * (62f + seed * 5f) * t) * Env(t, 0.006f, 26f) * 0.35f;
            return lp2 * 2.2f * Env(t, 0.006f, 20f) + body;
        }, 0.38f, 900f);
    }

    /// <summary>Metal zemin (kamyon kasası): tok vuruş + kısa metalik çınlama.</summary>
    private static AudioClip FootMetal(int seed)
    {
        System.Random r = new System.Random(seed * 17);
        float lp = 0f;
        float f0 = 340f + seed * 23f;
        return Make("FootstepMetal" + seed, 0.28f, t =>
        {
            lp += (Noise(r) - lp) * 0.2f;
            float thump = Mathf.Sin(TwoPi * 88f * t) * Env(t, 0.002f, 30f) * 0.55f;
            float ring = (Mathf.Sin(TwoPi * f0 * t) * Env(t, 0.001f, 26f)
                        + 0.6f * Mathf.Sin(TwoPi * f0 * 2.31f * t) * Env(t, 0.001f, 38f)
                        + 0.35f * Mathf.Sin(TwoPi * f0 * 3.87f * t) * Env(t, 0.001f, 55f)) * 0.3f;
            return thump + ring + lp * Env(t, 0.001f, 60f) * 0.8f;
        }, 0.45f, 4200f);
    }

    // ================= Eşya / etkileşim =================

    private static AudioClip Pickup()
    {
        return Make("Pickup", 0.2f, t =>
        {
            float f = Mathf.Lerp(620f, 1180f, Mathf.Clamp01(t / 0.09f));
            float tone = Mathf.Sin(TwoPi * f * t) + 0.2f * Mathf.Sin(TwoPi * f * 2f * t);
            return tone * Env(t, 0.004f, 16f);
        }, 0.38f, 6000f);
    }

    /// <summary>Düşme / darbe: perdesi düşen gövde + yumuşak toz.</summary>
    private static AudioClip Thud(string name, float f0, float f1, float dur, float noiseAmt, int seed)
    {
        System.Random r = new System.Random(seed);
        float lp = 0f, ph = 0f;
        return Make(name, dur, t =>
        {
            lp += (Noise(r) - lp) * 0.1f;
            float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / (dur * 0.7f)));
            ph += TwoPi * f / Rate;
            return Mathf.Sin(ph) * Env(t, 0.002f, 17f) + lp * Env(t, 0.001f, 40f) * (noiseAmt * 10f);
        }, 0.55f, 1400f);
    }

    /// <summary>Fermuar: yumuşak kapılı yüksek frekanslı hışırtı.</summary>
    private static AudioClip Zipper(string name, float dur, float teeth, int seed, bool rising)
    {
        System.Random r = new System.Random(seed);
        float prev = 0f;
        return Make(name, dur, t =>
        {
            float n = Noise(r);
            float hp = n - prev;
            prev = n;
            float gate = 0.55f + 0.45f * Mathf.Sin(TwoPi * teeth * t); // sert kesme yok
            float k = t / dur;
            float shape = rising ? Mathf.Lerp(0.6f, 1f, k) : Mathf.Lerp(1f, 0.6f, k);
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k)) ;
            return hp * gate * shape * env;
        }, 0.3f, 6500f, 0.02f, 0.02f);
    }

    private static AudioClip CoinThrow()
    {
        System.Random r = new System.Random(3);
        Svf f = new Svf();
        return Make("CoinThrow", 0.22f, t =>
        {
            float k = t / 0.22f;
            float swish = f.Band(Noise(r), Mathf.Lerp(1800f, 4800f, k), 0.9f) * Mathf.Sin(Mathf.PI * k) * 1.4f;
            float t2 = t - 0.03f;
            float tink = t2 > 0f ? Mathf.Sin(TwoPi * 3300f * t2) * Env(t2, 0.001f, 30f) * 0.35f : 0f;
            return swish + tink;
        }, 0.35f, 7000f);
    }

    private static float CoinHit(float lt, float f, float decay)
    {
        if (lt < 0f) return 0f;
        return (Mathf.Sin(TwoPi * f * lt) + 0.55f * Mathf.Sin(TwoPi * f * 2.41f * lt) * Mathf.Exp(-lt * decay * 0.5f)
              + 0.3f * Mathf.Sin(TwoPi * f * 3.9f * lt) * Mathf.Exp(-lt * decay)) * Env(lt, 0.0006f, decay);
    }

    private static AudioClip CoinLand()
    {
        return Make("CoinLand", 0.75f, t =>
            CoinHit(t, 2637f, 8f) + 0.55f * CoinHit(t - 0.13f, 2800f, 11f) + 0.3f * CoinHit(t - 0.21f, 2990f, 15f) + 0.15f * CoinHit(t - 0.26f, 3100f, 20f),
            0.34f, 8500f);
    }

    /// <summary>Yumuşak zemine düşen para: daha alçak, çok sayıda küçük şıngırtı.</summary>
    private static AudioClip CoinLandSoft()
    {
        return Make("CoinLandSoft", 0.55f, t =>
            0.8f * CoinHit(t, 2100f, 24f) + 0.6f * CoinHit(t - 0.05f, 2350f, 26f) + 0.45f * CoinHit(t - 0.10f, 1950f, 28f)
          + 0.35f * CoinHit(t - 0.14f, 2500f, 30f) + 0.25f * CoinHit(t - 0.19f, 2200f, 34f) + 0.15f * CoinHit(t - 0.23f, 2650f, 38f),
            0.28f, 6500f);
    }

    /// <summary>Havada dönen bozuk para çınlaması.</summary>
    private static AudioClip CoinToss()
    {
        return Make("CoinToss", 0.5f, t =>
        {
            float shimmer = 0.6f + 0.4f * Mathf.Sin(TwoPi * 34f * t);
            float rise = Mathf.Lerp(3100f, 3500f, Mathf.Clamp01(t / 0.12f));
            float tone = Mathf.Sin(TwoPi * rise * t) + 0.4f * Mathf.Sin(TwoPi * rise * 2.4f * t);
            return tone * shimmer * Env(t, 0.003f, 7f);
        }, 0.3f, 8000f);
    }

    private static AudioClip HoldComplete()
    {
        return Make("HoldComplete", 0.6f, t =>
        {
            float t2 = t - 0.09f;
            return Bell(t, 880f, 7f) + (t2 > 0f ? Bell(t2, 1320f, 6f) : 0f);
        }, 0.34f, 7000f);
    }

    /// <summary>Sinsi hırsız arpejyosu: inen üç yumuşak nota.</summary>
    private static AudioClip Steal()
    {
        return Make("Steal", 0.36f, t =>
        {
            int step = Mathf.Min(2, (int)(t / 0.09f));
            float lt = t - step * 0.09f;
            float f = step == 0 ? 880f : step == 1 ? 698f : 523f;
            float tri = Mathf.Asin(Mathf.Sin(TwoPi * f * lt)) * (2f / Mathf.PI);
            return tri * Env(lt, 0.004f, 24f);
        }, 0.32f, 3500f);
    }

    private static AudioClip Creak(string name, float dur, float f0, float f1, int seed)
    {
        System.Random r = new System.Random(seed);
        float phase = 0f, jitter = 0f;
        return Make(name, dur, t =>
        {
            jitter += ((float)(r.NextDouble() - 0.5) * 60f - jitter) * 0.002f; // yavaş titreşim
            float k = t / dur;
            float f = Mathf.Lerp(f0, f1, k * k) + jitter + Mathf.Sin(TwoPi * 9f * t) * 8f;
            phase += TwoPi * f / Rate;
            float saw = phase / TwoPi % 1f * 2f - 1f;
            float am = 0.65f + 0.35f * Mathf.Sin(TwoPi * 17f * t);
            return saw * am * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k));
        }, 0.3f, 1100f, 0.03f, 0.05f);
    }

    private static AudioClip DoorClose()
    {
        System.Random r = new System.Random(22);
        float lp = 0f;
        return Make("DoorClose", 0.34f, t =>
        {
            lp += (Noise(r) - lp) * 0.1f;
            float thump = Mathf.Sin(TwoPi * 72f * t) * Env(t, 0.003f, 16f);
            float t2 = t - 0.05f;
            float latch = t2 > 0f ? lp * Env(t2, 0.001f, 70f) * 0.8f : 0f;
            return thump * 0.8f + lp * Env(t, 0.002f, 22f) * 0.6f + latch;
        }, 0.5f, 1500f);
    }

    /// <summary>Yazar kasa: "çık" + zil.</summary>
    private static AudioClip Sell()
    {
        System.Random r = new System.Random(9);
        return Make("Sell", 0.8f, t =>
        {
            float click = t < 0.02f ? Noise(r) * (1f - t / 0.02f) * 0.5f : 0f;
            float t2 = t - 0.06f, t3 = t - 0.16f;
            float a = t2 > 0f ? Bell(t2, 2093f, 6f) : 0f;
            float b = t3 > 0f ? Bell(t3, 3136f, 7f) * 0.7f : 0f;
            return click + a + b;
        }, 0.32f, 8000f);
    }

    private static AudioClip Purchase()
    {
        return Make("Purchase", 0.5f, t =>
        {
            int step = Mathf.Min(2, (int)(t / 0.08f));
            float lt = t - step * 0.08f;
            float f = step == 0 ? 660f : step == 1 ? 880f : 1320f;
            return (Mathf.Sin(TwoPi * f * lt) + 0.25f * Mathf.Sin(TwoPi * f * 2f * lt)) * Env(lt, 0.004f, step == 2 ? 7f : 22f);
        }, 0.32f, 6000f);
    }

    private static AudioClip Swap()
    {
        System.Random r = new System.Random(31);
        Svf f = new Svf();
        return Make("Swap", 0.38f, t =>
        {
            float k = Mathf.Clamp01(t / 0.26f);
            float slide = f.Band(Noise(r), Mathf.Lerp(600f, 2600f, k), 1.1f) * Mathf.Sin(Mathf.PI * k);
            float t2 = t - 0.24f;
            float click = t2 > 0f ? Mathf.Sin(TwoPi * 760f * t2) * Env(t2, 0.001f, 55f) * 0.45f : 0f;
            return slide * 1.4f + click;
        }, 0.34f, 5000f);
    }

    // ================= Alarm / gerilim =================

    private static AudioClip Alarm()
    {
        float ph = 0f, fc = 880f;
        return Make("Alarm", 1.2f, t =>
        {
            float target = (t % 0.6f) < 0.3f ? 880f : 660f;
            fc += (target - fc) * 0.003f; // tonlar arasında yumuşak geçiş
            ph += TwoPi * fc / Rate;
            return Mathf.Sin(ph) + 0.33f * Mathf.Sin(3f * ph) + 0.18f * Mathf.Sin(5f * ph);
        }, 0.3f, 4500f, 0.02f, 0.05f);
    }

    /// <summary>Kısa gerilim vuruşu: ters yükselen hava + derin bom + uyumsuz tını.</summary>
    private static AudioClip AlarmSting()
    {
        System.Random r = new System.Random(66);
        Svf f = new Svf();
        float ph = 0f, p1 = 0f, p2 = 0f;
        return Make("AlarmSting", 0.95f, t =>
        {
            float swell = 0f;
            if (t < 0.2f)
            {
                float k = t / 0.2f;
                swell = f.Low(Noise(r), 400f + 2200f * k) * k * k * 1.6f;
            }
            float h = t - 0.2f;
            if (h < 0f) return swell;
            ph += TwoPi * Mathf.Lerp(82f, 44f, Mathf.Clamp01(h / 0.4f)) / Rate;
            p1 += TwoPi * 311f / Rate;
            p2 += TwoPi * 330f / Rate;
            float boom = Mathf.Sin(ph) * Env(h, 0.004f, 4.5f);
            float s1 = (p1 / TwoPi % 1f * 2f - 1f), s2 = (p2 / TwoPi % 1f * 2f - 1f);
            float stab = (s1 + s2) * 0.25f * Env(h, 0.005f, 5.5f);
            return swell + boom + stab;
        }, 0.6f, 1400f, 0.01f, 0.1f);
    }

    /// <summary>"Fark edildin!": iki keskin yükselen vuruş + alt vuruş.</summary>
    private static AudioClip Spotted()
    {
        return Make("Spotted", 0.42f, t =>
        {
            float t2 = t - 0.11f;
            float a = (Mathf.Sin(TwoPi * 1760f * t) + 0.4f * Mathf.Sin(TwoPi * 3520f * t) + 0.2f * Mathf.Sin(TwoPi * 5280f * t)) * Env(t, 0.002f, 13f);
            float b = t2 > 0f ? (Mathf.Sin(TwoPi * 2349f * t2) + 0.4f * Mathf.Sin(TwoPi * 4698f * t2)) * Env(t2, 0.002f, 9f) : 0f;
            float low = Mathf.Sin(TwoPi * 140f * t) * Env(t, 0.003f, 22f) * 0.7f;
            return a * 0.7f + b + low;
        }, 0.5f, 6500f);
    }

    private static AudioClip Caught()
    {
        float ph = 0f;
        System.Random r = new System.Random(2);
        return Make("Caught", 1.05f, t =>
        {
            int step = Mathf.Min(2, (int)(t / 0.27f));
            float lt = t - step * 0.27f;
            float f = step == 0 ? 392f : step == 1 ? 370f : 349f;
            float vib = 0f;
            if (step == 2)
            {
                f *= 1f - 0.07f * Mathf.Clamp01(lt / 0.6f);
                vib = Mathf.Sin(TwoPi * 6f * lt) * 6f * Mathf.Clamp01(lt / 0.15f);
            }
            ph += TwoPi * (f + vib) / Rate;
            float saw = ph / TwoPi % 1f * 2f - 1f;
            float wah = 0.7f + 0.3f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(lt / 0.25f)); // "vah" açılması
            return saw * wah * Env(lt, 0.02f, step == 2 ? 1.8f : 5f) + Noise(r) * 0.01f;
        }, 0.38f, 1300f);
    }

    private static AudioClip GuardAlert()
    {
        return Make("GuardAlert", 0.4f, t =>
        {
            int step = t < 0.15f ? 0 : 1;
            float lt = t - step * 0.15f;
            float f = step == 0 ? 1046f : 1568f;
            return (Mathf.Sin(TwoPi * f * lt) + 0.3f * Mathf.Sin(TwoPi * f * 2f * lt)) * Env(lt, 0.003f, step == 0 ? 20f : 9f);
        }, 0.34f, 6500f);
    }

    /// <summary>"Hm?" - yükselen, yumuşak soru tonu.</summary>
    private static AudioClip GuardHuh()
    {
        float ph = 0f;
        return Make("GuardHuh", 0.36f, t =>
        {
            float k = Mathf.Clamp01(t / 0.3f);
            ph += TwoPi * Mathf.Lerp(250f, 470f, k * k) / Rate;
            float saw = ph / TwoPi % 1f * 2f - 1f;
            return saw * Env(t, 0.03f, 5.5f);
        }, 0.34f, 1100f);
    }

    // ================= Kamyon / polis =================

    private static AudioClip TruckEngine()
    {
        System.Random er = new System.Random(77);
        float ph = 0f, lp = 0f;
        return Make("TruckEngine", 2.4f, t =>
        {
            float rpm = t < 0.5f ? Mathf.Lerp(25f, 48f, t / 0.5f) + Mathf.Sin(TwoPi * 14f * t) * 6f : Mathf.Lerp(48f, 70f, (t - 0.5f) / 1.9f);
            ph += TwoPi * rpm / Rate;
            lp += (Noise(er) - lp) * 0.05f;
            float puff = Mathf.Pow(Mathf.Abs(Mathf.Sin(ph * 0.5f)), 3f);
            float body = Mathf.Sin(ph) * 0.5f + Mathf.Sin(ph * 2f) * 0.3f + Mathf.Sin(ph * 3f) * 0.15f;
            return body * (0.5f + puff * 0.5f) + lp * 0.25f;
        }, 0.6f, 700f, 0.08f, 0.4f);
    }

    private static AudioClip PoliceSiren()
    {
        float sp = 0f;
        return Make("PoliceSiren", 1.6f, t =>
        {
            float k = (t % 0.8f) / 0.8f;
            float f = 620f + Mathf.Sin(k * Mathf.PI) * 420f;
            sp += TwoPi * f / Rate;
            return Mathf.Sin(sp) * 0.75f + Mathf.Sin(sp * 2f) * 0.15f + Mathf.Sin(sp * 3f) * 0.06f;
        }, 0.34f, 4000f, 0.03f, 0.05f);
    }

    private static AudioClip TruckBrake()
    {
        System.Random br = new System.Random(5);
        float lp2 = 0f, bp = 0f;
        return Make("TruckBrake", 0.95f, t =>
        {
            lp2 += (Noise(br) - lp2) * 0.22f;
            bp += TwoPi * Mathf.Lerp(1400f, 700f, t / 0.95f) / Rate;
            float screech = (Mathf.Sin(bp) * 0.5f + lp2 * 0.5f) * Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((0.95f - t) / 0.35f);
            float thud = Mathf.Sin(TwoPi * 68f * t) * Env(t, 0.004f, 8f);
            return screech * 0.4f + thud * 0.8f;
        }, 0.55f, 3000f);
    }

    // ================= Komik / yeni olaylar =================

    /// <summary>Kayma: aşağı kayan "vııuu" düdük + sonda tok düşüş.</summary>
    private static AudioClip Slip()
    {
        System.Random r = new System.Random(88);
        float ph = 0f, lp = 0f;
        return Make("Slip", 0.85f, t =>
        {
            float whistle = 0f;
            if (t < 0.4f)
            {
                float k = t / 0.4f;
                float f = Mathf.Lerp(1500f, 300f, Mathf.Pow(k, 0.8f)) + Mathf.Sin(TwoPi * 8f * t) * 18f;
                ph += TwoPi * f / Rate;
                whistle = (Mathf.Sin(ph) + 0.2f * Mathf.Sin(ph * 2f)) * Mathf.Clamp01(t / 0.015f) * Mathf.Clamp01((0.4f - t) / 0.04f) * 0.7f;
            }
            float h = t - 0.43f;
            float fall = 0f;
            if (h > 0f)
            {
                lp += (Noise(r) - lp) * 0.1f;
                fall = (Mathf.Sin(TwoPi * 90f * h) * Env(h, 0.003f, 13f) + lp * Env(h, 0.002f, 30f) * 1.2f) * 1.1f;
            }
            return whistle + fall;
        }, 0.5f, 5500f);
    }

    /// <summary>Yankesicilik: hafif kumaş hışırtısı.</summary>
    private static AudioClip Pickpocket()
    {
        System.Random r = new System.Random(55);
        Svf f = new Svf();
        float am = 0f;
        return Make("Pickpocket", 0.3f, t =>
        {
            am += ((float)r.NextDouble() - am) * 0.0018f;
            float env = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.3f)), 1.5f);
            return f.Band(Noise(r), 3000f, 0.9f) * (0.3f + am * 1.6f) * env;
        }, 0.2f, 5500f, 0.03f, 0.05f);
    }

    /// <summary>Boya topu fırlatma: "fwip".</summary>
    private static AudioClip PaintThrow()
    {
        System.Random r = new System.Random(71);
        Svf f = new Svf();
        float ph = 0f;
        return Make("PaintThrow", 0.2f, t =>
        {
            float k = t / 0.2f;
            float air = f.Band(Noise(r), Mathf.Lerp(700f, 3600f, k), 0.8f) * Mathf.Sin(Mathf.PI * k) * 1.4f;
            ph += TwoPi * Mathf.Lerp(380f, 1100f, k) / Rate;
            return air + Mathf.Sin(ph) * Mathf.Sin(Mathf.PI * k) * 0.25f;
        }, 0.32f, 6000f, 0.01f, 0.03f);
    }

    /// <summary>Boya sıçraması: ıslak patlama + kabarcıklar.</summary>
    private static AudioClip PaintSplat()
    {
        System.Random r = new System.Random(72);
        Svf f = new Svf();
        float ph = 0f;
        return Make("PaintSplat", 0.45f, t =>
        {
            float wet = f.Low(Noise(r), Mathf.Lerp(2600f, 300f, Mathf.Clamp01(t / 0.2f))) * Env(t, 0.002f, 11f) * 1.6f;
            ph += TwoPi * Mathf.Lerp(260f, 80f, Mathf.Clamp01(t / 0.12f)) / Rate;
            float plop = Mathf.Sin(ph) * Env(t, 0.003f, 18f) * 0.8f;
            float bubbles = Blip(t - 0.10f, 430f) + Blip(t - 0.17f, 560f) * 0.8f + Blip(t - 0.26f, 380f) * 0.6f;
            return wet + plop + bubbles * 0.35f;
        }, 0.5f, 3500f);
    }

    private static float Blip(float lt, float f)
    {
        if (lt < 0f) return 0f;
        return Mathf.Sin(TwoPi * (f + lt * 1800f) * lt) * Env(lt, 0.002f, 55f);
    }

    // ================= Atmosfer döngüleri =================

    /// <summary>Dikişsiz döngülü ortam sesi üretir (Ambience.cs bir kez çağırır).</summary>
    public static AudioClip CreateAmbience(AmbienceKind kind)
    {
        switch (kind)
        {
            case AmbienceKind.MuseumHum: return MuseumHum();
            case AmbienceKind.Tension: return TensionLoop();
            default: return TruckAmbience();
        }
    }

    /// <summary>Havalandırma uğultusu + çok hafif elektrik vızıltısı.</summary>
    private static AudioClip MuseumHum()
    {
        System.Random r = new System.Random(101);
        Svf low = new Svf(), air = new Svf();
        return MakeLoop("Amb_Museum", 8f, 0.5f, t =>
        {
            float n = Noise(r);
            float rumble = low.Low(n, 140f) * 2.4f;
            float vent = air.Band(n, 650f + 120f * Mathf.Sin(TwoPi * 0.125f * t), 0.7f) * (0.35f + 0.25f * Mathf.Sin(TwoPi * 0.25f * t));
            float hum = Mathf.Sin(TwoPi * 50f * t) * 0.05f + Mathf.Sin(TwoPi * 100f * t) * 0.025f;
            return rumble + vent + hum;
        }, 0.5f);
    }

    /// <summary>Alarm sırasında: alçak, vuran drone + kalp atışı.</summary>
    private static AudioClip TensionLoop()
    {
        System.Random r = new System.Random(102);
        Svf low = new Svf();
        return MakeLoop("Amb_Tension", 4f, 0.15f, t =>
        {
            float drone = Mathf.Sin(TwoPi * 55f * t) * 0.5f + Mathf.Sin(TwoPi * 58f * t) * 0.5f; // 3 Hz vurma
            float tt = t % 0.8f;
            float lub = Mathf.Sin(TwoPi * 52f * tt) * Env(tt, 0.004f, 14f);
            float t2 = tt - 0.22f;
            float dub = t2 > 0f ? Mathf.Sin(TwoPi * 46f * t2) * Env(t2, 0.004f, 16f) * 0.7f : 0f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(TwoPi * 2.5f * t);
            float whine = Mathf.Sin(TwoPi * 1318f * t) * 0.025f * pulse;
            float dust = low.Low(Noise(r), 220f) * 0.5f;
            return drone * 0.55f + (lub + dub) * 0.8f + whine + dust;
        }, 0.55f);
    }

    /// <summary>Kamyon/garaj: yavaş rölanti uğultusu + hafif metal tıngırtısı.</summary>
    private static AudioClip TruckAmbience()
    {
        System.Random r = new System.Random(103);
        Svf low = new Svf(), rat = new Svf();
        float am = 0f;
        return MakeLoop("Amb_Truck", 5f, 0.4f, t =>
        {
            float n = Noise(r);
            float p = Mathf.Sin(TwoPi * 22f * t);
            float idle = (p + 0.5f * Mathf.Sin(TwoPi * 44f * t) + 0.25f * Mathf.Sin(TwoPi * 66f * t)) * (0.7f + 0.3f * Mathf.Sin(TwoPi * 11f * t));
            am += ((float)r.NextDouble() - am) * 0.0004f;
            float rattle = rat.Band(n, 1900f, 0.8f) * am * 0.5f;
            return idle * 0.35f + low.Low(n, 180f) * 1.2f + rattle;
        }, 0.5f);
    }

    /// <summary>Uzaktan gelen, yankılı adımlar (Ambience.cs ara sıra çalar).</summary>
    public static AudioClip CreateFarSteps()
    {
        System.Random r = new System.Random(104);
        float lp = 0f;
        float[] dry = Render(2.4f, t =>
        {
            lp += (Noise(r) - lp) * 0.06f;
            float sum = 0f;
            for (int i = 0; i < 4; i++)
            {
                float lt = t - (0.1f + i * 0.42f);
                if (lt > 0f) sum += (lp * 1.8f + Mathf.Sin(TwoPi * 85f * lt) * 0.4f) * Env(lt, 0.002f, 40f);
            }
            return sum;
        });
        Echo(dry, 0.17f, 0.5f);
        Echo(dry, 0.31f, 0.35f);
        return Finish("FarSteps", dry, 0.25f, 650f, 0.01f, 0.25f);
    }

    // ================= Yardımcılar =================

    /// <summary>Çan benzeri, sönümlenen tını (hafif harmonikli).</summary>
    private static float Bell(float t, float f, float decay)
    {
        return (Mathf.Sin(TwoPi * f * t) + 0.35f * Mathf.Sin(TwoPi * f * 2.76f * t) * Mathf.Exp(-t * decay * 0.6f)) * Env(t, 0.003f, decay);
    }

    private static float Env(float t, float attack, float decay)
    {
        float a = attack <= 0f ? 1f : Mathf.Clamp01(t / attack);
        return a * Mathf.Exp(-t * decay);
    }

    private static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

    /// <summary>Chamberlin durum-değişkenli filtre (Low / Band). Frekans ~6 kHz altında kararlı.</summary>
    private sealed class Svf
    {
        private float low, band;

        private void Step(float x, float hz, float damp)
        {
            float fc = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(hz, 6000f) / Rate);
            low += fc * band;
            float high = x - low - damp * band;
            band += fc * high;
        }

        public float Low(float x, float hz) { Step(x, hz, 1.2f); return low; }
        public float Band(float x, float hz, float damp) { Step(x, hz, damp); return band; }
    }

    private static float[] Render(float duration, Func<float, float> sample)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(duration * Rate));
        float[] data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float v = sample(i / (float)Rate);
            data[i] = float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        }
        return data;
    }

    private static void Echo(float[] d, float delaySec, float feedback)
    {
        int delay = Mathf.RoundToInt(delaySec * Rate);
        for (int i = delay; i < d.Length; i++) d[i] += d[i - delay] * feedback;
    }

    private static void OnePole(float[] d, float hz)
    {
        float a = 1f - Mathf.Exp(-TwoPi * hz / Rate);
        float y = 0f;
        for (int i = 0; i < d.Length; i++) { y += (d[i] - y) * a; d[i] = y; }
    }

    /// <summary>Alçak geçiren (2 kutuplu) + DC temizliği + tepe normalizasyonu + zarf kenarları.</summary>
    private static AudioClip Finish(string name, float[] d, float peak, float lpHz, float fadeIn, float fadeOut)
    {
        if (lpHz > 0f) { OnePole(d, lpHz); OnePole(d, lpHz); }

        // DC kaymasını at (çok alçak geçiren çıkarımı)
        float dc = 0f, hp = 1f - Mathf.Exp(-TwoPi * 25f / Rate);
        for (int i = 0; i < d.Length; i++) { dc += (d[i] - dc) * hp; d[i] -= dc; }

        float max = 0f;
        for (int i = 0; i < d.Length; i++) max = Mathf.Max(max, Mathf.Abs(d[i]));
        float g = max > 1e-5f ? peak / max : 0f;

        int inN = Mathf.Min(d.Length / 3, Mathf.Max(1, Mathf.RoundToInt(fadeIn * Rate)));
        int outN = Mathf.Min(d.Length / 2, Mathf.Max(1, Mathf.RoundToInt(fadeOut * Rate)));
        for (int i = 0; i < d.Length; i++)
        {
            float w = g;
            if (i < inN) w *= i / (float)inN;
            int fromEnd = d.Length - 1 - i;
            if (fromEnd < outN) { float o = fromEnd / (float)outN; w *= o * o * (3f - 2f * o); }
            d[i] = Mathf.Clamp(d[i] * w, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("Synth_" + name, d.Length, 1, Rate, false);
        clip.SetData(d, 0);
        return clip;
    }

    private static AudioClip Make(string name, float duration, Func<float, float> sample, float peak = 0.4f, float lpHz = 0f, float fadeIn = 0.0015f, float fadeOut = 0.012f)
    {
        return Finish(name, Render(duration, sample), peak, lpHz, fadeIn, fadeOut);
    }

    /// <summary>Dikişsiz döngü: fazladan 'fade' süresi üretilip başa eşit güçle karıştırılır.</summary>
    private static AudioClip MakeLoop(string name, float duration, float fade, Func<float, float> sample, float peak)
    {
        int n = Mathf.CeilToInt(duration * Rate);
        int f = Mathf.CeilToInt(fade * Rate);
        float[] ext = Render(duration + fade, sample);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float v = ext[i];
            if (i < f)
            {
                float w = i / (float)f;
                v = ext[i] * Mathf.Sin(w * Mathf.PI * 0.5f) + ext[n + i] * Mathf.Cos(w * Mathf.PI * 0.5f);
            }
            d[i] = v;
        }

        float max = 0f;
        for (int i = 0; i < n; i++) max = Mathf.Max(max, Mathf.Abs(d[i]));
        float g = max > 1e-5f ? peak / max : 0f;
        for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i] * g, -1f, 1f);

        AudioClip clip = AudioClip.Create("Synth_" + name, n, 1, Rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
