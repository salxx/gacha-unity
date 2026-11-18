using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class BoosterPackSetup
{
    [MenuItem("Gacha/Setup Booster Pack In Scenes")]
    public static void Setup()
    {
        // 1. Open EmptyScene to get FullBooster
        var emptyScene = EditorSceneManager.OpenScene("Assets/Scenes/EmptyScene.unity", OpenSceneMode.Single);
        GameObject fullBooster = GameObject.Find("FullBooster");
        if (fullBooster == null)
        {
            Debug.LogError("Could not find FullBooster in EmptyScene.");
            return;
        }

        // Add / configure BoosterPackController on FullBooster in EmptyScene
        ConfigureBoosterController(fullBooster);
        EditorSceneManager.SaveScene(emptyScene);

        // 2. Save FullBooster as a Prefab
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Prefabs");
        }

        string prefabPath = "Assets/Resources/Prefabs/FullBooster.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(fullBooster, prefabPath, InteractionMode.AutomatedAction);
        Debug.Log($"Saved FullBooster prefab at {prefabPath}");

        // 3. Open GachaScene and add FullBooster
        var gachaScene = EditorSceneManager.OpenScene("Assets/Scenes/GachaScene.unity", OpenSceneMode.Single);
        GameObject gachaCanvas = GameObject.Find("GachaCanvas");
        if (gachaCanvas == null)
        {
            Debug.LogError("Could not find GachaCanvas in GachaScene.");
            return;
        }

        // Check if FullBooster already exists in GachaCanvas
        Transform existing = gachaCanvas.transform.Find("FullBooster");
        GameObject gachaFullBooster;
        if (existing == null)
        {
            gachaFullBooster = (GameObject)PrefabUtility.InstantiatePrefab(prefab, gachaCanvas.transform);
            gachaFullBooster.name = "FullBooster";
        }
        else
        {
            gachaFullBooster = existing.gameObject;
        }

        ConfigureBoosterController(gachaFullBooster);

        // 4. Connect to GachaItemSpawner
        GachaItemSpawner spawner = Object.FindFirstObjectByType<GachaItemSpawner>();
        if (spawner != null)
        {
            SerializedObject serializedSpawner = new SerializedObject(spawner);
            SerializedProperty boosterProp = serializedSpawner.FindProperty("boosterPack");
            if (boosterProp != null)
            {
                boosterProp.objectReferenceValue = gachaFullBooster.GetComponent<BoosterPackController>();
                serializedSpawner.ApplyModifiedProperties();
                EditorUtility.SetDirty(spawner);
            }
        }

        EditorSceneManager.SaveScene(gachaScene);
        AssetDatabase.SaveAssets();
        Debug.Log("Successfully setup FullBooster in GachaScene and wired up GachaItemSpawner.");
    }

    private static void ConfigureBoosterController(GameObject boosterObj)
    {
        BoosterPackController controller = boosterObj.GetComponent<BoosterPackController>();
        if (controller == null)
        {
            controller = boosterObj.AddComponent<BoosterPackController>();
        }

        RectTransform rootRect = boosterObj.GetComponent<RectTransform>();
        Transform boosterChild = boosterObj.transform.Find("Booster");
        RectTransform coverRect = boosterChild != null ? boosterChild.GetComponent<RectTransform>() : null;

        List<RectTransform> cardRects = new List<RectTransform>();
        List<Image> cardImgs = new List<Image>();

        for (int i = 0; i < boosterObj.transform.childCount; i++)
        {
            Transform child = boosterObj.transform.GetChild(i);
            if (child.name.StartsWith("Card"))
            {
                cardRects.Add(child.GetComponent<RectTransform>());
                cardImgs.Add(child.GetComponent<Image>());
            }
        }

        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("boosterRoot").objectReferenceValue = rootRect;
        so.FindProperty("boosterCover").objectReferenceValue = coverRect;

        SerializedProperty cardTransProp = so.FindProperty("cardTransforms");
        cardTransProp.arraySize = cardRects.Count;
        for (int i = 0; i < cardRects.Count; i++)
        {
            cardTransProp.GetArrayElementAtIndex(i).objectReferenceValue = cardRects[i];
        }

        SerializedProperty cardImgsProp = so.FindProperty("cardImages");
        cardImgsProp.arraySize = cardImgs.Count;
        for (int i = 0; i < cardImgs.Count; i++)
        {
            cardImgsProp.GetArrayElementAtIndex(i).objectReferenceValue = cardImgs[i];
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(controller);
    }
}
