using UnityEngine;

/// <summary>
/// Bir konuşanın sesini çalar (VoiceChat tarafından oluşturulur).
///
/// Neden OnAudioFilterRead: Unity'nin akışlı AudioClip'i (PCMReaderCallback) veriyi büyük bloklar halinde
/// (~250 ms) ister; ağdan gelen ses ise 40 ms'lik paketlerle gelir. Blok tamponda olandan büyük olunca
/// boşluklar sessizlikle dolar → ses "kesik kesik" gelir. Burada ses kartının küçük bloklarına (~20 ms)
/// doğrudan yazıyoruz.
///
/// Titreşim tamponu: ~120 ms birikince çalmaya başlar. Tampon şişerse biraz hızlı, azalırsa biraz yavaş çalar
/// (fark edilmeyecek kadar: %1.5–4); böylece gecikme birikmez ve ses kopmaz. Başlarken / biterken
/// kısa bir yumuşatma yapılır (çıt sesi olmaz).
/// </summary>
public class VoicePlayback : MonoBehaviour
{
    private const int Rate = VoiceChat.Rate;
    private const int Capacity = Rate * 2;

    /// <summary>Ses seviyesi çarpanı (0–2). Ses iş parçacığından okunur.</summary>
    public float Gain { get => gain; set => gain = value; }
    private volatile float gain = 1f;

    private readonly float[] ring = new float[Capacity];
    private readonly object sync = new object();
    private int readPos, writePos, count;
    private double frac;
    private bool buffering = true;
    private float env, lastSample, lp1, lp2, lpAlpha;
    private double baseStep;
    private AudioSource src;

    private void Awake()
    {
        int outRate = AudioSettings.outputSampleRate;
        if (outRate <= 0) outRate = 48000;
        baseStep = Rate / (double)outRate;
        // 16 kHz → çıkış hızına yükseltirken oluşan tiz yansımaları (cızırtı) süz: ~7 kHz alçak geçiren
        lpAlpha = 1f - Mathf.Exp(-2f * Mathf.PI * 7000f / outRate);

        src = gameObject.AddComponent<AudioSource>();
        src.spatialBlend = 0f; // takım sohbeti: her yerden duyulur
        src.playOnAwake = false;
        src.loop = true;
        src.priority = 32;
        src.bypassEffects = true;
        // Filtrenin çağrılması için kaynağın bir şey çalıyor olması gerekir: sessiz döngü
        src.clip = AudioClip.Create("voice_silence", outRate, 1, outRate, false);
        src.Play();
    }

    private void OnDestroy()
    {
        if (src != null)
        {
            src.Stop();
            if (src.clip != null) Destroy(src.clip);
        }
    }

    /// <summary>Ana iş parçacığı: çözülmüş 16 kHz örnekleri tampona ekler.</summary>
    public void Push(float[] samples, int n)
    {
        lock (sync)
        {
            // Aşırı birikme (ör. oyun takıldı): en eskileri atıp ~200 ms'ye indir
            if (count + n > Rate * 6 / 10)
            {
                int drop = Mathf.Min(count, count + n - Rate * 2 / 10);
                readPos = (readPos + drop) % Capacity;
                count -= drop;
            }
            for (int i = 0; i < n; i++)
            {
                ring[writePos] = samples[i];
                writePos = (writePos + 1) % Capacity;
            }
            count += n;
        }
    }

    /// <summary>Ses iş parçacığı: Unity API kullanılmaz.</summary>
    private void OnAudioFilterRead(float[] data, int channels)
    {
        int frames = data.Length / channels;
        float g = gain;
        lock (sync)
        {
            if (buffering && count >= Rate * 12 / 100) buffering = false;

            // Gecikme kontrolü: tampon büyükse hafif hızlan, azsa hafif yavaşla
            double step = baseStep;
            if (count > Rate * 30 / 100) step *= 1.04;
            else if (count > Rate * 20 / 100) step *= 1.015;
            else if (count < Rate * 6 / 100) step *= 0.985;

            for (int f = 0; f < frames; f++)
            {
                if (!buffering && count < 2) buffering = true; // tampon bitti: yeniden biriktir

                if (!buffering)
                {
                    float a = ring[readPos], b = ring[(readPos + 1) % Capacity];
                    lastSample = a + (b - a) * (float)frac;
                    frac += step;
                    while (frac >= 1.0 && count > 0)
                    {
                        frac -= 1.0;
                        readPos = (readPos + 1) % Capacity;
                        count--;
                    }
                    env += (1f - env) * 0.01f;
                }
                else
                {
                    env *= 0.995f; // son örnekten yumuşakça sessizliğe
                }

                lp1 += lpAlpha * (lastSample - lp1);
                lp2 += lpAlpha * (lp1 - lp2);
                float o = lp2 * env * g;
                if (o > 1f) o = 1f; else if (o < -1f) o = -1f;
                int baseIdx = f * channels;
                for (int c = 0; c < channels; c++) data[baseIdx + c] = o;
            }
        }
    }
}
