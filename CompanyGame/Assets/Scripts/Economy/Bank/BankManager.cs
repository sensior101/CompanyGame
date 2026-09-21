using System;
using UnityEngine;

public class BankManager : MonoBehaviour
{
    public static BankManager Instance { get; private set; }

    [SerializeField]
    private EconomyManager economyManager;

    private BankAccount account;

    public event Action<long> BalanceChanged;

    public BankAccount Account
    {
        get
        {
            EnsureInitialized();
            return account;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        EnsureInitialized();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void EnsureInitialized()
    {
        if (account != null) return;

        if (economyManager == null) economyManager = FindAnyObjectByType<EconomyManager>();
        account = new BankAccount(new EconomyWallet(economyManager));
        account.BalanceChanged += balance => BalanceChanged?.Invoke(balance);
    }
}
