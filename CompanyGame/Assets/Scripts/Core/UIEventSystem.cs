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

    /// <summary>Creates the one game-wide EventSystem; maps must not contain their own.</summary>
    public static void Ensure()
    {
        if (Object.FindAnyObjectByType<EventSystem>()) return;
        var host = new GameObject("UIEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        Object.DontDestroyOnLoad(host);
    }
}
