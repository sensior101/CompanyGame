using UnityEngine;

/// <summary>Base for every phone screen. PhoneManager opens/closes these by AppId.</summary>
public abstract class PhoneAppBase : MonoBehaviour
{
    [SerializeField]
    private string appId;

    [SerializeField]
    private GameObject screenRoot;

    public string AppId => string.IsNullOrEmpty(appId) ? GetType().Name : appId;

    public bool IsOpen => screenRoot == null || screenRoot.activeSelf;

    public void Open()
    {
        if (screenRoot != null) screenRoot.SetActive(true);
        OnOpened();
    }

    public void Close()
    {
        if (screenRoot != null) screenRoot.SetActive(false);
        OnClosed();
    }

    /// <summary>Called after the screen becomes visible. Refresh data here.</summary>
    protected virtual void OnOpened() { }

    /// <summary>Called after the screen is hidden. Release/save state here.</summary>
    protected virtual void OnClosed() { }
}
