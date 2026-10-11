using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Oyundaki tüm eşyaların listesi. Ağ üzerinden eşyanın kendisi değil bu listedeki SIRA NUMARASI gönderilir.
/// Dosya mutlaka Assets/Resources/ItemDatabase.asset olmalı.
/// Elle düzenlemeyin: Tools → Sahte Şaheser → Rebuild Item Database (id'ye göre sıralar).
/// İki oyuncunun listesi aynı olmalı (git ile aynı sürümü çekin).
/// </summary>
[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Sahte Şaheser/Item Database")]
public class ItemDatabase : ScriptableObject
{
    public const string ResourcePath = "ItemDatabase";

    [SerializeField] private List<ItemDefinition> items = new List<ItemDefinition>();

    private static ItemDatabase instance;
    private static bool warned;

    public static ItemDatabase Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<ItemDatabase>(ResourcePath);
                if (instance == null && !warned)
                {
                    warned = true;
                    Debug.LogError("[Inventory] Assets/Resources/ItemDatabase.asset bulunamadı. " +
                                   "Tools → Sahte Şaheser → Rebuild Item Database çalıştırın.");
                }
            }
            return instance;
        }
    }

    public int Count => items.Count;

    public ItemDefinition Get(int index)
    {
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    public int IndexOf(ItemDefinition def)
    {
        return def == null ? -1 : items.IndexOf(def);
    }

#if UNITY_EDITOR
    public void EditorSetItems(List<ItemDefinition> list)
    {
        items = list;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
