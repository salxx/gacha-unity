using System;
using System.Collections;
using UnityEngine;

public static class OverlayImageAnimations
{
    public enum Animation
    {
        Power,
        Agility,
        Impact,
        Defense,
        Finesse,
        Rest,
        Failure
    }

    public static IEnumerator Play(RectTransform image, Animation animation, float distance, Action onStumble = null)
    {
        Vector2 origin = image.anchoredPosition;
        Vector3 originalScale = image.localScale;
        float facing = originalScale.x < 0f ? 1f : -1f;
        float originalAngle = Mathf.DeltaAngle(0f, image.localEulerAngles.z);
        switch (animation)
        {
            case Animation.Power:
                for (int i = 0; i < 2; i++)
                {
                    yield return Move(image, origin + new Vector2(distance * facing, 0f), 0.1f, true);
                    yield return Move(image, origin, 0.2f, false);
                }
                break;
            case Animation.Agility:
                yield return Move(image, origin + new Vector2(distance * 1.1f * facing, 0f), 0.16f, false);
                SetFacing(image, facing < 0f);
                yield return Move(image, origin + new Vector2(-distance * 1.1f * facing, 0f), 0.32f, false);
                image.localScale = originalScale;
                yield return Move(image, origin, 0.16f, false);
                break;
            case Animation.Impact:
                yield return Move(image, origin + new Vector2(-distance * 0.6f * facing, 0f), 0.35f, false);
                yield return Move(image, origin + new Vector2(distance * 1.4f * facing, 0f), 0.08f, true);
                yield return new WaitForSecondsRealtime(0.15f);
                yield return Move(image, origin, 0.25f, false);
                break;
            case Animation.Defense:
                yield return Move(image, origin + new Vector2(-distance * 0.8f * facing, 0f), 0.72f, false);
                yield return Move(image, origin, 0.15f, true);
                break;
            case Animation.Finesse:
                for (int i = 0; i < 12; i++)
                {
                    float direction = i % 2 == 0 ? 1f : -1f;
                    yield return Move(image, origin + new Vector2(direction * distance * 0.12f, 0f), 0.05f, false);
                }
                yield return Move(image, origin, 0.05f, false);
                break;
            case Animation.Rest:
                for (int i = 0; i < 2; i++)
                {
                    yield return Scale(image, new Vector3(originalScale.x * 1.06f,
                        originalScale.y * 1.06f, originalScale.z), 0.48f);
                    yield return Scale(image, originalScale, 0.48f);
                }
                break;
            case Animation.Failure:
                yield return Move(image, origin + new Vector2(-distance * 0.6f * facing, 0f), 0.35f, false);
                onStumble?.Invoke();
                yield return Move(image, origin + new Vector2(distance * 0.3f * facing, 0f), 0.15f, true);
                yield return Tilt(image, origin + new Vector2(distance * 0.6f * facing, -distance * 0.5f),
                    originalAngle - 85f * facing, 0.3f);
                yield return Tilt(image, origin + new Vector2(distance * 0.6f * facing, -distance * 0.4f),
                    originalAngle - 75f * facing, 0.1f);
                yield return Tilt(image, origin + new Vector2(distance * 0.6f * facing, -distance * 0.5f),
                    originalAngle - 85f * facing, 0.1f);
                yield return new WaitForSecondsRealtime(0.4f);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(animation), animation, "Unknown image animation");
        }
    }

    private static void SetFacing(RectTransform image, bool right)
    {
        Vector3 scale = image.localScale;
        scale.x = Mathf.Abs(scale.x) * (right ? -1f : 1f);
        image.localScale = scale;
    }

    private static IEnumerator Move(RectTransform image, Vector2 target, float seconds, bool easeOut)
    {
        Vector2 start = image.anchoredPosition;
        for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            float progress = elapsed / seconds;
            if (easeOut)
            {
                progress = 1f - (1f - progress) * (1f - progress);
            }
            image.anchoredPosition = Vector2.Lerp(start, target, progress);
            yield return null;
        }
        image.anchoredPosition = target;
    }

    private static IEnumerator Scale(RectTransform image, Vector3 target, float seconds)
    {
        Vector3 start = image.localScale;
        for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            image.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, elapsed / seconds));
            yield return null;
        }
        image.localScale = target;
    }

    private static IEnumerator Tilt(RectTransform image, Vector2 target, float targetAngle, float seconds)
    {
        Vector2 start = image.anchoredPosition;
        float startAngle = Mathf.DeltaAngle(0f, image.localEulerAngles.z);
        for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            float progress = elapsed / seconds;
            progress *= progress;
            image.anchoredPosition = Vector2.Lerp(start, target, progress);
            image.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startAngle, targetAngle, progress));
            yield return null;
        }
        image.anchoredPosition = target;
        image.localRotation = Quaternion.Euler(0f, 0f, targetAngle);
    }
}
