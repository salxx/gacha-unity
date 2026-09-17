# Agent Guidelines & Rules

## Unity Field Serialization & Null Checks
- **No Null Checks for `[SerializeField]` Values**: Assume all `[SerializeField]` fields configured in the Unity Inspector/Editor are always assigned and present at runtime, unless explicitly stated otherwise in the prompt or documentation. Avoid adding redundant defensive null checks (`if (field != null)`) for serialized UI elements, prefabs, canvases, and component references.
