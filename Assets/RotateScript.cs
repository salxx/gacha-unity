using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class RotateScript : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 0.5f;
    [SerializeField] private bool invert = false;
    [SerializeField] private Vector3 axis = Vector3.up;

    private Vector2 lastMousePosition;
    private bool isDragging = false;

    public float RotationSpeed
    {
        get => rotationSpeed;
        set => rotationSpeed = value;
    }

    public bool Invert
    {
        get => invert;
        set => invert = value;
    }

    public Vector3 Axis
    {
        get => axis;
        set => axis = value;
    }

    void Update()
    {
        bool isPressed = IsMouseButtonPressed();

        if (isPressed)
        {
            Vector2 currentMousePosition = GetMousePosition();
            if (isDragging)
            {
                Vector2 delta = currentMousePosition - lastMousePosition;
                float direction = invert ? 1f : -1f;
                transform.Rotate(axis, delta.x * rotationSpeed * direction);
            }

            lastMousePosition = currentMousePosition;
            isDragging = true;
        }
        else
        {
            isDragging = false;
        }
    }

    private bool IsMouseButtonPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(0))
        {
            return true;
        }
#endif
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            return true;
        }

        return false;
    }

    private Vector2 GetMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            return Mouse.current.position.ReadValue();
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#endif
        if (Mouse.current != null)
        {
            return Mouse.current.position.ReadValue();
        }

        return Vector2.zero;
    }
}
