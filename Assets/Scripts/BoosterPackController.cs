using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the booster pack unpacking presentation for support card gacha pulls.
/// Handles sliding into scene, a sneak peek animation of cards, sliding to the side,
/// and drawing cards one by one to the center for reveal.
/// </summary>
public class BoosterPackController : MonoBehaviour
{
    [Header("Booster References")]
    [SerializeField] private RectTransform boosterRoot;
    [SerializeField] private RectTransform boosterCover;
    [SerializeField] private RectTransform[] cardTransforms;
    [SerializeField] private Image[] cardImages;

    [Header("Animation Durations")]
    [SerializeField] private float slideInDuration = 0.6f;
    [SerializeField] private float sneakPeekDuration = 0.5f;
    [SerializeField] private float sneakPeekHoldDuration = 0.8f;
    [SerializeField] private float sneakPeekReturnDuration = 0.4f;
    [SerializeField] private float slideLeftDuration = 0.5f;
    [SerializeField] private float cardExitDuration = 0.3f;
    [SerializeField] private float cardDrawDuration = 0.45f;

    [Header("Positions")]
    [SerializeField] private Vector2 offscreenRightPosition = new Vector2(1200f, 0f);
    [SerializeField] private Vector2 centerPosition = Vector2.zero;
    [SerializeField] private Vector2 leftPosition = new Vector2(-400f, 0f);
    [SerializeField] private float leftRotation = -20f;
    [SerializeField] private Vector2 cardExitOffset = new Vector2(0f, 120f);
    [SerializeField] private Vector2 drawnCardCenterOffset = new Vector2(400f, 0f);
    [SerializeField] private float hiddenCardY = -20f;
    [SerializeField] private Vector3 drawnCardScale = new Vector3(1.25f, 1.25f, 1f);

    private int lastDrawnIndex = -1;
    private int activeCount;
    private float[] sneakPeekYPositions;

    private void Awake()
    {
        BindCardChildren();
        InitializePeekPositions();
    }

    private void BindCardChildren()
    {
        List<RectTransform> discoveredTransforms = new List<RectTransform>();
        List<Image> discoveredImages = new List<Image>();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith("Card"))
            {
                continue;
            }

            RectTransform cardTransform = child.GetComponent<RectTransform>();
            Image cardImage = child.GetComponentInChildren<Image>(true);
            if (cardTransform == null || cardImage == null)
            {
                Debug.LogWarning($"Booster card '{child.name}' needs a RectTransform and a child Image.", child);
                continue;
            }

            discoveredTransforms.Add(cardTransform);
            discoveredImages.Add(cardImage);
        }

        if (discoveredTransforms.Count > 0)
        {
            cardTransforms = discoveredTransforms.ToArray();
            cardImages = discoveredImages.ToArray();
        }
    }

    private void InitializePeekPositions()
    {
        if (sneakPeekYPositions == null && cardTransforms != null)
        {
            sneakPeekYPositions = new float[cardTransforms.Length];
            for (int i = 0; i < cardTransforms.Length; i++)
            {
                sneakPeekYPositions[i] = (cardTransforms.Length - 1 - i) * 5f;
            }
        }
    }

    public void SetupPulls(GachaReward[] rewards)
    {
        BindCardChildren();
        InitializePeekPositions();
        activeCount = Mathf.Min(rewards.Length, cardTransforms.Length);
        lastDrawnIndex = -1;

        for (int i = 0; i < cardTransforms.Length; i++)
        {
            if (i < activeCount)
            {
                cardTransforms[i].SetParent(boosterRoot, false);
                GachaReward reward = rewards[i];
                Sprite cardSprite = Resources.Load<Sprite>($"Images/Cards/{reward.id}");
                if (cardSprite == null)
                {
                    string spriteName = reward.Tier switch
                    {
                        1 => "common",
                        2 => "rare",
                        3 => "super-rare",
                        _ => "common"
                    };
                    cardSprite = Resources.Load<Sprite>($"Images/Cards/{spriteName}");
                }

                cardImages[i].sprite = cardSprite;
                cardImages[i].preserveAspect = true;
                Vector3 localPosition = cardTransforms[i].localPosition;
                cardTransforms[i].localPosition = new Vector3(localPosition.x, -10f, localPosition.z);
                cardTransforms[i].localScale = Vector3.one;
                cardTransforms[i].gameObject.SetActive(true);
            }
            else
            {
                cardTransforms[i].gameObject.SetActive(false);
            }
        }

        boosterCover.SetAsLastSibling();
        boosterRoot.anchoredPosition = offscreenRightPosition;
        gameObject.SetActive(true);
    }

    public IEnumerator PlayIntroRoutine(int cardCount)
    {
        activeCount = Mathf.Min(cardCount, cardTransforms.Length);

        // Phase 1: Slide into scene
        Vector2 startPos = offscreenRightPosition;
        for (float elapsed = 0f; elapsed < slideInDuration; elapsed += Time.deltaTime)
        {
            float t = EaseOutQuad(elapsed / slideInDuration);
            boosterRoot.anchoredPosition = Vector2.Lerp(startPos, centerPosition, t);
            yield return null;
        }
        boosterRoot.anchoredPosition = centerPosition;

        yield return new WaitForSeconds(0.1f);

        // Phase 2: Sneak peek pull out
        for (float elapsed = 0f; elapsed < sneakPeekDuration; elapsed += Time.deltaTime)
        {
            float t = EaseInOutQuad(elapsed / sneakPeekDuration);
            for (int i = 0; i < activeCount; i++)
            {
                float targetY = (activeCount - 1 - i) * 5f;
                Vector3 hiddenPosition = cardTransforms[i].localPosition;
                cardTransforms[i].localPosition = new Vector3(hiddenPosition.x,
                    Mathf.Lerp(-10f, targetY, t), hiddenPosition.z);
            }
            yield return null;
        }

        for (int i = 0; i < activeCount; i++)
        {
            float targetY = (activeCount - 1 - i) * 5f;
            Vector3 localPosition = cardTransforms[i].localPosition;
            cardTransforms[i].localPosition = new Vector3(localPosition.x, targetY, localPosition.z);
        }

        // Hold peek
        yield return new WaitForSeconds(sneakPeekHoldDuration);

        // Sneak peek return into booster
        for (float elapsed = 0f; elapsed < sneakPeekReturnDuration; elapsed += Time.deltaTime)
        {
            float t = EaseInOutQuad(elapsed / sneakPeekReturnDuration);
            for (int i = 0; i < activeCount; i++)
            {
                float startY = (activeCount - 1 - i) * 5f;
                Vector3 hiddenPosition = cardTransforms[i].localPosition;
                cardTransforms[i].localPosition = new Vector3(hiddenPosition.x,
                    Mathf.Lerp(startY, -10f, t), hiddenPosition.z);
            }
            yield return null;
        }

        for (int i = 0; i < activeCount; i++)
        {
            Vector3 localPosition = cardTransforms[i].localPosition;
            cardTransforms[i].localPosition = new Vector3(localPosition.x, -10f, localPosition.z);
        }

        yield return new WaitForSeconds(0.1f);

        // Phase 3: Slide booster to left
        Vector2 fromPos = centerPosition;
        for (float elapsed = 0f; elapsed < slideLeftDuration; elapsed += Time.deltaTime)
        {
            float t = EaseInOutQuad(elapsed / slideLeftDuration);
            boosterRoot.anchoredPosition = Vector2.Lerp(fromPos, leftPosition, t);
            boosterRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, leftRotation, t));
            yield return null;
        }
        boosterRoot.anchoredPosition = leftPosition;
        boosterRoot.localRotation = Quaternion.Euler(0f, 0f, leftRotation);

        yield return new WaitForSeconds(0.1f);
    }

    public IEnumerator DrawCardRoutine(int cardIndex)
    {
        if (cardIndex < 0 || cardIndex >= cardTransforms.Length)
        {
            yield break;
        }

        // Hide previous card if one was shown
        if (lastDrawnIndex >= 0 && lastDrawnIndex < cardTransforms.Length && lastDrawnIndex != cardIndex)
        {
            cardTransforms[lastDrawnIndex].gameObject.SetActive(false);
        }

        RectTransform card = cardTransforms[cardIndex];
        card.gameObject.SetActive(true);
        boosterCover.SetAsLastSibling();

        Vector3 localPosition = card.localPosition;
        Vector2 startPos = new Vector2(localPosition.x, -10f);
        float exitDistance = card.rect.height + 10f;
        Vector2 exitPos = new Vector2(localPosition.x, startPos.y + exitDistance);
        Vector2 targetPos = drawnCardCenterOffset;

        for (float elapsed = 0f; elapsed < cardExitDuration; elapsed += Time.deltaTime)
        {
            float t = elapsed / cardExitDuration;
            float ease = EaseOutQuad(t);
            Vector2 position = Vector2.Lerp(startPos, exitPos, ease);
            card.localPosition = new Vector3(position.x, position.y, localPosition.z);

            yield return null;
        }

        card.localPosition = new Vector3(exitPos.x, exitPos.y, localPosition.z);
        Transform revealParent = boosterRoot.parent;
        card.SetParent(revealParent, true);
        card.localRotation = Quaternion.identity;
        Vector3 detachedPosition = card.localPosition;
        Vector2 centerTarget = Vector2.zero;

        for (float elapsed = 0f; elapsed < cardDrawDuration; elapsed += Time.deltaTime)
        {
            float t = elapsed / cardDrawDuration;
            float ease = EaseOutQuad(t);
            float overshoot = 1f + 0.15f * Mathf.Sin(t * Mathf.PI);

            Vector2 position = Vector2.Lerp(new Vector2(detachedPosition.x, detachedPosition.y), centerTarget, ease);
            card.localPosition = new Vector3(position.x, position.y, detachedPosition.z);
            card.localRotation = Quaternion.identity;
            card.localScale = Vector3.Lerp(Vector3.one, drawnCardScale, ease) * overshoot;

            yield return null;
        }

        card.localPosition = new Vector3(centerTarget.x, centerTarget.y, 0f);
        card.localRotation = Quaternion.identity;
        card.localScale = drawnCardScale;
        lastDrawnIndex = cardIndex;
    }

    public void ShowCardAtCenter(int cardIndex)
    {
        if (cardIndex < 0 || cardIndex >= cardTransforms.Length)
        {
            return;
        }

        for (int i = 0; i < cardTransforms.Length; i++)
        {
            if (i == cardIndex)
            {
                cardTransforms[i].SetParent(boosterRoot.parent, false);
                cardTransforms[i].gameObject.SetActive(true);
                cardTransforms[i].SetAsLastSibling();
                cardTransforms[i].anchoredPosition = Vector2.zero;
                cardTransforms[i].localRotation = Quaternion.identity;
                cardTransforms[i].localScale = drawnCardScale;
            }
            else
            {
                cardTransforms[i].gameObject.SetActive(false);
            }
        }

        lastDrawnIndex = cardIndex;
    }

    public void ResetAndHide()
    {
        StopAllCoroutines();

        for (int i = 0; i < cardTransforms.Length; i++)
        {
            cardTransforms[i].SetParent(boosterRoot, false);
            Vector3 localPosition = cardTransforms[i].localPosition;
            cardTransforms[i].localPosition = new Vector3(localPosition.x, -10f, localPosition.z);
            cardTransforms[i].localRotation = Quaternion.identity;
            cardTransforms[i].localScale = Vector3.one;
            cardTransforms[i].gameObject.SetActive(false);
        }

        boosterCover.SetAsLastSibling();
        boosterRoot.anchoredPosition = offscreenRightPosition;
        boosterRoot.localRotation = Quaternion.identity;
        lastDrawnIndex = -1;
        gameObject.SetActive(false);
    }

    private static float EaseOutQuad(float t)
    {
        return 1f - (1f - t) * (1f - t);
    }

    private static float EaseInOutQuad(float t)
    {
        return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
    }
}
