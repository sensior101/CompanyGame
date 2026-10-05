using System;
using UnityEngine;

/// <summary>User-supplied currency art; the exact value is shown in the item tooltip.</summary>
public static class CurrencyIconFactory
{
    static readonly Sprite[] sprites = new Sprite[6];
    static readonly string[] Names = { "CurrencySilver", "CurrencyGold", "CurrencyBlue", "CurrencyGreen", "CurrencyYellow", "CurrencyRed" };

    public static Sprite GetSprite(long faceValue)
    {
        if (faceValue < CashService.MinimumFaceValue) throw new ArgumentOutOfRangeException(nameof(faceValue));
        int design = (int)CashService.DesignFor(faceValue);
        if (sprites[design]) return sprites[design];
        var texture = Resources.Load<Texture2D>("Inventory/" + Names[design]);
        if (!texture) return null;
        // Native sprite rectangles omit the supplied notes' outer blank margins.
        // Original source pixels remain untouched, including all printed patterns.
        var rect = design < 2 ? new Rect(0, 0, texture.width, texture.height) :
            new Rect(28f / 1672f * texture.width, 152f / 941f * texture.height,
                1615f / 1672f * texture.width, 621f / 941f * texture.height);
        sprites[design] = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
        sprites[design].name = Names[design];
        sprites[design].hideFlags = HideFlags.DontSave;
        return sprites[design];
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i]) UnityEngine.Object.Destroy(sprites[i]);
            sprites[i] = null;
        }
    }
}
