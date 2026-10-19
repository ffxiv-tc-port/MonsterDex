namespace DeepDungeonDex.Storage;

public class Locale : ILoad<Locale>
{
    public Dictionary<string, string> TranslationDictionary { get; set; } = new();

    public Locale Load(string path)
    {
        TranslationDictionary = StorageHandler.Deserializer.Deserialize<Dictionary<string, string>>(path);
        return this;
    }

    public void Dispose()
    {
        TranslationDictionary.Clear();
    }
    object ILoad.Load(string str) => Load(str);
}

public class LocaleKeys : ILoad<LocaleKeys>
{
    public Dictionary<string, string> LocaleDictionary { get; set; } = new();
        
    public LocaleKeys Load(string path)
    {
        LocaleDictionary = StorageHandler.Deserializer.Deserialize<Dictionary<string, string>>(path);
        return this;
    }

    public void Dispose()
    {
        LocaleDictionary.Clear();
    }

    object ILoad.Load(string str) => Load(str);
}

public static class LocaleExtensions
{
    public static string GetLocale(this Locale locale, string key)
    {
        return locale.TranslationDictionary.TryGetValue(key, out var value) ? value : key;
    }

    // 每幀會被呼叫多次：不用 LINQ／閉包。語意＝第一份有相符鍵的 Locale 裡、列舉順序第一個相符鍵的值。
    public static string GetLocale(this IEnumerable<Locale> locales, string key)
    {
        var trimmedKey = key.Trim();
        if (locales is Locale[] array)
        {
            foreach (var locale in array)
            {
                if (TryFind(locale, trimmedKey, out var found))
                    return found;
            }
            return key;
        }

        foreach (var locale in locales)
        {
            if (TryFind(locale, trimmedKey, out var found))
                return found;
        }
        return key;
    }

    private static bool TryFind(Locale locale, string trimmedKey, out string value)
    {
        foreach (var (k, v) in locale.TranslationDictionary)
        {
            if (string.Equals(k.Trim(), trimmedKey, StringComparison.InvariantCultureIgnoreCase))
            {
                value = v;
                return true;
            }
        }
        value = null!;
        return false;
    }

    public static IEnumerable<KeyValuePair<string, string>> GetLocaleList(this IEnumerable<Locale> locales, string key)
    {
        foreach (var locale in locales)
        {
            foreach (var (s, value) in locale.TranslationDictionary)
            {
                if(s == key)
                    yield return new KeyValuePair<string, string>(s, value);
            }
        }
    }
}