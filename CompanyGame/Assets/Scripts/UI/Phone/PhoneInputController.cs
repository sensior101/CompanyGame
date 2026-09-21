using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Opens/closes the phone with a key and stops player movement while it is open (same approach as ChatUIManager).</summary>
public class PhoneInputController : MonoBehaviour
{
    [SerializeField]
    private PhoneManager phone;

    [SerializeField]
    private Key toggleKey = Key.R;

    private PlayerMovement playerMovement;
    private bool movementWasEnabled;

    private void Awake()
    {
        if (phone == null) phone = GetComponent<PhoneManager>();
    }

    private void OnEnable()
    {
        if (phone != null) phone.OpenStateChanged += OnOpenStateChanged;
    }

    private void OnDisable()
    {
        if (phone != null) phone.OpenStateChanged -= OnOpenStateChanged;
        RestoreMovement();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || phone == null) return;
        if (ChatUIManager.IsChatting) return;

        if (IsTypingInField())
        {
            if (keyboard.escapeKey.wasPressedThisFrame && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
            return;
        }

        if (keyboard[toggleKey].wasPressedThisFrame)
        {
            phone.TogglePhone();
        }
        else if (phone.IsPhoneOpen && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (phone.CurrentApp != null) phone.GoHome();
            else phone.ClosePhone();
        }
    }

    private static bool IsTypingInField()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null) return false;

        var field = selected.GetComponent<TMP_InputField>();
        return field != null && field.isFocused;
    }

    private void OnOpenStateChanged(bool isOpen)
    {
        if (isOpen) LockMovement();
        else RestoreMovement();
    }

    private void LockMovement()
    {
        if (playerMovement == null) playerMovement = FindAnyObjectByType<PlayerMovement>();
        if (playerMovement == null) return;

        movementWasEnabled = playerMovement.enabled;
        playerMovement.enabled = false;
    }

    private void RestoreMovement()
    {
        if (playerMovement != null && movementWasEnabled) playerMovement.enabled = true;
        movementWasEnabled = false;
    }
}
