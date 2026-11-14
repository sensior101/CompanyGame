using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BankScreenView : MonoBehaviour
{
    [SerializeField] private BankApplication app;
    [SerializeField] private TMP_Text cashText;
    [SerializeField] private TMP_Text bankText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_InputField amountInput;
    [SerializeField] private Button depositButton;
    [SerializeField] private Button withdrawButton;

    private void Awake()
    {
        if (app == null) app = GetComponent<BankApplication>();
        app.Refreshed += Refresh;
        depositButton.onClick.AddListener(OnDeposit);
        withdrawButton.onClick.AddListener(OnWithdraw);
    }

    private void OnDestroy()
    {
        if (app != null) app.Refreshed -= Refresh;
    }

    long shownCash = -1, shownBank = -1;

    // Purchases, inventory withdrawals and server answers change the money while the app is open.
    private void Update()
    {
        if (app.IsOpen && (app.GetCash() != shownCash || app.GetBankBalance() != shownBank)) Refresh();
    }

    private void Refresh()
    {
        shownCash = app.GetCash(); shownBank = app.GetBankBalance();
        cashText.text = "현금   " + PhoneFormat.Won(shownCash);
        bankText.text = "계좌   " + PhoneFormat.Won(shownBank);
    }

    private void OnDeposit()
    {
        if (!PhoneFormat.TryParseAmount(amountInput.text, out long amount))
        {
            messageText.text = "금액을 입력하세요.";
            return;
        }

        messageText.text = app.Deposit(amount, out string error) ? "입금했습니다." : "입금할 수 없습니다. " + error;
        amountInput.text = string.Empty;
        Refresh();
    }

    private void OnWithdraw()
    {
        if (!PhoneFormat.TryParseAmount(amountInput.text, out long amount))
        {
            messageText.text = "금액을 입력하세요.";
            return;
        }

        messageText.text = app.Withdraw(amount, out string error) ? "출금했습니다." : "출금할 수 없습니다. " + error;
        amountInput.text = string.Empty;
        Refresh();
    }
}
