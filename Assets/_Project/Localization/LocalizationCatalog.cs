using System;
using UnityEngine;

[CreateAssetMenu(menuName = "LD60/Localization Catalog")]
public sealed class LocalizationCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string key;
        [TextArea] public string ru;
        [TextArea] public string en;
    }

    public Entry[] entries = Array.Empty<Entry>();
}
