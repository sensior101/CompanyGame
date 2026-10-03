using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopScreenView : MonoBehaviour
{
    [SerializeField] private ShopApplication app;
    [SerializeField] private TMP_Text cashText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Transform rowContainer;
    [SerializeField] private GameObject rowTemplate;

    private class Row
    {
        public GameObject root;
        public TMP_Text name;
        public TMP_Text price;
        public Button buy;
    }

    private readonly List<Row> rows = new List<Row>();

    private void Awake()
    {
        if (app == null) app = GetComponent<ShopApplication>();
        app.Refreshed += Refresh;
        rowTemplate.SetActive(false);
    }

    private void OnDestroy()
    {
        if (app != null) app.Refreshed -= Refresh;
    }

    private void Refresh()
    {
        cashText.text = "보유 현금   " + PhoneFormat.Won(app.GetCash());

        var items = app.Items;
        while (rows.Count < items.Count)
        {
            var go = Instantiate(rowTemplate, rowContainer);
            go.SetActive(true);
            rows.Add(new Row
            {
                root = go,
                name = go.transform.Find("Info/Name").GetComponent<TMP_Text>(),
                price = go.transform.Find("Info/Price").GetComponent<TMP_Text>(),
                buy = go.transform.Find("BuyButton").GetComponent<Button>()
            });
        }

        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].root.SetActive(i < items.Count);
            if (i >= items.Count) continue;

            var item = items[i];
            rows[i].name.text = item.displayName;
            rows[i].price.text = PhoneFormat.Won(item.price);
            rows[i].buy.onClick.RemoveAllListeners();
            rows[i].buy.onClick.AddListener(() => Buy(item));
        }
    }

    private void Buy(ShopItem item)
    {
        messageText.text = app.Purchase(item)
            ? $"{item.displayName} 구매 완료 (인벤토리 연동 전 임시)"
            : "현금이 부족합니다.";
        Refresh();
    }
}
