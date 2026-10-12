/// <summary>
/// The one place that knows which server the game talks to when the player has not typed an address.
///
/// DEPLOY: after deploying the Cloudflare server, replace <see cref="PublicUrl"/> below with the real address
/// (https://..., no trailing slash) and rebuild. Nothing else needs to change.
///
/// Order used at run time (see <see cref="LocalAccount.ServerUrl"/>): the `-server &lt;url&gt;` command-line argument,
/// then the address typed under Settings > 고급 (kept per profile slot), then <see cref="PublicUrl"/>.
/// </summary>
public static class ServerConfig
{
    /// <summary>The always-on public server every player connects to by default.</summary>
    public const string PublicUrl = "https://companygame-server.ladon73-00.workers.dev";

    /// <summary>The test server started by Server/start-test.cmd.</summary>
    public const string LocalUrl = "http://localhost:3000";

    /// <summary>True while <see cref="PublicUrl"/> still holds the REPLACE placeholder (nobody filled in the deployed address yet).</summary>
    public static bool IsPlaceholder => PublicUrl.Contains("REPLACE");

    static string cliUrl;
    static bool cliRead;

    /// <summary>The address from `-server &lt;url&gt;` (null when absent). It beats everything else and cannot be changed in the game.</summary>
    public static string CommandLineUrl
    {
        get
        {
            if (!cliRead) { cliUrl = FromArgs(System.Environment.GetCommandLineArgs()); cliRead = true; }
            return cliUrl;
        }
    }

    public static string FromArgs(string[] args)
    {
        for (int i = 0; args != null && i + 1 < args.Length; i++)
            if (args[i] == "-server" && !string.IsNullOrWhiteSpace(args[i + 1])) return args[i + 1].Trim();
        return null;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cliUrl = null; cliRead = false; }
}
