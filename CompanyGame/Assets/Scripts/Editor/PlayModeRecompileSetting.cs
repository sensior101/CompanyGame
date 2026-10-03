using UnityEditor;

/// <summary>
/// Recompiling scripts in the middle of Play resets every runtime reference (player, inventory)
/// and floods the Console with errors. Recompile only after Play ends.
/// Same as Preferences > General > Script Changes While Playing > Recompile After Finished Playing.
/// </summary>
[InitializeOnLoad]
static class PlayModeRecompileSetting
{
    const int RecompileAfterFinishedPlaying = 1;

    static PlayModeRecompileSetting() => EditorPrefs.SetInt("ScriptCompilationDuringPlay", RecompileAfterFinishedPlaying);
}
