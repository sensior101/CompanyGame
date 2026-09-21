using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns the phone UI: which app is open and switching between them.</summary>
public class PhoneManager : MonoBehaviour
{
    public static PhoneManager Instance { get; private set; }

    [SerializeField]
    private GameObject phoneRoot;

    [SerializeField]
    private PhoneAppBase[] apps;

    private readonly Dictionary<string, PhoneAppBase> appsById = new Dictionary<string, PhoneAppBase>();
    private PhoneAppBase currentApp;

    public bool IsPhoneOpen => phoneRoot != null && phoneRoot.activeSelf;

    public PhoneAppBase CurrentApp => currentApp;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        foreach (var app in apps)
        {
            if (app == null) continue;
            appsById[app.AppId] = app;
            app.Close();
        }

        if (phoneRoot != null) phoneRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void TogglePhone()
    {
        if (IsPhoneOpen) ClosePhone();
        else OpenPhone();
    }

    public void OpenPhone()
    {
        if (phoneRoot != null) phoneRoot.SetActive(true);
    }

    public void ClosePhone()
    {
        currentApp?.Close();
        currentApp = null;
        if (phoneRoot != null) phoneRoot.SetActive(false);
    }

    public void OpenApp(string appId)
    {
        if (!appsById.TryGetValue(appId, out var app))
        {
            Debug.LogWarning($"PhoneManager: unknown app '{appId}'");
            return;
        }

        OpenPhone();

        if (currentApp == app) return;

        currentApp?.Close();
        currentApp = app;
        app.Open();
    }
}
