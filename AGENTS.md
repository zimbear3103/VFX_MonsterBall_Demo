# Repository Guidelines

## Project Structure & Asset Organization

This is a Unity 6 project; open the repository root with Unity `6000.6.0f1`. Main runtime code lives in `Assets/Scripts/`, grouped by feature (`Character`, `Combat`, `Spawn`, `UI`, and others). Custom inspectors, authoring tools, and validators are under `Assets/Scripts/Editor/`. Scenes are in `Assets/Scenes/`, and URP configuration is in `Assets/Settings/`. The older grouped content remains under `Assets/Game/` (scripts, prefabs, resources, and rendering); VFX content is under `Assets/GamePlay/Effects/`, while imported packages and art live in `Assets/ThirdParty/` and the other asset folders. Add new work beside its owning feature. Keep each Unity `.meta` file with its asset so GUID references remain stable.

## Build, Test, and Development

- Open the repository root in Unity `6000.6.0f1` to edit and run the project.
- Use **File > Build Profiles** to select a target and build. Check the active scene and target before building.
- Run affected scenes in Play Mode and check the Console for compile errors and runtime exceptions.
- Run available EditMode or PlayMode tests from **Window > General > Test Runner**. For visual changes, inspect the result in the Game view and on the affected prefab or scene.

## Coding Style & Naming

Use four spaces for C# indentation and follow the surrounding file's structure. Use `PascalCase` for types and methods, `m_camelCase` for private fields, and `camelCase` for public fields. Mark Inspector-tunable private fields with `[SerializeField] private`. Keep code comments in English; use `#region` only when it helps group a long file. Avoid renaming serialized fields without updating their scene and prefab references.

## Commits and Pull Requests

Use `feature/<short-name>` for feature branches. Commit subjects have no fixed prefix; keep them short, descriptive, and focused, for example `Tune impact flash timing`. Pull requests should explain the change, list affected scenes or assets, and include the Unity validation performed. Add a screenshot or short capture for visual changes and link a related issue when one exists.
