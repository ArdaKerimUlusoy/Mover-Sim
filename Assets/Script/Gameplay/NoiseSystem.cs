using System;
using UnityEngine;

/// <summary>
/// Oyundaki "gürültü" olayları (para düşmesi, eşya çarpması...). SADECE SUNUCUDA tetiklenir.
/// Güvenlik yapay zekası yazıldığında:  NoiseSystem.NoiseMade += (pos, radius) => { ... };
/// ile dinleyip yarıçap içindeyse sesin geldiği yere yönelecek.
/// </summary>
public static class NoiseSystem
{
    /// <summary>pozisyon, duyulma yarıçapı (metre)</summary>
    public static event Action<Vector3, float> NoiseMade;

    /// <summary>Sahne penceresinde gürültüleri çember olarak çiz (test için).</summary>
    public static bool DrawDebug = true;

    public static void Emit(Vector3 position, float radius)
    {
        NoiseMade?.Invoke(position, radius);

        if (DrawDebug) DrawCircle(position, radius, new Color(1f, 0.6f, 0.1f), 1.5f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        NoiseMade = null;
    }

    private static void DrawCircle(Vector3 center, float radius, Color color, float duration)
    {
        const int segments = 32;
        Vector3 prev = center + new Vector3(radius, 0.05f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            Vector3 p = center + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius);
            Debug.DrawLine(prev, p, color, duration);
            prev = p;
        }
    }
}
