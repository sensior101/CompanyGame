using UnityEngine;
using TMPro;

public class MoneyUI : MonoBehaviour
{
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("bank")]
    private BankManager bank;

    [SerializeField]
    private TMP_Text moneyText;

    private void Start()
    {
        if (bank == null)
        {
            bank = BankManager.Instance;
        }

        if (bank == null || moneyText == null)
        {
            Debug.LogError("MoneyUI 연결 실패: BankManager 또는 MoneyText를 확인하세요.");
            return;
        }

        // 게임 시작 시 현재 잔액 표시
        UpdateMoneyUI(bank.Money);

        // 돈이 변경될 때마다 UI 갱신
        bank.MoneyChanged += OnMoneyChanged;
    }

    private void OnMoneyChanged(
        long newBalance,
        long delta,
        MoneyChangeReason reason)
    {
        UpdateMoneyUI(newBalance);
    }

    private void UpdateMoneyUI(long amount)
    {
        moneyText.text = $"{amount:N0}";
    }

    private void OnDestroy()
    {
        if (bank != null)
        {
            bank.MoneyChanged -= OnMoneyChanged;
        }
    }
}