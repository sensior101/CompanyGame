/// <summary>
/// How this game instance runs. Single player and the host own the simulation
/// (economy, clock, events); a client only mirrors what the host sends.
/// Online random matching is a way to find a host, so it is still Host or Client.
/// </summary>
public enum SessionMode { Single, Host, Client }

public static class GameSession
{
    // 기본은 방장 호스트. 네트워크가 붙기 전에는 Single과 같게 동작한다.
    public static SessionMode Mode { get; private set; } = SessionMode.Host;

    /// <summary>이 클라이언트 플레이어의 표시 이름. 소유권·시스템 메시지가 쓴다.</summary>
    public static string LocalPlayerName { get; set; } = "Player";

    /// <summary>True where economy, time and events are computed. Gate every simulation write on this.</summary>
    public static bool IsAuthority => Mode != SessionMode.Client;

    /// <summary>Called by the network layer (Net) when a session starts or ends.</summary>
    public static void Begin(SessionMode mode) => Mode = mode;
}
