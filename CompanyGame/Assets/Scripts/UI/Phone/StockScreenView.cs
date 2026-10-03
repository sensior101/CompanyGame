using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StockScreenView : MonoBehaviour
{
    [SerializeField] private StockApplication app;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private TMP_Text newsText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_InputField quantityInput;
    [SerializeField] private Transform rowContainer;
    [SerializeField] private GameObject rowTemplate;

    private class Row
    {
        public GameObject root;
        public TMP_Text name;
        public TMP_Text price;
        public TMP_Text change;
        public Button buy;
        public Button sell;
    }

    private readonly List<Row> rows = new List<Row>();

    private void Awake()
    {
        if (app == null) app = GetComponent<StockApplication>();
        app.Refreshed += Refresh;
        rowTemplate.SetActive(false);
    }

    private void OnDestroy()
    {
        if (app != null) app.Refreshed -= Refresh;
    }

    private void Refresh()
    {
        statusText.text = (app.IsMarketOpen ? "장 운영 중" : "장 마감") + "    " + PhoneFormat.Now();
        summaryText.text = "보유 주식 평가액   " + PhoneFormat.Won(app.GetPortfolioValue());

        var quotes = app.GetQuotes();
        EnsureRows(quotes.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].root.SetActive(i < quotes.Count);
            if (i < quotes.Count) Bind(rows[i], quotes[i]);
        }

        RefreshNews();
    }

    private void RefreshNews()
    {
        var news = app.GetNews();
        if (news.Count == 0)
        {
            newsText.text = "아직 발행된 뉴스가 없습니다.";
            return;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < news.Count && i < 4; i++) sb.AppendLine("· " + news[i].headline);
        newsText.text = sb.ToString().TrimEnd();
    }

    private void EnsureRows(int count)
    {
        while (rows.Count < count)
        {
            var go = Instantiate(rowTemplate, rowContainer);
            go.SetActive(true);
            rows.Add(new Row
            {
                root = go,
                name = go.transform.Find("Info/Name").GetComponent<TMP_Text>(),
                price = go.transform.Find("Info/Price").GetComponent<TMP_Text>(),
                change = go.transform.Find("Change").GetComponent<TMP_Text>(),
                buy = go.transform.Find("BuyButton").GetComponent<Button>(),
                sell = go.transform.Find("SellButton").GetComponent<Button>()
            });
        }
    }

    private void Bind(Row row, StockQuote quote)
    {
        long owned = 0;
        foreach (var holding in app.GetHoldings())
        {
            if (holding.stockId == quote.stockId) owned = holding.quantity;
        }

        row.name.text = quote.displayName + (owned > 0 ? $" ({owned}주)" : string.Empty);
        row.price.text = PhoneFormat.Won(quote.price);
        row.change.text = PhoneFormat.SignedPercent(quote.changeRate);
        row.change.color = PhoneFormat.ChangeColor(quote.changeRate);

        string stockId = quote.stockId;
        string stockName = quote.displayName;
        row.buy.onClick.RemoveAllListeners();
        row.buy.onClick.AddListener(() => Trade(stockId, stockName, true));
        row.sell.onClick.RemoveAllListeners();
        row.sell.onClick.AddListener(() => Trade(stockId, stockName, false));
    }

    private void Trade(string stockId, string stockName, bool buying)
    {
        int quantity = ReadQuantity();
        var receipt = buying ? app.Buy(stockId, quantity) : app.Sell(stockId, quantity);
        messageText.text = Describe(receipt, stockName, buying);
        Refresh();
    }

    private int ReadQuantity()
    {
        if (string.IsNullOrWhiteSpace(quantityInput.text)) return 1;
        return int.TryParse(quantityInput.text.Trim(), out int quantity) ? quantity : 0;
    }

    private static string Describe(TradeReceipt receipt, string stockName, bool buying)
    {
        switch (receipt.result)
        {
            case TradeResult.Success:
                return buying
                    ? $"{stockName} {receipt.quantity}주 매수 완료 ({PhoneFormat.Won(receipt.total)})"
                    : $"{stockName} {receipt.quantity}주 매도 완료 (실현손익 {receipt.realizedProfit:+#,0;-#,0;0}원)";
            case TradeResult.MarketClosed: return "지금은 장이 열려 있지 않습니다.";
            case TradeResult.NotEnoughMoney: return "현금이 부족합니다.";
            case TradeResult.NotEnoughShares: return "보유 수량이 부족합니다.";
            case TradeResult.InvalidQuantity: return "수량을 확인하세요.";
            case TradeResult.QuantityTooLarge: return "한 번에 주문할 수 있는 수량을 넘었습니다.";
            default: return "종목을 찾을 수 없습니다.";
        }
    }
}
