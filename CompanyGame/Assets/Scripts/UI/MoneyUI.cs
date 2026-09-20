using UnityEngine;
using TMPro;

public class MoneyUI : MonoBehaviour
{
    [SerializeField]
    private PropertyManager propertyManager;

    [SerializeField]
    private TMP_Text moneyText;

    private void Start()
    {
        if (propertyManager == null)
        {
            propertyManager = PropertyManager.Instance;
        }

        if (propertyManager == null || moneyText == null)
        {
            Debug.LogError("MoneyUI 연결 실패: PropertyManager 또는 MoneyText를 확인하세요.");
            return;
        }

        // 게임 시작 시 현재 잔액 표시
        UpdateMoneyUI(propertyManager.Money);

        // 돈이 변경될 때마다 UI 갱신
        propertyManager.MoneyChanged += OnMoneyChanged;
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
        if (propertyManager != null)
        {
            propertyManager.MoneyChanged -= OnMoneyChanged;
        }
    }
}