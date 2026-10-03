using UnityEngine;

public static class PhoneFormat
{
    public static readonly Color Up = new Color(0.93f, 0.32f, 0.32f, 1f);
    public static readonly Color Down = new Color(0.35f, 0.55f, 0.95f, 1f);
    public static readonly Color Flat = new Color(0.62f, 0.66f, 0.74f, 1f);

    public static string Won(long amount) => amount.ToString("N0") + "원";

    public static string Percent(float fraction) => (fraction * 100f).ToString("0.0") + "%";

    public static string SignedPercent(float fraction) => (fraction > 0f ? "+" : "") + Percent(fraction);

    public static string Now() => GameClock.Current != null ? GameClock.Current.Now.ToString() : "--";

    public static int Today() => GameClock.Current != null ? GameClock.Current.Now.absoluteDay : 0;

    /// <summary>Korean market convention: red for up, blue for down.</summary>
    public static Color ChangeColor(float fraction) => fraction > 0f ? Up : fraction < 0f ? Down : Flat;

    public static bool TryParseAmount(string text, out long amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return long.TryParse(text.Replace(",", "").Trim(), out amount) && amount > 0;
    }
}
