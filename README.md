# BioDome

An interactive 3D smart botanical conservatory built with **Unity 6 (6000.6.9f1)**, **C#** and the **Universal Render Pipeline**.
All geometry, materials, animation, lighting and interaction are created inside Unity.

The Unity project lives in `BioDomeUnity/`. Open that folder from Unity Hub.

## Phase 1 status

Phase 1 delivers the architecture only: folder layout, foundation scripts and an empty procedural
geometry framework. There are no plants, glass or UI layouts yet.

## Architecture

```
                      +--------------------+
   UIManager  <------>|  GreenhouseManager |<------> CameraController
 (events out,         |  (clock + wiring)  |        (PlantPicked event)
  Show* calls in)     +---------+----------+
                                | ticks, in this order each frame
              +-----------------+------------------+
              v                 v                  v
   EnvironmentController  IrrigationController  PlantController (x N)
   (time, season, sun,    (reservoir, sprinkler  (visuals only)
    controls)              logic, spray FX)             |
              |                 |                       v
              |                 |                 PlantSimulation
              +-> EnvironmentConditions (struct) ->   (pure C# maths)
                                                        ^
                                                        |
                                                    PlantData (ScriptableObject)
```

Design rules:

* **One clock.** `GreenhouseManager` converts real time to simulated hours and passes the same
  `deltaHours` to every system. Pausing or changing speed is one variable.
* **Simulation is separate from presentation.** `PlantSimulation` is a plain C# class with no
  `UnityEngine` object dependencies. `PlantController` shows its results (scale, tint).
* **Data is in ScriptableObjects.** `PlantData` (per species) and `ClimateProfile` (per season).
* **No singletons.** References are serialized in the Inspector; `GreenhouseManager` is the only
  script that knows all the others.
* **Systems communicate through events and one data struct**, never through each other's fields.
* **No legacy input.** The project has the old Input Manager disabled, so `CameraController` uses the new Input System.

## Folder responsibilities (`BioDomeUnity/Assets/`)

| Folder | Purpose |
|---|---|
| `Art/Models` | Meshes saved from procedural generators (if you choose to bake any) |
| `Art/Materials` | Materials (URP Lit, glass, soil, water) |
| `Art/Textures` | Textures and normal maps |
| `Art/Procedural` | Assets produced by procedural tools (baked meshes, generated textures) |
| `Art/Animations` | Animator Controllers and animation clips |
| `Prefabs/Environment` `Plants` `Irrigation` `Effects` `UI` | Prefabs grouped by system |
| `Scenes` | Scenes (`SampleScene` is the template default) |
| `Scripts/Core` | Shared enums, data structs, `GreenhouseManager` |
| `Scripts/Environment` | Time, season, sun, climate |
| `Scripts/Plants` | Plant data asset and plant scene component |
| `Scripts/Simulation` | Pure simulation maths |
| `Scripts/Irrigation` | Water reservoir and sprinklers |
| `Scripts/Camera` | Orbit camera and plant picking |
| `Scripts/UI` | TextMeshPro / uGUI presentation |
| `Scripts/Effects` | Reserved for particle and visual effect scripts (empty) |
| `Scripts/Procedural` | Reusable procedural geometry framework |
| `ScriptableObjects/Plants` | `PlantData` assets |
| `ScriptableObjects/Environment` | `ClimateProfile` assets |
| `ScriptableObjects/Settings` | Future global settings assets |
| `Fonts` | TextMeshPro font assets |
| `Editor` | Editor-only scripts (custom inspectors, tools) |

`Assets/Settings`, `Assets/TutorialInfo`, `Assets/Readme.asset` and `Assets/InputSystem_Actions.inputactions` come from the Unity URP template and are left untouched.
Folders that are empty hold a `.gitkeep` file so Git keeps them; Unity ignores it.

## Scripts

Namespaces follow the folders (`BioDome.Core`, `BioDome.Plants`, ...). The camera namespace is
`BioDome.CameraSystem` so it does not hide `UnityEngine.Camera`.

| Script | Type | Responsibility |
|---|---|---|
| `Core/Season.cs` | enum | Spring, Summer, Autumn, Winter, plus `Next()` |
| `Core/PlantHealthState.cs` | enum | Healthy, Stressed, Critical, Dead |
| `Core/EnvironmentConditions.cs` | struct | Snapshot (temperature, humidity, light, season, time, day) passed to the simulation |
| `Core/GreenhouseManager.cs` | MonoBehaviour | Simulation clock, ticks systems in order, registers plants, wires UI/camera events, plant selection |
| `Environment/EnvironmentController.cs` | MonoBehaviour | Time of day, day count, seasons, heating/humidity/shade controls, sun light |
| `Environment/ClimateProfile.cs` | ScriptableObject | Per-season temperature, humidity, sunrise/sunset, peak light |
| `Plants/PlantData.cs` | ScriptableObject | Species configuration: ideal ranges, water use, growth time, health thresholds |
| `Plants/PlantController.cs` | MonoBehaviour | Owns a `PlantSimulation`; scales the plant by growth, tints by health, raises `HealthStateChanged` |
| `Simulation/PlantSimulation.cs` | plain class | Water use, stress, health and growth calculations |
| `Irrigation/IrrigationController.cs` | MonoBehaviour | Reservoir, Off/Manual/Auto watering with hysteresis, spray particle systems |
| `Camera/CameraController.cs` | MonoBehaviour | Orbit, pan, zoom, smooth focus, click-to-pick plants |
| `UI/UIManager.cs` | MonoBehaviour | Shows data, turns widget input into events. Knows no controllers |
| `Procedural/MeshData.cs` | plain class | CPU mesh buffer: vertices, submeshes, `Append`, `ApplyTo(Mesh)` |
| `Procedural/ProceduralMeshGenerator.cs` | abstract ScriptableObject | Base for seed-deterministic geometry recipes |
| `Procedural/MeshPrimitives.cs` | static class | Shared shapes (currently a cylinder) |
| `Procedural/CylinderMeshGenerator.cs` | ScriptableObject | First concrete generator; a smoke test of the pipeline |
| `Procedural/ProceduralMeshBuilder.cs` | MonoBehaviour | Runs a generator onto a `MeshFilter`, owns and frees the Mesh |

### Procedural geometry

```
ProceduralMeshGenerator (ScriptableObject recipe + seed)
        |  Build(MeshData, System.Random)
        v
MeshPrimitives / your code  ->  MeshData (CPU buffer, parts combined with Append)
        |  ApplyTo(Mesh)
        v
ProceduralMeshBuilder (MonoBehaviour) -> MeshFilter
```

To add a generator (stem, leaf, branch, pot, glass pane): subclass `ProceduralMeshGenerator`,
implement `Build`, use only the supplied `System.Random`, and add reusable shapes to `MeshPrimitives`.
Plants will be assembled by appending generator output (stem + leaves + flowers) into one `MeshData`.

## How systems communicate

| From | To | How |
|---|---|---|
| `GreenhouseManager` | all systems | Direct calls: `Advance`, `Tick(deltaHours, ...)` |
| `EnvironmentController` | simulation | `EnvironmentConditions` struct passed by the manager |
| `EnvironmentController` | anyone | Events `SeasonChanged`, `ConditionsChanged` |
| `UIManager` | `GreenhouseManager` | Events (`TemperatureOffsetRequested`, `SeasonRequested`, ...) |
| `GreenhouseManager` | `UIManager` | `ShowEnvironment`, `ShowIrrigation`, `ShowPlant`, `SyncControls` |
| `CameraController` | `GreenhouseManager` | Event `PlantPicked` |
| `IrrigationController` | `PlantController` | `ReceiveWater(litres)` |
| `PlantController` | anyone | Event `HealthStateChanged` |

## Setting up a scene in Unity

1. **Create assets** (Project window > right-click > Create > BioDome):
   * `Climate Profile` -> save in `ScriptableObjects/Environment`.
   * `Plant Data` -> save in `ScriptableObjects/Plants`. Defaults are playable.
2. **Create an empty GameObject `Greenhouse`.** Add `GreenhouseManager`, `EnvironmentController`
   and `IrrigationController` (they can be on one object or separate ones).
   * `EnvironmentController`: assign the Climate Profile; optionally assign the scene's Directional Light as *Sun Light*.
   * `IrrigationController`: optionally assign particle systems to *Spray Effects*.
3. **Camera:** add `CameraController` to the Main Camera.
4. **Plants:** create a GameObject (any mesh for now, e.g. a Capsule), add a **Collider** and
   `PlantController`, assign a Plant Data asset. Optionally set *Visual Root* (defaults to itself) and
   *Tint Renderers*. Make it a prefab in `Prefabs/Plants`.
5. **UI:** create a Canvas with TextMeshPro texts, Sliders, Toggles, a Dropdown and Buttons. Add `UIManager`
   to the Canvas and assign what you have; every field is optional.
   * Temperature offset slider: -10 to 10. Humidity offset slider: -0.3 to 0.3. Shade slider: 0 to 1.
   * Speed slider: about 0.1 to 4 (simulated hours per real second).
   * Season dropdown options in order: Spring, Summer, Autumn, Winter.
   * The Canvas needs an `EventSystem` using **Input System UI Input Module** (Unity offers a one-click replacement
     of the default module, because only the new Input System is enabled).
6. **Wire the manager:** on `GreenhouseManager` assign Environment (required), Irrigation, Camera Controller and UI Manager.
   Leave *Auto Discover Plants* on, or fill the *Plants* list.
7. Press Play. Right-drag orbits, middle-drag pans, scroll zooms, left-click selects a plant.

## Git notes

* `.gitignore` excludes `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Obj/`, builds and generated `*.csproj/*.sln/*.slnx`.
* `.gitattributes` stores textures, audio and model files with Git LFS.
* Always commit `.meta` files together with their assets.
