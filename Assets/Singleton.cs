using UnityEngine;

/// <summary>
/// Scene-level singleton base. The instance is registered in Awake, so it is safe to access from any Start().
/// </summary>
public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    public static T Instance { get; private set; }

    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"Duplicate {typeof(T).Name} on '{name}' destroyed.");
            Destroy(this);
            return;
        }

        Instance = (T)this;
    }

    protected virtual void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
