using UnityEngine;

/// <summary>Names, lines and looks for the citizens. Everything is a pure function of (map hash, index) so host and clients agree.</summary>
public static class CitizenInfo
{
    public static readonly string[] Names =
    {
        "김민준", "이서연", "박지호", "최수아", "정도윤", "한예린", "강태민", "윤하은", "임준서", "오지우",
        "서현우", "신유나", "권지안", "황민재", "안소율", "송재현", "조은채", "배성민", "문다온", "홍세아",
    };

    public static readonly string[] Lines =
    {
        "오늘 날씨 참 좋죠? 산책하기 딱 좋아요.",
        "이 동네는 아침마다 빵 굽는 냄새가 나서 좋아요.",
        "요즘 물가가 너무 올랐어요. 장 보러 가기가 겁나요.",
        "횡단보도 건널 땐 꼭 초록불 확인하세요. 차가 은근히 빨라요.",
        "퇴근길에 천천히 걷는 게 요즘 제 낙이에요.",
        "어제 뉴스 보셨어요? 주가가 또 크게 출렁였대요.",
        "이 근처에 국밥 잘하는 집이 있다던데, 가보셨어요?",
        "벤치에 앉아서 멍하니 있는 시간이 제일 좋더라고요.",
        "새 건물이 계속 올라가네요. 동네가 금방 바뀌어요.",
        "길 찾고 계세요? 저도 아직 이 골목은 헷갈려요.",
        "자전거 타는 분들 보면 부러워요. 바람이 시원할 것 같아요.",
        "해 질 무렵 이 거리가 제일 예뻐요. 한번 보세요.",
        "고시원 생활이 쉽진 않죠. 그래도 힘내세요!",
        "잠깐 쉬었다 가려고요. 걷다 보니 다리가 아프네요.",
    };

    public static readonly string[] LibraryLines =
    {
        "쉿, 도서관에서는 조용히 해주세요.",
        "이 책 정말 재미있어요. 읽다 보면 시간 가는 줄 몰라요.",
        "경제 서적은 2층에 모여 있어요.",
        "요즘 주식 책만 빌려 가는 사람이 많더라고요.",
        "대출 기한이 내일까지라 서둘러 읽는 중이에요.",
        "창가 자리가 제일 조용하고 좋아요.",
        "신간 코너에 좋은 책이 들어왔대요. 한번 보세요.",
        "카페에서 커피 한 잔 하고 다시 와야겠어요.",
    };

    public static readonly string[] OfficeLines =
    {
        "오늘 회의가 세 개나 있어서 정신이 없어요.",
        "점심은 뭘 먹을지 벌써부터 고민이에요.",
        "한대 본사는 처음이세요? 안내 데스크는 저쪽이에요.",
        "사원증이 없으면 게이트를 못 지나가요. 꼭 챙기세요.",
        "엘리베이터가 오늘따라 느리네요.",
        "커피 한 잔만 마시고 올라가려고요.",
        "이번 분기 실적 때문에 다들 예민해요.",
        "퇴근하고 한잔 어때요? 아, 일 얘기는 그만하고요.",
    };

    static readonly Color[] SkinTones =
    {
        new Color(.91f, .72f, .62f), new Color(.86f, .66f, .54f), new Color(.80f, .58f, .46f),
        new Color(.95f, .80f, .70f), new Color(.70f, .49f, .38f),
    };

    static readonly Color[] Clothes =
    {
        new Color(.52f, .52f, .52f), new Color(.20f, .27f, .42f), new Color(.55f, .25f, .22f), new Color(.30f, .45f, .33f),
        new Color(.72f, .62f, .40f), new Color(.18f, .18f, .20f), new Color(.60f, .38f, .50f), new Color(.85f, .82f, .74f),
        new Color(.28f, .50f, .58f), new Color(.75f, .45f, .20f), new Color(.40f, .32f, .26f), new Color(.64f, .70f, .78f),
    };

    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    /// <summary>Stable FNV-1a (string.GetHashCode is not stable across runs).</summary>
    public static int Hash(string text)
    {
        uint h = 2166136261;
        foreach (char c in text ?? "") { h ^= c; h *= 16777619; }
        return (int)(h & 0x7fffffff);
    }

    // Steps 3 (names, 20) and 3 (lines, 14) are coprime with the table sizes, so 8 citizens on one map never repeat.
    public static string NameFor(int mapHash, int index) => Names[(mapHash + index * 3) % Names.Length];
    public static string LineFor(int mapHash, int index) => Lines[(mapHash / 7 + index * 3) % Lines.Length];
    public static string LineFor(string[] table, int mapHash, int index) => table[(mapHash / 7 + index) % table.Length];

    /// <summary>Tints skin (submesh 0) and clothes (other submeshes) per citizen without creating materials.</summary>
    public static void Dress(GameObject citizen, int seed)
    {
        var skin = citizen.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (!skin) return;
        var rng = new System.Random(seed);
        var block = new MaterialPropertyBlock();
        for (int m = 0; m < skin.sharedMaterials.Length; m++)
        {
            skin.GetPropertyBlock(block, m);
            block.SetColor(BaseColor, m == 0 ? SkinTones[rng.Next(SkinTones.Length)] : Clothes[rng.Next(Clothes.Length)]);
            skin.SetPropertyBlock(block, m);
        }
    }
}
