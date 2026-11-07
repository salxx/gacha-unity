using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Rotates the object around an axis while the left mouse button is dragged horizontally.
/// </summary>
public class RotateScript : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 0.5f;
    [SerializeField] private bool invert = false;
    [SerializeField] private Vector3 axis = Vector3.up;

    private Vector2 lastMousePosition;
    private bool isDragging;

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.isPressed)
        {
            isDragging = false;
            return;
        }

        Vector2 mousePosition = mouse.position.ReadValue();
        if (isDragging)
        {
            float direction = invert ? 1f : -1f;
            transform.Rotate(axis, (mousePosition.x - lastMousePosition.x) * rotationSpeed * direction);
        }

        lastMousePosition = mousePosition;
        isDragging = true;
    }
}
