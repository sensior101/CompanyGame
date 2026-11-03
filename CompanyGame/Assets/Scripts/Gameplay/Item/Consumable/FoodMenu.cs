using System.Collections.Generic;

/// <summary>
/// Every food's price, effects and fridge days as decided for the game, keyed by the crafted food library id (menu.json).
/// Effects are HUD points (each bar is 20): fullness, stamina, and stress relief. A restaurant meal (Korean, Chinese,
/// Japanese, Western, Southeast Asian/Indian) also gives 든든함. ItemData assets and the shop prices take these values
/// through CompanyGame/Setup/Sync Food Data; FoodShelfLife reads the fridge days from here.
/// </summary>
public static class FoodMenu
{
    public enum Category { Korean, Chinese, Japanese, Western, FastFood, Healthy, Asian, Bakery, Bingsu, SoftServe, Cafe, Ade, Smoothie, Convenience }

    public sealed class Entry
    {
        public readonly string id, name, itemId;
        public readonly Category category;
        public readonly long price;
        public readonly int fullness, stamina, stressRelief, fridgeDays;
        public readonly bool drink;

        public Entry(string id, string name, Category category, long price, int fullness, int stamina, int stressRelief,
            int fridgeDays, bool drink = false, string itemId = null)
        {
            this.id = id; this.name = name; this.category = category; this.price = price; this.fullness = fullness;
            this.stamina = stamina; this.stressRelief = stressRelief; this.fridgeDays = fridgeDays; this.drink = drink;
            this.itemId = itemId;
        }

        public bool RestaurantMeal => category <= Category.Western || category == Category.Asian;
        public ConsumableType Type => drink ? ConsumableType.Drink : ConsumableType.Food;
    }

    const Category K = Category.Korean, C = Category.Chinese, J = Category.Japanese, W = Category.Western, F = Category.FastFood,
        H = Category.Healthy, A = Category.Asian, B = Category.Bakery, I = Category.Bingsu, S = Category.SoftServe,
        Cafe = Category.Cafe, Ade = Category.Ade, Smoothie = Category.Smoothie, Store = Category.Convenience;

    //                     id                      name                    cat  price  fullness stamina stress days
    public static readonly Entry[] All =
    {
        new Entry("Bossam",               "보쌈",                 K, 18000, 12,  0,  0,  5),
        new Entry("Jokbal",               "족발",                 K, 20000, 12,  0,  3,  5),
        new Entry("DoenjangStew",         "된장찌개",             K,  8000,  6,  0,  0,  5),
        new Entry("KimchiStew",           "김치찌개",             K,  9000,  7,  0,  0,  5),
        new Entry("Gimbap",               "김밥",                 K,  3500,  3,  0,  0,  3, false, "food:kimbap"), // food:gimbap is the store's 삼각김밥
        new Entry("ColdNoodles",          "물냉면",               K, 10000,  6,  0,  0,  3),
        new Entry("SpicyColdNoodles",     "비빔냉면",             K, 10000,  6,  0,  0,  3),
        new Entry("Samgyeopsal",          "삼겹살",               K, 17000, 12,  0,  3,  5),
        new Entry("Samgyetang",           "삼계탕",               K, 17000, 10, 10,  0,  5),
        new Entry("SeafoodPancake",       "해물파전",             K, 13000,  6,  0,  3,  3),
        new Entry("SpicyPork",            "제육볶음",             K, 10000,  8,  0,  0,  5),
        new Entry("SteamedEgg",           "계란찜",               K,  5000,  4,  0,  0,  3),
        new Entry("StoneBibimbap",        "돌솥비빔밥",           K, 10000,  8,  0,  0,  3),
        new Entry("Tteokbokki",           "떡볶이",               K,  5000,  4,  0,  2,  3),
        new Entry("PorkSoup",             "돼지국밥",             K, 10000,  8,  5,  0,  5),
        new Entry("ChickenFeet",          "닭발",                 K, 13000,  5,  0,  5,  5),
        new Entry("MozzarellaCornDog",    "모짜렐라 핫도그",      K,  3500,  2,  0,  1,  3),
        new Entry("Sundae",               "순대",                 K,  5000,  4,  0,  0,  3),

        new Entry("Jajangmyeon",          "짜장면",               C,  7000,  6,  0,  0,  3),
        new Entry("Jjamppong",            "짬뽕",                 C,  9000,  6,  0,  3,  3),
        new Entry("Tangsuyuk",            "탕수육",               C, 18000,  5,  0,  5,  5),
        new Entry("Yangjangpi",           "양장피",               C, 22000,  8,  0,  8,  3),
        new Entry("Yuringi",              "유린기",               C, 18000,  9,  0,  3,  3),
        new Entry("MapoTofu",             "마파두부",             C, 10000,  8,  0,  0,  5),
        new Entry("Malatang",             "마라탕",               C, 12000,  8,  0,  4,  3),
        new Entry("MalaXiangguo",         "마라샹궈",             C, 18000, 10,  0,  5,  3),
        new Entry("Laziji",               "라조기",               C, 19000, 10,  0,  0,  5),
        new Entry("Guobaorou",            "꿔바로우",             C, 18000,  9,  0,  4,  5),
        new Entry("DongpoPork",           "동파육",               C, 25000,  6,  2,  2,  5),
        new Entry("DimSum",               "딤섬",                 C,  7000,  4,  0,  2,  5),
        new Entry("ChiliShrimp",          "깐쇼새우",             C, 20000,  9,  0,  4,  3),
        // 춘권 in the design table; the library's SpringRolls model is 월남쌈 until a spring roll model exists.
        new Entry("SpringRolls",          "춘권",                 C,  6000,  3,  0,  2,  5),

        new Entry("SalmonSushi",          "연어 초밥",            J, 12000,  7,  3,  0,  3),
        new Entry("ShrimpSushi",          "새우 초밥",            J, 10000,  6,  2,  0,  3),
        new Entry("TunaSushi",            "참치 초밥",            J, 14000,  8,  3,  0,  3),
        new Entry("EggSushi",             "계란 초밥",            J,  8000,  5,  0,  0,  3),
        new Entry("EelRice",              "장어덮밥",             J, 17000,  8,  5,  0,  3),
        new Entry("Okonomiyaki",          "오코노미야키",         J, 11000,  7,  0,  2,  3),
        new Entry("Omurice",              "오므라이스",           J, 10000,  8,  0,  0,  3),
        new Entry("Ramen",                "라멘",                 J, 10000,  8,  0,  0,  3),
        new Entry("ShrimpTempura",        "새우튀김",             J,  7000,  4,  0,  2,  3),
        new Entry("Tendon",               "텐동",                 J, 12000,  9,  0,  0,  3),
        new Entry("Takoyaki",             "타코야키",             J,  5000,  3,  0,  2,  3),
        new Entry("TokyoCurry",           "도쿄 카레",            J, 10000,  8,  0,  0,  5),
        new Entry("Udon",                 "우동",                 J,  8000,  6,  0,  0,  3),
        new Entry("ColdSoba",             "냉소바",               J,  9000,  7,  0,  0,  3),
        new Entry("PorkCutlet",           "돈카츠",               J, 12000,  9,  0,  2,  5),
        new Entry("CheeseCutlet",         "치즈카츠",             J, 13000,  9,  0,  4,  5),

        new Entry("Steak",                "스테이크",             W, 32000, 10,  5,  5,  5),
        new Entry("BBQRibs",              "BBQ 립",               W, 28000, 15,  0,  5,  5),
        new Entry("CreamPasta",           "크림 파스타",          W, 14000,  9,  0,  3,  3),
        new Entry("TomatoPasta",          "토마토 파스타",        W, 13000,  9,  0,  0,  5),
        new Entry("Risotto",              "리조또",               W, 14000,  9,  0,  0,  3),
        new Entry("MushroomSoup",         "양송이 수프",          W,  6000,  4,  0,  2,  5),

        new Entry("Hamburger",            "햄버거",               F,  6000,  5,  0,  0,  3),
        new Entry("Cheeseburger",         "치즈버거",             F,  7000,  6,  0,  1,  3),
        new Entry("HotDog",               "핫도그",               F,  3000,  2,  0,  0,  3),
        new Entry("FrenchFries",          "감자튀김",             F,  3500,  2,  0,  2,  3),
        new Entry("FishAndChips",         "피시앤칩스",           F, 12000,  8,  0,  2,  3),
        new Entry("Pizza",                "피자",                 F, 18000, 11,  0,  4,  5),
        new Entry("Nachos",               "나초",                 F,  5000,  2,  0,  3,  7),
        new Entry("Taco",                 "타코",                 F,  7000,  3,  3,  0,  3),
        new Entry("Kebab",                "케밥",                 F,  8000,  5,  5,  0,  3),

        new Entry("Salad",                "샐러드",               H,  8000,  3,  3,  0,  3),
        new Entry("Sandwich",             "샌드위치",             H,  7000,  5,  3,  0,  3),
        new Entry("GreekYogurt",          "그릭요거트",           H,  6000,  2,  2,  0,  7),

        new Entry("GreenCurry",           "그린커리",             A, 12000,  9,  0,  0,  5),
        new Entry("CrabCurry",            "크랩커리",             A, 17000, 10,  0,  3,  3),
        new Entry("TomYum",               "똠얌",                 A, 12000,  9,  0,  0,  5),
        new Entry("Pho",                  "쌀국수",               A, 10000,  8,  0,  0,  3),
        new Entry("NasiGoreng",           "나시고렝",             A, 11000,  8,  0,  0,  3),
        new Entry("MieGoreng",            "미고렝",               A, 11000,  8,  0,  0,  3),
        new Entry("Naan",                 "난",                   A,  4000,  3,  0,  0,  5),
        new Entry("TandooriChicken",      "탄두리 치킨",          A, 16000,  6,  6,  0,  5),

        new Entry("Brownie",              "브라우니",             B,  4500,  0,  0,  5,  7),
        new Entry("Cheesecake",           "치즈케이크",           B, 20000,  0,  0, 10,  5),
        new Entry("ChocolateCake",        "초코케이크",           B, 20000,  0,  0, 10,  5),
        new Entry("StrawberryCake",       "딸기케이크",           B, 20000,  0,  0, 10,  3),
        new Entry("ChocolateChipCookie",  "초코칩 쿠키",          B,  2500,  0,  0,  3, 14),
        new Entry("Churros",              "츄러스",               B,  4000,  2,  0,  3,  3),
        new Entry("Croissant",            "크루아상",             B,  3800,  2,  0,  2,  5),
        new Entry("Donut",                "도넛",                 B,  3000,  2,  0,  3,  5),
        new Entry("EggTart",              "에그타르트",           B,  3500,  2,  0,  3,  5),
        new Entry("Financier",            "휘낭시에",             B,  2800,  0,  0,  4,  7),
        new Entry("Macaron",              "마카롱",               B,  3000,  0,  0,  5,  7),
        new Entry("Pretzel",              "프레첼",               B,  3500,  3,  0,  0,  7),
        new Entry("Pudding",              "푸딩",                 B,  4500,  0,  0,  6,  5),
        new Entry("SaltBread",            "소금빵",               B,  3000,  3,  0,  0,  5),
        new Entry("Scone",                "스콘",                 B,  3500,  3,  0,  0,  7),
        new Entry("Tiramisu",             "티라미수",             B,  6500,  0,  3,  7,  5),
        new Entry("Waffle",               "와플",                 B,  5000,  2,  0,  2,  3),

        new Entry("BlueberryBingsu",      "블루베리 빙수",        I, 11000,  0,  0,  8,  3),
        new Entry("ChocolateBingsu",      "초코 빙수",            I, 10000,  0,  0,  8,  3),
        new Entry("MangoBingsu",          "망고 빙수",            I, 12000,  0,  0,  8,  3),
        new Entry("MatchaBingsu",         "말차 빙수",            I, 11000,  0,  0,  8,  3),
        new Entry("StrawberryBingsu",     "딸기 빙수",            I, 12000,  0,  0,  8,  3),

        new Entry("VanillaSoftServe",     "바닐라 소프트아이스크림", S, 3000, 0, 0,  3, 20),
        new Entry("ChocolateSoftServe",   "초코 소프트아이스크림",   S, 3000, 0, 0,  3, 20),
        new Entry("MangoSoftServe",       "망고 소프트아이스크림",   S, 3500, 0, 0,  3, 20),
        new Entry("MatchaSoftServe",      "말차 소프트아이스크림",   S, 3500, 0, 0,  3, 20),
        new Entry("OrangeSoftServe",      "오렌지 소프트아이스크림", S, 3500, 0, 0,  3, 20),
        new Entry("StrawberrySoftServe",  "딸기 소프트아이스크림",   S, 3500, 0, 0,  3, 20),

        new Entry("HotAmericano",         "아메리카노 (핫)",      Cafe, 3500, 0, 5,  0,  7, true),
        new Entry("IcedAmericano",        "아메리카노 (아이스)",  Cafe, 4000, 0, 5,  0,  7, true),
        new Entry("CafeLatte",            "카페라떼",             Cafe, 4500, 0, 4,  1,  5, true),
        new Entry("ChocolateLatte",       "초코라떼",             Cafe, 5000, 0, 0,  3,  5, true),
        new Entry("MatchaLatte",          "말차라떼",             Cafe, 5500, 0, 3,  2,  5, true),
        new Entry("StrawberryLatte",      "딸기라떼",             Cafe, 5500, 0, 0,  4,  3, true),
        new Entry("SweetPotatoLatte",     "고구마라떼",           Cafe, 5000, 2, 0,  2,  5, true),
        new Entry("MilkTea",              "밀크티",               Cafe, 5000, 0, 3,  2,  5, true),
        new Entry("BlackTea",             "홍차",                 Cafe, 4000, 0, 2,  0,  7, true),
        new Entry("Chamomile",            "캐모마일",             Cafe, 4000, 0, 0,  3,  7, true),
        new Entry("EarlGrey",             "얼그레이",             Cafe, 4000, 0, 2,  0,  7, true),
        new Entry("GreenTea",             "녹차",                 Cafe, 3500, 0, 2,  0,  7, true),
        new Entry("PeachIcedTea",         "복숭아 아이스티",      Cafe, 4000, 0, 0,  2,  7, true),
        new Entry("YujaTea",              "유자차",               Cafe, 4500, 0, 0,  3,  7, true),

        new Entry("LemonAde",             "레몬 에이드",          Ade, 5000, 0, 2,  2,  5, true),
        new Entry("GrapefruitAde",        "자몽 에이드",          Ade, 5000, 0, 2,  2,  5, true),
        new Entry("GreenGrapeAde",        "청포도 에이드",        Ade, 5500, 0, 2,  2,  5, true),

        new Entry("BlueberrySmoothie",    "블루베리 스무디",      Smoothie, 5500, 0, 0, 3, 3, true),
        new Entry("CookiesCreamSmoothie", "쿠키앤크림 스무디",    Smoothie, 6000, 0, 0, 3, 5, true),
        new Entry("KiwiSmoothie",         "키위 스무디",          Smoothie, 5500, 0, 0, 3, 3, true),
        new Entry("MangoSmoothie",        "망고 스무디",          Smoothie, 6000, 0, 0, 3, 3, true),
        new Entry("StrawberrySmoothie",   "딸기 스무디",          Smoothie, 5500, 0, 0, 3, 3, true),
        new Entry("YogurtSmoothie",       "요거트 스무디",        Smoothie, 5500, 0, 0, 3, 3, true),

        new Entry("Chips",                "감자칩",               Store, 1800, 0, 0,  2, 20, false, "food:chips"),
        new Entry("Chocolate",            "초콜릿",               Store, 2000, 0, 0,  2, 20, false, "food:chocolate"),
        new Entry("Cola",                 "콜라",                 Store, 2000, 0, 2,  0, 20, true,  "food:cola"),
        new Entry("CupRamen",             "컵라면",               Store, 1800, 2, 0,  0, 20, false, "food:cup-ramen"),
        new Entry("EnergyBar",            "에너지바",             Store, 2000, 0, 3,  0, 10, false, "food:energy-bar"),
        new Entry("EnergyDrink",          "에너지드링크",         Store, 2500, 0, 3,  0, 10, true,  "food:energy-drink"),
        new Entry("Jelly",                "젤리",                 Store, 1500, 0, 0,  2, 15, false, "food:jelly"),
        new Entry("LunchBox",             "도시락",               Store, 5500, 5, 0,  0,  5, false, "food:lunch-box"),
        new Entry("TriangleGimbap",       "삼각김밥",             Store, 1500, 2, 0,  0,  3, false, "food:gimbap"),
        new Entry("Water",                "생수",                 Store, 1000, 0, 1,  0, 80, true,  "food:water"),
    };

    static Dictionary<string, Entry> byKey;

    static Dictionary<string, Entry> ByKey
    {
        get
        {
            if (byKey != null) return byKey;
            byKey = new Dictionary<string, Entry>();
            foreach (var entry in All)
            {
                byKey[entry.id] = entry;
                if (!string.IsNullOrEmpty(entry.itemId)) byKey[entry.itemId] = entry;
            }
            return byKey;
        }
    }

    /// <summary>The entry for a library id or an item id.</summary>
    public static bool TryGet(string key, out Entry entry)
    {
        entry = null;
        return !string.IsNullOrEmpty(key) && ByKey.TryGetValue(key, out entry);
    }

    /// <summary>The entry for an item: its itemId first, then its asset name (library id).</summary>
    public static bool TryGet(ItemData item, out Entry entry)
    {
        entry = null;
        return item && (TryGet(item.itemId, out entry) || TryGet(item.name, out entry));
    }
}
