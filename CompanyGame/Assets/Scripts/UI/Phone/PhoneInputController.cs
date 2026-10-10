using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Opens/closes the phone with a key and stops player movement while it is open (same approach as ChatUIManager).</summary>
public class PhoneInputController : MonoBehaviour
{
    [SerializeField]
    private PhoneManager phone;

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
        if (phone == null) return;
        if (ChatUIManager.IsChatting || DialogueManager.OwnsInput) return;

        if (IsTypingInField())
        {
            if (GameInput.CancelPressed && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
            return;
        }

        if (GameInput.PhonePressed)
        {
            phone.TogglePhone();
        }
        else if (phone.IsPhoneOpen && GameInput.CancelPressed)
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
        if (playerMovement == null) playerMovement = PlayerSpawner.Player;
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
