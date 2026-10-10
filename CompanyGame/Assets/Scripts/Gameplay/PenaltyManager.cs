using UnityEngine;

public class PenaltyManager : MonoBehaviour
{
    public int Points => GetPoints(GameSession.LocalPlayerId);
    public static int GetPoints(string playerId) => LibraryCatalog.GetAccount(playerId).penaltyPoints;
}
