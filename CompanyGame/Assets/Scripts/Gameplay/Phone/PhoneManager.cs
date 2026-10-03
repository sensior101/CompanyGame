using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns the phone UI: which app is open and switching between them.</summary>
public class PhoneManager : MonoBehaviour
{
    public static PhoneManager Instance { get; private set; }

    [SerializeField]
    private GameObject phoneRoot;

    [SerializeField]
    private GameObject homeScreen;

    [SerializeField]
    private PhoneAppBase[] apps;

    private readonly Dictionary<string, PhoneAppBase> appsById = new Dictionary<string, PhoneAppBase>();
    private PhoneAppBase currentApp;

    public event Action<bool> OpenStateChanged;

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

        if (homeScreen != null) homeScreen.SetActive(false);
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
        bool wasOpen = IsPhoneOpen;
        if (phoneRoot != null) phoneRoot.SetActive(true);
        RefreshHome();
        if (!wasOpen && IsPhoneOpen) OpenStateChanged?.Invoke(true);
    }

    public void ClosePhone()
    {
        bool wasOpen = IsPhoneOpen;
        currentApp?.Close();
        currentApp = null;
        if (phoneRoot != null) phoneRoot.SetActive(false);
        RefreshHome();
        if (wasOpen) OpenStateChanged?.Invoke(false);
    }

    public void GoHome()
    {
        currentApp?.Close();
        currentApp = null;
        RefreshHome();
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
        RefreshHome();
    }

    private void RefreshHome()
    {
        if (homeScreen != null) homeScreen.SetActive(IsPhoneOpen && currentApp == null);
    }
}
