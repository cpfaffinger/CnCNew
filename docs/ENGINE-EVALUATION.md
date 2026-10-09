# Engine evaluation

## Decision for the first vertical slice: Stride

Stride is being trialled as the foundation for rendering, windowing, input, audio and editor. The decision is initially secured by a vertical slice and remains revisable, because simulation and file formats must not reference any Stride types.

The local Game Studio MSI step can fail on individual systems with Windows Installer code 1619. The client therefore uses reproducible NuGet packages and does not need Game Studio to build or start. The editor remains an optional tool.

The code-only client integrates the official `Stride.Core.Assets.CompilerApp`. As a result, the standard SDSL shaders of the referenced Stride packages are imported case-sensitively into the runtime database at build time. The real W3D smoke test loads `Art/W3D/ABArFrcCmd.W3D`, produces 21 source meshes and renders them with state-dependent shader/material grouping via Direct3D 11. Alpha test, blend factors, depth compare/write, vertex colours and two-sided culling are set at runtime without Game Studio. The `NU1901`/`NU1903` findings reported by NuGet in transitive build tools of Stride 4.3.0.2507 remain visible and are to be tracked as an upgrade task.

### Reasons

- Open source under the MIT licence.
- Engine and public game API are C#/.NET-centric.
- The current toolchain supports .NET 10 and therefore matches the locally installed SDK version.
- Windows and Direct3D 11 are supported directly; further backends remain possible.
- Scenes, rendering, shaders, audio, input, content pipeline and editor do not have to be developed from scratch.
- The standard C# project structure makes Rider, tests and profiling easier.

### What Stride is not supposed to take over

- deterministic RTS simulation and fixed simulation tick
- Generals INI semantics, object templates and behavior system
- BIG overlay priority and legacy file formats
- pathfinding and skirmish AI compatibility
- mission scripts, campaign state and the later network lockstep

These areas form the identity and compatibility of the game. They remain in engine-independent .NET libraries.

## Alternatives

### Godot

Godot is MIT-licensed, mature and supports Direct3D 12 or a Direct3D 11 compatibility renderer under Windows. C# is supported, but the engine core and the primary extension architecture are not .NET-centric. For this project the boundary between an extensive C# simulation core and Godot's node/resource model would be additional integration effort.

### MonoGame

MonoGame is a lean, proven C# framework and supports .NET 10. However, it deliberately provides considerably less engine and editor functionality. Terrain, scene/asset tools, material system, particles, UI and many production tools would have to be built in-house to a larger extent. It remains a good fallback option in case Stride's content pipeline hinders the direct use of the legacy assets too much.

## Abort criteria for the Stride spike

- W3D meshes and textures cannot be supplied efficiently at runtime or offline.
- Large amounts of terrain and units produce unmanageable CPU/GPU costs.
- Stride's build/content pipeline forces a coupling of the simulation core.
- A reproducible, headless simulation test is prevented by the client integration.

## Vertical slice

1. Window and free RTS camera under Direct3D 11.
2. Display an extracted skirmish heightmap together with a simple texture.
3. Load a W3D model with material and animation.
4. Draw 100–500 simple units from a headless simulation snapshot.
5. Connect selection, move command and deterministic fixed tick.

Only when this spike passes the abort criteria will Stride be declared the permanent client engine.

