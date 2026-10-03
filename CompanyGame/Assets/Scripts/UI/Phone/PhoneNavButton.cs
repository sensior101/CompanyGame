using UnityEngine;
using UnityEngine.UI;

/// <summary>Wires a button to a PhoneManager action so the scene needs no persistent UnityEvent setup.</summary>
[RequireComponent(typeof(Button))]
public class PhoneNavButton : MonoBehaviour
{
    public enum NavAction
    {
        OpenApp,
        Home,
        Close
    }

    [SerializeField]
    private NavAction action;

    [SerializeField, Tooltip("PhoneAppBase.AppId. Defaults to the app's class name.")]
    private string appId;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        var phone = PhoneManager.Instance;
        if (phone == null) return;

        switch (action)
        {
            case NavAction.OpenApp:
                phone.OpenApp(appId);
                break;
            case NavAction.Home:
                phone.GoHome();
                break;
            default:
                phone.ClosePhone();
                break;
        }
    }
}
