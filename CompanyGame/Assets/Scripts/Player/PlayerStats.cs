using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [Header("상태")]
    public float health = 100f;
    public float stress = 0f;

    [Header("스탯")]
    public int intelligence = 0;
    public int charm = 0;
    public int physical = 0;
    public int farming = 0;
    public int cooking = 0;
}
