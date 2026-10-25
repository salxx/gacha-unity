# Agent Guidelines & Rules

## Project Scope & Responsibilities
- **Full-Stack Management (Frontend & Backend)**: Handle both Frontend (FE / Unity project in `C:\Users\Alex\Gacha`) and Backend (BE / Python project in `C:\Users\Alex\gacha-BE`).

## Project Builds
- **Do Not Build the Project**: Never run build commands or the build tool on this project, as Unity automatically compiles and builds the project.

## Unity Scene Wiring
- **Singletons over Inspector links**: Scene-wide managers (`GameClient`, `GachaItemSpawner`) derive from `Singleton<T>` and register in `Awake`. Other components fetch them via `X.Instance` in `Start()` instead of `[SerializeField]` references.
- **Keep serialized field names stable**: Scripts are attached to GameObjects in `SampleScene`; renaming or removing a `[SerializeField]` field loses its Inspector value. Add `[FormerlySerializedAs]` when a rename is unavoidable, and never rename MonoBehaviour classes/files.
- **Backend is authoritative**: Odds, guarantees and token balances live in `gacha-BE`; the client only displays them. Keep the API contract in `DESIGN_DOCUMENT.md` §5 and `gacha-BE/PROJECT_HANDOFF.md` in sync when changing either side.

## Unity Field Serialization & Null Checks
- **No Null Checks for `[SerializeField]` Values**: Assume all `[SerializeField]` fields configured in the Unity Inspector/Editor are always assigned and present at runtime, unless explicitly stated otherwise in the prompt or documentation. Avoid adding redundant defensive null checks (`if (field != null)`) for serialized UI elements, prefabs, canvases, and component references.
