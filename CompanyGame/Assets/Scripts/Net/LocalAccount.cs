using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The server account this PC (this profile slot) uses, remembered together with the server address.
/// The player never sees username or password: the title screen registers a generated pair behind the
/// player's name. One entry per server address, so changing the address does not lose the other account.
/// </summary>
public static class LocalAccount
{
    [Serializable]
    public sealed class Entry
    {
        public string url, username, password, nickname, gender;
        public bool HasName => !string.IsNullOrEmpty(nickname);
    }

    [Serializable] sealed class Store { public List<Entry> items = new List<Entry>(); }

    static string UrlKey => ProfileSlot.Key("net.server_url");
    static string StoreKey => ProfileSlot.Key("net.accounts");

    /// <summary>
    /// The server address in use (http(s)://host[:port], no trailing slash): the `-server` argument, else the address typed under
    /// Settings > 고급 for this slot, else <see cref="ServerConfig.PublicUrl"/>. Setting the public address (or an empty one) clears the override.
    /// </summary>
    public static string ServerUrl
    {
        get
        {
            if (ServerConfig.CommandLineUrl != null) return Normalize(ServerConfig.CommandLineUrl);
            return Normalize(PlayerPrefs.GetString(UrlKey, ""));
        }
        set
        {
            string url = Normalize(value);
            if (url == ServerConfig.PublicUrl) PlayerPrefs.DeleteKey(UrlKey);
            else PlayerPrefs.SetString(UrlKey, url);
            PlayerPrefs.Save();
        }
    }

    /// <summary>True when the address was typed in (or given with `-server`) instead of the built-in public server.</summary>
    public static bool IsCustomUrl => ServerUrl != ServerConfig.PublicUrl;

    /// <summary>Empty input means the built-in public server; an address without a scheme is taken as http.</summary>
    public static string Normalize(string url)
    {
        url = (url ?? "").Trim().TrimEnd('/');
        if (url.Length == 0) return ServerConfig.PublicUrl;
        return url.Contains("://") ? url : "http://" + url;
    }

    public static Entry Find(string url)
    {
        url = Normalize(url);
        foreach (var e in Load().items) if (e.url == url) return e;
        return null;
    }

    public static void Save(Entry entry)
    {
        var store = Load();
        store.items.RemoveAll(e => e.url == entry.url);
        store.items.Add(entry);
        Write(store);
    }

    public static void Remove(string url)
    {
        url = Normalize(url);
        var store = Load();
        if (store.items.RemoveAll(e => e.url == url) > 0) Write(store);
    }

    /// <summary>Forgets every account of this slot on this PC (the server keeps them).</summary>
    public static void Clear()
    {
        PlayerPrefs.DeleteKey(StoreKey);
        PlayerPrefs.Save();
    }

    // Server rule: a-z 0-9 _ , 3..20 chars.
    public static string NewUsername() => "u" + Guid.NewGuid().ToString("N").Substring(0, 12);

    // Server rule: 8..64 chars.
    public static string NewPassword() => Guid.NewGuid().ToString("N");

    static Store Load()
    {
        try { return JsonUtility.FromJson<Store>(PlayerPrefs.GetString(StoreKey, "")) ?? new Store(); }
        catch (Exception) { return new Store(); }
    }

    // ponytail: the generated password sits in PlayerPrefs as plain text. It protects a test server account
    // nobody knows the name of; encrypt (DPAPI/Keychain) if accounts ever hold anything valuable.
    static void Write(Store store)
    {
        PlayerPrefs.SetString(StoreKey, JsonUtility.ToJson(store));
        PlayerPrefs.Save();
    }
}
