using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The convenience store clerk greets with a choice instead of opening the trade window straight away.
/// A DialogueData is added to the store's NpcTrader at load, so the DialogueManager takes the Space press first.
/// </summary>
public static class ConvenienceClerk
{
    const string NpcId = "convenience_clerk";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        DialogueManager.EnsureInstance();
        DialogueManager.Instance.OptionSelected -= OnOption;
        DialogueManager.Instance.OptionSelected += OnOption;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) Attach(SceneManager.GetSceneAt(i));
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

    static void Attach(Scene scene)
    {
        if (!scene.isLoaded) return;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var trader in root.GetComponentsInChildren<NpcTrader>(true))
        {
            if (trader.shopId != TradeCatalog.ConvenienceStore || trader.GetComponent<DialogueData>()) continue;
            trader.openAfterDialogue = false;
            var dialogue = trader.gameObject.AddComponent<DialogueData>();
            dialogue.npcId = NpcId;
            dialogue.line = "어서 오세요! 무엇을 도와드릴까요?";
            dialogue.radius = trader.radius;
            dialogue.options = new[]
            {
                new DialogueOption("Buy", "구매하기"),
                new DialogueOption("Job", "알바하기"),
                new DialogueOption("Cancel", "취소")
            };
        }
    }

    static void OnOption(DialogueData npc, string id)
    {
        if (!npc || npc.npcId != NpcId) return;
        var manager = DialogueManager.Instance;
        if (id == "Buy") manager.StartCoroutine(OpenTradeNextFrame());
        // The part-time job is not built yet; choosing it only gets a reply.
        else if (id == "Job") manager.PresentNotice(npc, "알바는 아직 모집하지 않아요. 다음에 다시 와 주세요!");
        else manager.Close();
    }

    // The Space press that chose the option is consumed this frame, so the trade window opens on the next one.
    static IEnumerator OpenTradeNextFrame()
    {
        yield return null;
        var interaction = PlayerInteraction.Local;
        if (interaction) interaction.TryTradeWithNearest();
    }
}
