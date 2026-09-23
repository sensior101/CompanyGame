using System;
using UnityEngine;

public class BankManager : MonoBehaviour
{
    public static BankManager Instance { get; private set; }

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

        account = new BankAccount(new PropertyWallet());
        account.BalanceChanged += balance => BalanceChanged?.Invoke(balance);
    }
}
