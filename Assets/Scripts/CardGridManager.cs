using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

public class CardGridManager : MonoBehaviour
{
    [Header("UI Elements")] [SerializeField]
    private GameObject prefabItem; // Your Image + TMPro Prefab

    [SerializeField] private Transform contentPanel; // The GameObject with the GridLayoutGroup

    private Action<SupportCardData> onGridCompleteCallback;
    private int loadGeneration;

    /// <summary>
    /// Call this method to open and initialize the grid with your data list.
    /// </summary>
    public void FetchAndInitializeGrid(
        Action<SupportCardData> onItemSelected,
        IEnumerable<string> unavailableCardIds,
        string traineeCharacterId,
        Action<string> onError = null)
    {
        int generation = ++loadGeneration;
        GameClient client = GameClient.Instance;
        if (client == null)
        {
            onError?.Invoke("Game client is unavailable.");
            return;
        }

        var unavailableIds = new HashSet<string>(unavailableCardIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        client.FetchSupportCards(
            cards =>
            {
                if (generation == loadGeneration)
                {
                    InitializeGrid(new List<SupportCardData>(cards), onItemSelected, unavailableIds, traineeCharacterId);
                }
            },
            error =>
            {
                if (generation == loadGeneration)
                {
                    onError?.Invoke(error);
                }
            });
    }

    public void CancelPendingLoad() => loadGeneration++;

    public void InitializeGrid(
        List<SupportCardData> itemList,
        Action<SupportCardData> onItemSelected,
        ISet<string> unavailableCardIds = null,
        string traineeCharacterId = null)
    {
        onGridCompleteCallback = onItemSelected;

        // Clear any old items before populating
        foreach (Transform child in contentPanel)
        {
            Destroy(child.gameObject);
        }

        // Keep tiers together, then order facilities by the trait display order.
        IEnumerable<SupportCardData> sortedItems = itemList
            .OrderByDescending(item => item.tier)
            .ThenBy(item => GetFacilitySortOrder(item.facility));

        foreach (SupportCardData item in sortedItems)
        {
            GameObject spawned = Instantiate(prefabItem, contentPanel);
            CardGridItem gridItemScript = spawned.GetComponent<CardGridItem>();

            if (gridItemScript != null)
            {
                gridItemScript.Setup(item, OnItemClicked);
                if (string.IsNullOrWhiteSpace(item.CharacterId))
                {
                    Debug.LogError($"Support card {item.id} has no character ID.", this);
                }
                gridItemScript.SetInteractable(
                    !string.IsNullOrWhiteSpace(item.CharacterId)
                    && (unavailableCardIds == null || !unavailableCardIds.Contains(item.id))
                    && !string.Equals(item.CharacterId, traineeCharacterId, StringComparison.Ordinal));
            }
        }
    }

    private static int GetFacilitySortOrder(string facility)
    {
        switch (facility?.Trim().ToLowerInvariant())
        {
            case "power":
                return 0;
            case "agility":
                return 1;
            case "impact":
                return 2;
            case "defense":
                return 3;
            case "finesse":
                return 4;
            default:
                return int.MaxValue;
        }
    }

    private void OnItemClicked(SupportCardData selectedObject)
    {
        // 1. Pass the selected object out to the system that opened this grid
        onGridCompleteCallback?.Invoke(selectedObject);

        // 2. Terminate the grid component
        TerminateGrid();
    }

    private void TerminateGrid()
    {
        // Option A: If this grid is a UI panel/popup, disable the whole UI hierarchy
        // transform.root.gameObject.SetActive(false); 

        // Option B: Just destroy the spawned grid items
        foreach (Transform child in contentPanel)
        {
            Destroy(child.gameObject);
        }

        Debug.Log("Grid component terminated.");
    }
}