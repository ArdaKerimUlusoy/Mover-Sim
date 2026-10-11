using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ses çalma merkezi. Sahneye bir şey eklemeye gerek yok, oyun başlarken kendini kurar.
///   GameAudio.PlayAt(SoundId.Pickup, pozisyon);   // 3D (uzaklaştıkça kısılır)
///   GameAudio.Play2D(SoundId.UiClick);             // arayüz sesi
/// Resources/SoundBank varsa oradaki gerçek sesler, yoksa SoundSynth'in ürettiği geçici sesler kullanılır.
/// </summary>
public static class GameAudio
{
    private const int PoolSize = 24;

    private static readonly Dictionary<SoundId, AudioClip[]> clips = new Dictionary<SoundId, AudioClip[]>();
    private static readonly Dictionary<SoundId, float> volumes = new Dictionary<SoundId, float>();
    private static AudioSource[] pool;
    private static int next;
    private static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        initialized = false;
        clips.Clear();
        volumes.Clear();
        pool = null;
    }

    private static void EnsureInitialized()
    {
        if (initialized && pool != null && pool[0] != null) return;
        initialized = true;

        // 1) Geçici (üretilmiş) sesler
        foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
        {
            clips[id] = SoundSynth.Create(id);
            volumes[id] = 1f;
        }

        // 2) Gerçek ses dosyaları varsa üstüne yaz
        SoundBank bank = Resources.Load<SoundBank>(SoundBank.ResourcePath);
        if (bank != null)
        {
            foreach (SoundBank.Entry e in bank.entries)
            {
                if (e == null || e.clips == null || e.clips.Length == 0) continue;
                List<AudioClip> valid = new List<AudioClip>();
                foreach (AudioClip c in e.clips) if (c != null) valid.Add(c);
                if (valid.Count == 0) continue;
                clips[e.id] = valid.ToArray();
                volumes[e.id] = e.volume;
            }
        }

        // 3) Ses kaynağı havuzu
        GameObject root = new GameObject("[GameAudio]");
        UnityEngine.Object.DontDestroyOnLoad(root);
        pool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            GameObject go = new GameObject("Source" + i);
            go.transform.SetParent(root.transform, false);
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1.5f;
            src.maxDistance = 25f;
            src.dopplerLevel = 0f;
            pool[i] = src;
        }
    }

    public static void PlayAt(SoundId id, Vector3 position, float volume = 1f, float pitchVariation = 0.08f)
    {
        Play(id, position, true, volume, pitchVariation);
    }

    public static void Play2D(SoundId id, float volume = 1f, float pitchVariation = 0.03f)
    {
        Play(id, Vector3.zero, false, volume, pitchVariation);
    }

    private static void Play(SoundId id, Vector3 position, bool spatial, float volume, float pitchVariation)
    {
        EnsureInitialized();
        if (!clips.TryGetValue(id, out AudioClip[] list) || list == null || list.Length == 0) return;

        AudioSource src = GetFreeSource();
        src.transform.position = position;
        src.spatialBlend = spatial ? 1f : 0f;
        src.clip = list[UnityEngine.Random.Range(0, list.Length)];
        src.volume = Mathf.Clamp01(volume * volumes[id]);
        src.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
        src.Play();
    }

    private static AudioSource GetFreeSource()
    {
        for (int i = 0; i < pool.Length; i++)
        {
            int idx = (next + i) % pool.Length;
            if (!pool[idx].isPlaying)
            {
                next = (idx + 1) % pool.Length;
                return pool[idx];
            }
        }
        // Hepsi doluysa en eskisini kullan
        AudioSource src = pool[next];
        next = (next + 1) % pool.Length;
        return src;
    }
}
