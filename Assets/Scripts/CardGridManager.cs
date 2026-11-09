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

    /// <summary>
    /// Call this method to open and initialize the grid with your data list.
    /// </summary>
    public void FetchAndInitializeGrid(
        Action<SupportCardData> onItemSelected,
        IEnumerable<string> unavailableCardIds,
        Action<string> onError = null)
    {
        GameClient client = GameClient.Instance;
        if (client == null)
        {
            onError?.Invoke("Game client is unavailable.");
            return;
        }

        var unavailableIds = new HashSet<string>(unavailableCardIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        client.FetchSupportCards(
            cards => InitializeGrid(new List<SupportCardData>(cards), onItemSelected, unavailableIds),
            onError);
    }

    public void InitializeGrid(
        List<SupportCardData> itemList,
        Action<SupportCardData> onItemSelected,
        ISet<string> unavailableCardIds = null)
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
                gridItemScript.SetInteractable(
                    unavailableCardIds == null || !unavailableCardIds.Contains(item.id));
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