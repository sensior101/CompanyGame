using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>Maps without an EventSystem still need one for runtime UI clicks.</summary>
public static class UIEventSystem
{
    public static void Ensure(Transform owner)
    {
        var existing = Object.FindFirstObjectByType<EventSystem>();
        if (existing && existing.GetComponent<BaseInputModule>()) return;
        var host = existing ? existing.gameObject : new GameObject("UIEventSystem", typeof(EventSystem));
        if (!existing) host.transform.SetParent(owner, false);
        host.AddComponent<InputSystemUIInputModule>();
    }
}
