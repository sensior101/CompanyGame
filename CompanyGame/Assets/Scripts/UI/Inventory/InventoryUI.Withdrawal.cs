using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class InventoryUI
{
    void PositionWithdrawal()
    {
        if (!withdrawalWindow || !bankButton) return;
        var parent = (RectTransform)withdrawalWindow.parent;
        float scale = window.localScale.x;
        withdrawalWindow.localScale = Vector3.one * scale;
        bankButton.GetWorldCorners(bankCorners);
        Vector2 position = parent.InverseTransformPoint((bankCorners[0] + bankCorners[3]) * .5f);
        position.y -= 8f * scale;
        float halfWidth = withdrawalWindow.rect.width * scale * .5f;
        float popupHeight = withdrawalWindow.rect.height * scale;
        position.x = Mathf.Clamp(position.x, parent.rect.xMin + halfWidth + 8f, parent.rect.xMax - halfWidth - 8f);
        position.y = Mathf.Clamp(position.y, parent.rect.yMin + popupHeight + 8f, parent.rect.yMax - 8f);
        withdrawalWindow.anchoredPosition = position;
    }

    void BuildWithdrawal()
    {
        var blocker = Panel("WithdrawalModal", modal.transform, Vector2.zero, Color.clear, 0f, 0f);
        UIBuild.Stretch(blocker);
        MakeButton(blocker, CloseWithdrawal);
        withdrawalModal = blocker.gameObject;
        var card = Panel("WithdrawalWindow", blocker, new Vector2(320f, 234f), new Color(.995f, .982f, .955f, .99f), 30f, 14f);
        withdrawalWindow = card;
        card.GetComponent<InventoryRoundedGraphic>().raycastTarget = true;
        card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
        card.pivot = new Vector2(.5f, 1f);
        var title = Label("WithdrawalTitle", card, "출금", 23f, Ink, new Vector2(288f, 40f));
        title.fontStyle = FontStyles.Bold;
        AtTop(title.rectTransform, 0f, -25f);
        var amountLabel = Label("AmountLabel", card, "금액", 15f, Ink, new Vector2(176f, 28f));
        amountLabel.alignment = TextAlignmentOptions.MidlineLeft;
        AtTop(amountLabel.rectTransform, -56f, -59f);
        var quantityLabel = Label("QuantityLabel", card, "장수", 15f, Ink, new Vector2(100f, 28f));
        quantityLabel.alignment = TextAlignmentOptions.MidlineLeft;
        AtTop(quantityLabel.rectTransform, 94f, -59f);
        denominationInput = InputField("WithdrawalAmount", card, new Vector2(176f, 48f), "개당 금액");
        AtTop(denominationInput.GetComponent<RectTransform>(), -56f, -99f);
        quantityInput = InputField("WithdrawalQuantity", card, new Vector2(100f, 48f), "1");
        AtTop(quantityInput.GetComponent<RectTransform>(), 94f, -99f);
        denominationInput.characterLimit = 19;
        quantityInput.characterLimit = 9;
        var amountNavigation = denominationInput.navigation;
        amountNavigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
        amountNavigation.selectOnRight = quantityInput;
        amountNavigation.selectOnDown = quantityInput;
        denominationInput.navigation = amountNavigation;
        var quantityNavigation = quantityInput.navigation;
        quantityNavigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
        quantityNavigation.selectOnLeft = denominationInput;
        quantityNavigation.selectOnUp = denominationInput;
        quantityInput.navigation = quantityNavigation;
        denominationInput.onValueChanged.AddListener(_ => ValidateWithdrawal());
        quantityInput.onValueChanged.AddListener(_ => ValidateWithdrawal());
        withdrawalError = Label("WithdrawalError", card, "", 12f, new Color(.64f, .23f, .2f), new Vector2(288f, 40f));
        withdrawalError.textWrappingMode = TextWrappingModes.Normal;
        AtTop(withdrawalError.rectTransform, 0f, -151f);
        var cancel = Panel("CancelWithdrawal", card, new Vector2(100f, 38f), new Color(.9f, .865f, .82f, .85f), 15f, 7f);
        AtBottom(cancel, -79f, 31f);
        MakeButton(cancel, CloseWithdrawal);
        Label("CancelText", cancel, "취소", 15f, Ink, new Vector2(92f, 31f));
        var confirm = Panel("ConfirmWithdrawal", card, new Vector2(137f, 38f), new Color(.72f, .49f, .33f, 1f), 15f, 7f);
        AtBottom(confirm, 54f, 31f);
        withdrawButton = MakeButton(confirm, SubmitWithdrawal);
        Label("ConfirmText", confirm, "확인 (Enter)", 14f, Color.white, new Vector2(128f, 31f));
        withdrawalModal.SetActive(false);
    }

    TMP_InputField InputField(string name, Transform parent, Vector2 size, string placeholder)
    {
        var rect = Panel(name, parent, size, new Color(1f, 1f, 1f, .97f), 19f, 8f);
        var background = rect.GetComponent<InventoryRoundedGraphic>();
        background.raycastTarget = true;
        var viewport = UIBuild.Rect("TextViewport", rect, size - new Vector2(24f, 10f));
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var text = Label("Text", viewport, "", 18f, Ink, size - new Vector2(24f, 10f));
        text.overflowMode = TextOverflowModes.Overflow;
        UIBuild.Stretch(text.rectTransform); text.alignment = TextAlignmentOptions.MidlineLeft;
        var hint = Label("Placeholder", viewport, placeholder, 15f, Muted, size - new Vector2(24f, 10f));
        UIBuild.Stretch(hint.rectTransform); hint.alignment = TextAlignmentOptions.MidlineLeft;
        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background; input.textViewport = viewport;
        input.textComponent = (TextMeshProUGUI)text; input.placeholder = hint;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.caretColor = Ink; input.customCaretColor = true;
        input.selectionColor = new Color(.84f, .68f, .46f, .45f);
        input.characterValidation = TMP_InputField.CharacterValidation.Digit;
        return input;
    }

    public void OpenWithdrawal()
    {
        if (!IsOpen || IsWithdrawalOpen) return;
        owner.CancelDrag(); HideItemTooltip(); withdrawalModal.SetActive(true);
        Canvas.ForceUpdateCanvases(); PositionWithdrawal();
        denominationInput.SetTextWithoutNotify(""); quantityInput.SetTextWithoutNotify("1");
        withdrawalError.text = "";
        ValidateWithdrawal(); denominationInput.Select(); denominationInput.ActivateInputField();
    }

    public void CloseWithdrawal()
    {
        if (!withdrawalModal) return;
        denominationInput.DeactivateInputField(); quantityInput.DeactivateInputField();
        withdrawalModal.SetActive(false);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
    }

    public bool HandleEscape()
    {
        if (!IsWithdrawalOpen) return false;
        CloseWithdrawal(); return true;
    }

    public void SetWithdrawalValues(string amount, string quantity)
    {
        denominationInput.text = amount; quantityInput.text = quantity; ValidateWithdrawal();
    }

    void ValidateWithdrawal()
    {
        if (!withdrawButton) return;
        bool valid = TryWithdrawalValues(out long amount, out int quantity, out string error);
        if (valid)
        {
            long total = amount * quantity;
            withdrawButton.interactable = total <= CashService.BankBalance;
            withdrawalError.text = total > CashService.BankBalance ? "지갑 잔액이 부족합니다." : "";
        }
        else
        {
            withdrawButton.interactable = false;
            withdrawalError.text = string.IsNullOrEmpty(denominationInput.text) ? "" : error;
        }
    }

    bool TryWithdrawalValues(out long amount, out int quantity, out string error)
    {
        error = "";
        bool a = long.TryParse(denominationInput.text, NumberStyles.None, CultureInfo.InvariantCulture, out amount);
        bool q = int.TryParse(quantityInput.text, NumberStyles.None, CultureInfo.InvariantCulture, out quantity);
        if (!a || amount < 0) { error = "한 개의 금액을 0원 이상으로 입력해 주세요."; return false; }
        if (!q || quantity < 1) { error = "장수를 1 이상으로 입력해 주세요."; return false; }
        if (amount > long.MaxValue / quantity) { error = "출금할 합계 금액이 너무 큽니다."; return false; }
        return true;
    }

    public void SubmitWithdrawal()
    {
        if (!IsWithdrawalOpen || withdrawing || submittedFrame == Time.frameCount) return;
        submittedFrame = Time.frameCount;
        if (!TryWithdrawalValues(out long amount, out int quantity, out string error))
        { withdrawalError.text = error; return; }
        withdrawing = true;
        try
        {
            if (!CashService.TryWithdraw(owner.Inventory, amount, quantity, out error))
            { withdrawalError.text = error; return; }
            owner.SetStatus(amount.ToString("N0") + "원 × " + quantity + (amount < 10000 ? "개를 꺼냈습니다." : "장을 꺼냈습니다."));
            CloseWithdrawal(); Refresh();
        }
        finally { withdrawing = false; }
    }
}
