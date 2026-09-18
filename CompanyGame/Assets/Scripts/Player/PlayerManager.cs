using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public PlayerMovement Movement { get; private set; }
    public PlayerStats Stats { get; private set; }
    public PlayerInteraction Interaction { get; private set; }

    private void Awake()
    {
        Movement = GetComponent<PlayerMovement>();
        Stats = GetComponent<PlayerStats>();
        Interaction = GetComponent<PlayerInteraction>();
    }
}
