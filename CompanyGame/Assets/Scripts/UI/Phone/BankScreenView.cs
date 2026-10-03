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

    private void Refresh()
    {
        cashText.text = "보유 현금   " + PhoneFormat.Won(app.GetCash());
        bankText.text = "통장 잔액   " + PhoneFormat.Won(app.GetBankBalance());
    }

    private void OnDeposit()
    {
        if (!PhoneFormat.TryParseAmount(amountInput.text, out long amount))
        {
            messageText.text = "금액을 입력하세요.";
            return;
        }

        messageText.text = app.Deposit(amount) ? "입금했습니다." : "입금할 수 없습니다. (현금 부족)";
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

        messageText.text = app.Withdraw(amount) ? "출금했습니다." : "출금할 수 없습니다. (잔액 부족)";
        amountInput.text = string.Empty;
        Refresh();
    }
}
