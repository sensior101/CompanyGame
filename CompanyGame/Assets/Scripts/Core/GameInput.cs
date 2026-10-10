using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Every key and mouse binding in one place (기획안 0. 플레이어 조작).
/// Scripts ask for a game action, never for a raw key, so a rebinding touches only this file.
/// </summary>
public static class GameInput
{
    // 기획 조작: WASD 이동, Shift 달리기, Ctrl 앉기, Space 상호작용, Q 퀘스트, E 인벤토리, R 핸드폰, Tab 1인칭/3인칭
    public static Vector2 Move
    {
        get
        {
            var keys = Keyboard.current;
            if (keys == null) return Vector2.zero;
            return new Vector2(
                (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
        }
    }

    public static bool SprintHeld => Held(Key.LeftShift);
    public static bool CrouchHeld => Held(Key.LeftCtrl);
    public static bool InteractPressed => Pressed(Key.Space);
    public static bool InteractHeld => Held(Key.Space);
    public static bool QuestPressed => Pressed(Key.Q);
    public static bool InventoryPressed => Pressed(Key.E);
    public static bool PhonePressed => Pressed(Key.R);
    public static bool ViewTogglePressed => Pressed(Key.Tab);
    public static bool PickupPressed => Pressed(Key.F);
    public static bool ResetPositionPressed => Pressed(Key.Home);

    // UI
    public static bool CancelPressed => Pressed(Key.Escape);
    public static bool SubmitPressed => Pressed(Key.Enter) || Pressed(Key.NumpadEnter);
    public static bool NextFieldPressed => Pressed(Key.Tab);
    public static bool NavLeftPressed => Pressed(Key.LeftArrow);
    public static bool NavRightPressed => Pressed(Key.RightArrow);
    public static bool NavUpPressed => Pressed(Key.UpArrow);
    public static bool NavDownPressed => Pressed(Key.DownArrow);
    public static bool NavLeftPressed => Pressed(Key.LeftArrow);
    public static bool NavRightPressed => Pressed(Key.RightArrow);
    public static bool AltHeld => Held(Key.LeftAlt) || Held(Key.RightAlt);

    /// <summary>Index of the number key (1..count) pressed this frame, or -1. Alt+number is reserved for debug shortcuts.</summary>
    public static int NumberPressed(int count)
    {
        var keys = Keyboard.current;
        if (keys == null || AltHeld) return -1;
        return DigitPressed(keys, count);
    }

    /// <summary>Alt+number (1..count) pressed this frame, or -1.</summary>
    public static int AltNumberPressed(int count)
    {
        var keys = Keyboard.current;
        if (keys == null || !AltHeld) return -1;
        return DigitPressed(keys, count);
    }

    // Mouse
    public static bool AttackPressed => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    public static bool DismantleVehiclePressed => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
    public static bool OrbitHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;
    public static Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    public static Vector2 PointerDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
    public static bool HasPointer => Mouse.current != null;

    /// <summary>Scroll in wheel notches (one notch = 1).</summary>
    public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;

    public static bool ButtonReleased(bool right)
    {
        var mouse = Mouse.current;
        return mouse != null && (right ? mouse.rightButton : mouse.leftButton).wasReleasedThisFrame;
    }

    static bool Pressed(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
    static bool Held(Key key) => Keyboard.current != null && Keyboard.current[key].isPressed;

    static int DigitPressed(Keyboard keys, int count)
    {
        for (int i = 0; i < count && i < 9; i++)
            if (keys[Key.Digit1 + i].wasPressedThisFrame) return i;
        return -1;
    }
}
