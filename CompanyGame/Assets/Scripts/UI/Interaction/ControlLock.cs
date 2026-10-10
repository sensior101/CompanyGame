using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Pauses player movement, camera input and chat while a menu owns the keyboard and mouse,
/// then restores exactly what was running before.
/// </summary>
public sealed class ControlLock
{
    readonly List<Behaviour> suspended = new List<Behaviour>();
    readonly List<PlayerCameraController> cameras = new List<PlayerCameraController>();
    bool held;
    bool cursorWasVisible;
    CursorLockMode previousCursorLock;
    GameObject previousSelection;

    public bool MovementWasEnabled { get; private set; }

    public void Hold(PlayerMovement movement)
    {
        if (held || !movement) return;
        MovementWasEnabled = movement.enabled;
        cursorWasVisible = Cursor.visible;
        previousCursorLock = Cursor.lockState;
        previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        held = true;
        suspended.Clear();
        // Closed chat controllers also listen for Enter/Escape. Pause them while the menu owns the keyboard.
        foreach (var chat in Object.FindObjectsByType<ChatUIManager>()) Suspend(chat);
        foreach (var cameraController in Object.FindObjectsByType<PlayerCameraController>())
            if (cameraController.isActiveAndEnabled && cameraController.target == movement.transform)
            {
                cameraController.HoldMenuInput();
                cameras.Add(cameraController);
            }
        // Chat.OnDisable can restore the movement state it captured, so lock movement after pausing chat.
        movement.enabled = false;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Release(PlayerMovement movement, bool restoreMovement)
    {
        if (!held) return;
        held = false;
        foreach (var cameraController in cameras) if (cameraController) cameraController.ReleaseMenuInput();
        cameras.Clear();
        foreach (var component in suspended) if (component) component.enabled = true;
        suspended.Clear();
        if (restoreMovement && movement) movement.enabled = MovementWasEnabled;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = cursorWasVisible;
        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
        previousSelection = null;
    }

    void Suspend(Behaviour component)
    {
        if (!component.isActiveAndEnabled) return;
        suspended.Add(component);
        component.enabled = false;
    }
}
