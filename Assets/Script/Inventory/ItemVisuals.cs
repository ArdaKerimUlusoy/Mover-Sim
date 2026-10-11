using UnityEngine;

/// <summary>Eşya prefab'ının sadece GÖRÜNTÜSÜNÜ (mesh + malzeme) kopyalar: fizik, çarpıştırıcı ve ağ bileşeni yok.
/// Elde tutma ve kaide üstündeki eser gösterimi için kullanılır.</summary>
public static class ItemVisuals
{
    public static GameObject Build(GameObject prefab, float maxSize, bool castShadows = false)
    {
        GameObject root = new GameObject("Visual_" + prefab.name);
        Matrix4x4 toLocal = prefab.transform.worldToLocalMatrix;

        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mf.sharedMesh == null) continue;

            GameObject part = new GameObject(mf.name);
            part.transform.SetParent(root.transform, false);

            if (mf.transform == prefab.transform)
            {
                part.transform.localScale = prefab.transform.localScale;
            }
            else
            {
                Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
                part.transform.localScale = Vector3.Scale(prefab.transform.localScale, mf.transform.lossyScale);
            }

            part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            MeshRenderer r = part.AddComponent<MeshRenderer>();
            r.sharedMaterials = mr.sharedMaterials;
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Boyutu ayarla: çok büyükse küçült, çok küçükse biraz büyüt (yüzük gibi)
        if (TryGetBounds(root, out Bounds b))
        {
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (size > maxSize) root.transform.localScale = Vector3.one * (maxSize / size);
            else if (size < maxSize * 0.4f && size > 0.0001f) root.transform.localScale = Vector3.one * (maxSize * 0.4f / size);
        }

        return root;
    }

    public static bool TryGetBounds(GameObject go, out Bounds b)
    {
        b = new Bounds();
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return any;
    }

    /// <summary>
    /// Eşyanın GERÇEK görünümü: atölyede yapılan sahte (oyuncunun kendi eseri) ya da müzedeki orijinalin kendisi.
    /// Böyle bir görünüm yoksa (alet, bozuk para, verisi henüz gelmemiş sahte) eşyanın prefab görüntüsü döner.
    /// pending = sahtenin verisi sunucudan bekleniyor; ForgeryRegistry.Arrived gelince yeniden kurulmalı.
    /// Dönen objenin pivotu görselin ORTASIDIR.
    /// </summary>
    public static GameObject BuildFor(ItemDefinition def, InventorySlot slot, out bool pending)
    {
        pending = false;
        if (def == null) return null;
        GameObject art = null;

        if (def.type == ItemType.Forgery && slot.ForgeryId != 0)
        {
            if (ForgeryRegistry.TryGet(slot.ForgeryId, out ForgeryData data)) art = ArtworkLook.BuildForgeryVisual(data);
            else pending = true;
        }
        else if (def.type == ItemType.Original && slot.LookKey != 0)
        {
            ArtworkDisplay d = ArtworkDisplay.FindByKey(slot.LookKey);
            MuseumSession ms = MuseumSession.Instance;
            if (d != null && ms != null && ms.Ready) art = ArtworkLook.BuildOriginalVisual(ArtworkLook.KindOf(def), ms.Seed, ms.ThemeIndex, d.name);
        }

        if (art == null)
        {
            if (def.worldPrefab == null) return null;
            art = Build(def.worldPrefab, 1000f);
            art.transform.localScale = Vector3.one;
        }
        return Centered(art);
    }

    /// <summary>Görseli, pivotu sınırlarının ortası olan boş bir kökün altına alır.</summary>
    /// <summary>BuildFor gerçek bir eser görünümü bulamayıp prefab görüntüsüne mi düştü?</summary>
    public static bool IsPrefabFallback(GameObject built) => built != null && built.name.StartsWith("Held_Visual_");

    public static GameObject Centered(GameObject visual)
    {
        GameObject root = new GameObject("Held_" + visual.name);
        visual.transform.SetParent(root.transform, false);
        if (TryGetBounds(visual, out Bounds b)) visual.transform.localPosition = -b.center;
        return root;
    }

    /// <summary>En büyük kenarı maxSize'ı geçiyorsa küçültür. Kullanılan ölçeği döner.</summary>
    public static float Fit(GameObject root, float maxSize)
    {
        if (!TryGetBounds(root, out Bounds b)) return 1f;
        float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (size <= maxSize || size < 0.0001f) return 1f;
        float k = maxSize / size;
        root.transform.localScale *= k;
        return k;
    }

    public static void SetShadows(GameObject root, bool cast)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
