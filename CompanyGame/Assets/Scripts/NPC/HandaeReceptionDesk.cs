using UnityEngine;

/// <summary>
/// Answers the Handae lobby reception choices. Construction and demolition have no game system yet, so the staff
/// reply with a notice; the notice also ends the conversation so the dialogue camera is released.
/// </summary>
public static class HandaeReceptionDesk
{
    const string NpcPrefix = "handae_reception_";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        DialogueManager.EnsureInstance();
        DialogueManager.Instance.OptionSelected -= OnOption;
        DialogueManager.Instance.OptionSelected += OnOption;
    }

    static void OnOption(DialogueData npc, string id)
    {
        if (!npc || npc.npcId == null || !npc.npcId.StartsWith(NpcPrefix)) return;
        string reply = id switch
        {
            "construction_consultation" => "건축 상담은 현재 준비 중입니다. 서비스가 열리면 다시 안내해 드리겠습니다.",
            "construction_progress" => "진행 중인 공사 정보를 확인하고 있습니다. 잠시 후 다시 문의해 주세요.",
            "building_demolition" => "건물 철거 신청은 현재 준비 중입니다.",
            "building_entry" => "건물에 출입하시려면 ID카드를 소지하고 게이트를 통과해 주세요.",
            _ => null
        };
        if (reply != null) DialogueManager.Instance.PresentNotice(npc, reply);
    }
}
