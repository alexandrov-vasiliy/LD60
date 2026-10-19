using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class Localization
{
    private const string PreferenceKey = "LD60.Language";
    private static Dictionary<string, LocalizationCatalog.Entry> entries;
    private static readonly HashSet<string> reported = new HashSet<string>();
    private static string language;
    public static event Action LanguageChanged;
    public static string Language => language ??= PlayerPrefs.GetString(PreferenceKey, "en") == "ru" ? "ru" : "en";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        entries = null;
        language = null;
        reported.Clear();
        LanguageChanged = null;
    }

    public static void SetLanguage(string value)
    {
        if (value != "ru" && value != "en") throw new ArgumentOutOfRangeException(nameof(value));
        if (Language == value) return;
        language = value;
        PlayerPrefs.SetString(PreferenceKey, value);
        PlayerPrefs.Save();
        LanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (entries == null)
        {
            var catalog = Resources.Load<LocalizationCatalog>("LocalizationCatalog");
            if (catalog == null) throw new InvalidOperationException("Missing Resources/LocalizationCatalog.");
            var loaded = new Dictionary<string, LocalizationCatalog.Entry>(StringComparer.Ordinal);
            foreach (var entry in catalog.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.key) || string.IsNullOrEmpty(entry.ru))
                    throw new InvalidOperationException("Localization catalog contains an incomplete entry.");
                if (!loaded.TryAdd(entry.key, entry))
                    throw new InvalidOperationException($"Duplicate localization key '{entry.key}'.");
            }
            entries = loaded;
        }
        if (!entries.TryGetValue(key, out var found))
        {
            if (reported.Add(key)) Debug.LogError($"Missing localization key '{key}'.");
            return $"[{key}]";
        }
        if (Language == "ru") return found.ru;
        if (!string.IsNullOrEmpty(found.en)) return found.en;
        if (reported.Add(key)) Debug.LogWarning($"Missing English translation for '{key}'; using Russian.");
        return found.ru;
    }

    public static string Format(string key, params object[] arguments) =>
        string.Format(Language == "ru" ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture, Get(key), arguments);
}
