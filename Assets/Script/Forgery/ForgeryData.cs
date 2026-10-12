using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>
/// Oyuncunun atölyede yaptığı sahtenin KENDİSİ (GDD Bölüm 10: çizim bitince sıkıştırılıp gönderilir).
/// Elde, yerde, kaidede ve çanta ikonunda görünen şey bu veriden üretilir; benzerliği de sunucu bundan hesaplar.
///   Tablo   : palet + tuval pikselleri (palet indeksi)
///   Vazo    : kilin her noktasının yarıçapı (shape) + palet + boya dokusu
///   Büst    : kaydırıcı değerleri + taş / kaide rengi (pars)
///   Mücevher: gövde, metal ve taş seçimleri (pars)
/// </summary>
public sealed class ForgeryData
{
    private const byte Version = 1;
    public const int MaxBlobBytes = 96 * 1024;

    public ArtKind kind;
    public int targetKey;   // hangi kaide için yapıldı (ArtworkDisplay.LookKey)
    public int seed;        // yapıldığı görevin tohumu

    public byte[] pars = Array.Empty<byte>();
    public Color32[] palette = Array.Empty<Color32>();
    public int width, height;
    public byte[] pixels = Array.Empty<byte>();
    public byte[] shape = Array.Empty<byte>();

    // ---------------- Vazo parametreleri (pars) ----------------
    // [0..1] yükseklik mm, [2..3] yarıçap ölçeği mm (255 = bu yarıçap), [4] radyal dilim, [5] yükseklik dilimi
    public float VaseHeight => pars.Length >= 6 ? (pars[0] | (pars[1] << 8)) / 1000f : 0.5f;
    public float VaseRadiusScale => pars.Length >= 6 ? (pars[2] | (pars[3] << 8)) / 1000f : 0.25f;
    public int VaseRadial => pars.Length >= 6 ? pars[4] : 0;
    public int VaseRows => pars.Length >= 6 ? pars[5] : 0;

    public static byte[] VasePars(float height, float radiusScale, int radial, int rows)
    {
        int h = Mathf.Clamp(Mathf.RoundToInt(height * 1000f), 1, 65535);
        int r = Mathf.Clamp(Mathf.RoundToInt(radiusScale * 1000f), 1, 65535);
        return new[] { (byte)h, (byte)(h >> 8), (byte)r, (byte)(r >> 8), (byte)radial, (byte)rows };
    }

    /// <summary>Vazonun (satır, dilim) noktasındaki yarıçapı (metre).</summary>
    public float VaseRadiusAt(int row, int seg)
    {
        int stride = VaseRadial + 1;
        int i = row * stride + seg;
        return i >= 0 && i < shape.Length ? shape[i] / 255f * VaseRadiusScale : 0f;
    }

    public Color PixelColor(int x, int y)
    {
        int i = y * width + x;
        if (i < 0 || i >= pixels.Length || palette.Length == 0) return Color.white;
        return palette[Mathf.Min(pixels[i], palette.Length - 1)];
    }

    public Texture2D BuildTexture(FilterMode filter = FilterMode.Bilinear)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height) return null;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = filter, name = "ForgeryTexture" };
        var cols = new Color32[width * height];
        int last = Mathf.Max(0, palette.Length - 1);
        for (int i = 0; i < cols.Length; i++) cols[i] = palette.Length > 0 ? palette[Mathf.Min(pixels[i], last)] : new Color32(255, 255, 255, 255);
        tex.SetPixels32(cols);
        tex.Apply();
        return tex;
    }

    // ---------------- Palet ----------------

    public static Color32[] ToPalette(IList<Color> colors, params Color[] extra)
    {
        var list = new List<Color32>();
        if (colors != null) foreach (Color c in colors) list.Add(c);
        if (extra != null) foreach (Color c in extra) list.Add(c);
        if (list.Count > 255) list.RemoveRange(255, list.Count - 255);
        return list.ToArray();
    }

    public static byte Nearest(Color32[] palette, Color c)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            float dr = palette[i].r / 255f - c.r, dg = palette[i].g / 255f - c.g, db = palette[i].b / 255f - c.b;
            float d = dr * dr + dg * dg + db * db;
            if (d < bestD) { bestD = d; best = i; }
        }
        return (byte)best;
    }

    /// <summary>Renk dizisini palet indekslerine çevirir (step = kaç pikselde bir örnek alınacağı).</summary>
    public static byte[] Quantize(Color[] src, int srcW, int srcH, int step, Color32[] palette, out int w, out int h)
    {
        w = srcW / step;
        h = srcH / step;
        var res = new byte[w * h];
        Color lastColor = new Color(-1f, -1f, -1f);
        byte lastIndex = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = src[(y * step) * srcW + x * step];
                if (c.r != lastColor.r || c.g != lastColor.g || c.b != lastColor.b)
                {
                    lastColor = c;
                    lastIndex = Nearest(palette, c);
                }
                res[y * w + x] = lastIndex;
            }
        return res;
    }

    // ---------------- Kodlama ----------------

    public byte[] Encode()
    {
        using (var ms = new MemoryStream())
        {
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Version);
                bw.Write((byte)kind);
                bw.Write(targetKey);
                bw.Write(seed);
                bw.Write((byte)pars.Length);
                bw.Write(pars);
                bw.Write((byte)palette.Length);
                foreach (Color32 c in palette) { bw.Write(c.r); bw.Write(c.g); bw.Write(c.b); }
                bw.Write((ushort)width);
                bw.Write((ushort)height);
                WriteBlock(bw, pixels);
                WriteBlock(bw, shape);
            }
            return ms.ToArray();
        }
    }

    public static bool TryDecode(byte[] blob, out ForgeryData d)
    {
        d = null;
        if (blob == null || blob.Length < 12 || blob.Length > MaxBlobBytes) return false;
        try
        {
            using (var br = new BinaryReader(new MemoryStream(blob)))
            {
                if (br.ReadByte() != Version) return false;
                var res = new ForgeryData { kind = (ArtKind)br.ReadByte(), targetKey = br.ReadInt32(), seed = br.ReadInt32() };
                res.pars = br.ReadBytes(br.ReadByte());
                int pn = br.ReadByte();
                res.palette = new Color32[pn];
                for (int i = 0; i < pn; i++) res.palette[i] = new Color32(br.ReadByte(), br.ReadByte(), br.ReadByte(), 255);
                res.width = br.ReadUInt16();
                res.height = br.ReadUInt16();
                if (res.width > 512 || res.height > 512) return false;
                res.pixels = ReadBlock(br, res.width * res.height);
                res.shape = ReadBlock(br, 16384);
                if (res.pixels == null || res.shape == null) return false;
                if (res.pixels.Length != res.width * res.height) return false;
                if (res.pixels.Length > 0 && pn == 0) return false;
                d = res;
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void WriteBlock(BinaryWriter bw, byte[] raw)
    {
        raw = raw ?? Array.Empty<byte>();
        byte[] packed;
        using (var ms = new MemoryStream())
        {
            using (var ds = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
            packed = ms.ToArray();
        }
        bw.Write(raw.Length);
        bw.Write(packed.Length);
        bw.Write(packed);
    }

    private static byte[] ReadBlock(BinaryReader br, int maxRaw)
    {
        int rawLen = br.ReadInt32();
        int packedLen = br.ReadInt32();
        if (rawLen < 0 || rawLen > maxRaw || packedLen < 0 || packedLen > MaxBlobBytes) return null;
        byte[] packed = br.ReadBytes(packedLen);
        if (packed.Length != packedLen) return null;
        var raw = new byte[rawLen];
        using (var ds = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress))
        {
            int read = 0;
            while (read < rawLen)
            {
                int n = ds.Read(raw, read, rawLen - read);
                if (n <= 0) return null;
                read += n;
            }
        }
        return raw;
    }
}

/// <summary>
/// Yapılan sahtelerin kaydı: kimlik → veri. Sunucu hepsini tutar; istemciler ihtiyaç duyduklarını
/// (çantada, yerde ya da kaidede gördükleri kimlikleri) kendi oyuncuları üzerinden ister (PlayerInventory).
/// Görev değişince temizlenir (sahteler o görevin eserlerine göre yapılır).
/// </summary>
public static class ForgeryRegistry
{
    private static readonly Dictionary<ushort, byte[]> blobs = new Dictionary<ushort, byte[]>();
    private static readonly Dictionary<ushort, ForgeryData> decoded = new Dictionary<ushort, ForgeryData>();
    private static readonly Dictionary<ushort, Texture2D> icons = new Dictionary<ushort, Texture2D>();
    // İstenen ama henüz gelmeyen veriler: ne zaman istendi (cevap kaybolursa birkaç saniye sonra yeniden istenir)
    private static readonly Dictionary<ushort, float> requested = new Dictionary<ushort, float>();
    private const float RetryAfterSeconds = 3f;
    private static ushort nextId = 1;

    /// <summary>Bir sahtenin verisi bu bilgisayara ulaştı (görseller yenilensin).</summary>
    public static event Action<ushort> Arrived;

    public static void Clear()
    {
        blobs.Clear();
        decoded.Clear();
        requested.Clear();
        foreach (Texture2D t in icons.Values) if (t != null) UnityEngine.Object.Destroy(t);
        icons.Clear();
        nextId = 1;
    }

    /// <summary>Sunucu: yeni sahteyi kaydeder, kimliğini döner.</summary>
    public static ushort ServerAdd(byte[] blob, ForgeryData data)
    {
        ushort id = nextId++;
        if (nextId == 0) nextId = 1;
        blobs[id] = blob;
        decoded[id] = data;
        return id;
    }

    public static byte[] GetBlob(ushort id) => blobs.TryGetValue(id, out byte[] b) ? b : null;

    /// <summary>İstemci: sunucudan gelen veriyi kaydet.</summary>
    public static void Store(ushort id, byte[] blob)
    {
        if (id == 0) return;
        if (decoded.ContainsKey(id)) { requested.Remove(id); return; } // zaten var (sunucu herkese yollamış olabilir)
        if (!ForgeryData.TryDecode(blob, out ForgeryData d))
        {
            Debug.LogWarning($"[Forgery] Sahte #{id} verisi çözülemedi ({(blob != null ? blob.Length : 0)} bayt). Yeniden istenecek.");
            requested.Remove(id);
            return;
        }
        blobs[id] = blob;
        decoded[id] = d;
        requested.Remove(id);
        Arrived?.Invoke(id);
    }

    /// <summary>Veri bu bilgisayarda varsa döner; yoksa sunucudan ister (gelince Arrived tetiklenir).</summary>
    public static bool TryGet(ushort id, out ForgeryData data)
    {
        data = null;
        if (id == 0) return false;
        if (decoded.TryGetValue(id, out data)) return true;

        float now = Time.unscaledTime;
        if (requested.TryGetValue(id, out float askedAt) && now - askedAt < RetryAfterSeconds) return false;

        PlayerInventory local = PlayerInventory.Local;
        if (local != null && local.IsSpawned)
        {
            if (requested.ContainsKey(id)) Debug.Log($"[Forgery] Sahte #{id} verisi gelmedi, yeniden isteniyor.");
            requested[id] = now;
            local.RequestForgeryData(id);
        }
        // oyuncumuz henüz hazır değilse bir sonraki denemede istenir
        return false;
    }

    /// <summary>Çanta ikonu (tablo: resmin kendisi, vazo: silüet). Veri yoksa ya da tür ikon üretmiyorsa null.</summary>
    public static Texture2D Icon(ushort id)
    {
        if (id == 0) return null;
        if (icons.TryGetValue(id, out Texture2D t)) return t;
        if (!TryGet(id, out ForgeryData d)) return null;
        t = ArtworkLook.ForgeryIcon(d);
        icons[id] = t;
        return t;
    }
}
