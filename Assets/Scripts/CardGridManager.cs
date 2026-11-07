using UnityEngine;
using System.Collections.Generic;
using System;

public class CardGridManager : MonoBehaviour
{
    [Header("UI Elements")] [SerializeField]
    private GameObject prefabItem; // Your Image + TMPro Prefab

    [SerializeField] private Transform contentPanel; // The GameObject with the GridLayoutGroup

    private Action<SupportCardData> onGridCompleteCallback;

    private void OnEnable()
    {
        FetchAndInitializeGrid((card) =>
        {
            Debug.Log("Card selected: " + card.id);
            gameObject.SetActive(false);
        });
    }

    /// <summary>
    /// Call this method to open and initialize the grid with your data list.
    /// </summary>
    public void FetchAndInitializeGrid(Action<SupportCardData> onItemSelected, Action<string> onError = null)
    {
        GameClient client = GameClient.Instance;
        if (client == null)
        {
            onError?.Invoke("Game client is unavailable.");
            return;
        }

        client.FetchSupportCards(cards => { InitializeGrid(new List<SupportCardData>(cards), onItemSelected); },
            onError);
    }

    public void InitializeGrid(List<SupportCardData> itemList, Action<SupportCardData> onItemSelected)
    {
        onGridCompleteCallback = onItemSelected;

        // Clear any old items before populating
        foreach (Transform child in contentPanel)
        {
            Destroy(child.gameObject);
        }

        // Instantiate and populate the grid
        foreach (SupportCardData item in itemList)
        {
            GameObject spawned = Instantiate(prefabItem, contentPanel);
            CardGridItem gridItemScript = spawned.GetComponent<CardGridItem>();

            if (gridItemScript != null)
            {
                gridItemScript.Setup(item, OnItemClicked);
            }
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