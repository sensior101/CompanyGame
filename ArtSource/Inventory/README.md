# Inventory currency artwork

## Active artwork

The six designs were supplied by the user. Unity uses the source pixels, with Sprite rectangles to omit the empty margins around banknotes. The actual face value is a separate label beneath each icon.

| Resource | Source clipboard image | Face value |
| --- | --- | --- |
| CurrencySilver.png | 137149ba-074e-42af-8fdd-d5b5eb9dd8de | 100–999 |
| CurrencyGold.png | f8c6dd8c-08b8-469e-99fd-0c10d8b3508e | 1,000–9,999 |
| CurrencyBlue.png | 6d9d43fc-e6a5-4240-840b-c308b3b8cd3e | 10,000–99,999 |
| CurrencyGreen.png | 0949e267-beb3-4b3f-9018-8506bd922d04 | 100,000–999,999 |
| CurrencyYellow.png | 3d223b19-6984-41ff-b00f-8539150acaac | 1,000,000–9,999,999 |
| CurrencyRed.png | 7d5a80d4-32e6-4918-b107-b72228d1f834 | 10,000,000+ |

Assets are in `CompanyGame/Assets/Resources/Inventory`.

## Gold transparency

The supplied gold coin had an opaque black background. The built-in image generation tool made the transparent cutout, preserving the coin design. Task: remove only the black background, retain the supplied gold coin, its geometry, shading, embossed patterns and proportions, and output a transparent PNG. No other currency was regenerated. The original is retained in `Source/CurrencyGoldOriginal.png`.

`Source/UnusedTofuCurrencyPrototype.png` is an earlier unused prototype; it is not included in game resources.

## Verification

`QA` contains the native Unity Play Mode captures and JSON acceptance reports. See `Docs/Inventory.md` for controls and system scope.
