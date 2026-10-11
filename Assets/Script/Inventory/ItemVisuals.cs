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
}
