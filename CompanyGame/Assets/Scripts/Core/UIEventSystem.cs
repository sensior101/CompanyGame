using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>Shared EventSystem helpers for runtime UI.</summary>
public static class UIEventSystem
{
    /// <summary>True while a text field has keyboard focus, so game keys must be ignored.</summary>
    public static bool IsEditingText()
    {
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        return selected && (selected.GetComponentInParent<TMPro.TMP_InputField>() || selected.GetComponentInParent<UnityEngine.UI.InputField>());
    }

    /// <summary>Maps without an EventSystem still need one for runtime UI clicks.</summary>
    public static void Ensure(Transform owner)
    {
        var existing = Object.FindAnyObjectByType<EventSystem>();
        if (existing && existing.GetComponent<BaseInputModule>()) return;
        var host = existing ? existing.gameObject : new GameObject("UIEventSystem", typeof(EventSystem));
        if (!existing) host.transform.SetParent(owner, false);
        host.AddComponent<InputSystemUIInputModule>();
    }
}
