# W3D material contract

As of: 27 September 2026

## Renderer-independent semantics

`CnCNew.W3D` preserves the order of all material passes and texture stages. The render plan groups
a triangle by the complete state of all passes; a later pass is therefore not sorted as an
independent batch. W3D defines at most two texture stages per pass. More than two
stages are structurally invalid and are rejected during decoding with a mesh diagnostic, not only
in the client with a `NotSupportedException`.

Shader values are typed enums. Supported and tested are all W3D values for depth compare,
depth write, alpha test, source/destination blend, primary/secondary gradient, detail color,
detail alpha, post-detail color and post-detail alpha. On loading, the Generals PC loader copies the
old `Detail*` fields into its active post-detail state and ignores the serialised
`PostDetail*` bytes; CnCNew preserves both losslessly and uses the same effective rule.

The file bytes `ColorMask`, `FogFunc`, `ShaderPreset` and padding were reused by the tools as
PlayStation 2 parameters A to D. The EA PC loader always turns color writing on and
file fog always off. CnCNew therefore does not wrongly map the frequent raw values `0/2/2/2` as
PC render state. The raw values remain available as `W3dLegacyShaderMetadata`.

DCG, DIG and SCG are validated separately. DCG and vertex material diffuse/opacity form the
primary color stream; SCG forms the secondary gradient. Unknown shader values never fall back
silently to a default value.

## Mappers

The shipped content uses:

- UV;
- Environment (camera-space reflection vector);
- Classic/Cheap Environment (camera-space normal);
- Linear Offset with `UPerSec`, `VPerSec`, `UOffset`, `VOffset`, `UScale`, `VScale`, `ClampFix`;
- Grid with `FPS`, `Log2Width` or the shipped spelling `Log1Width`, `Last`
  and `Offset`.

Mapper kind and slot come from the vertex material attribute bits. Arguments are typed invariantly and
strictly; unknown, duplicate or invalid values produce a diagnostic. Linear Offset
adopts the historical negative time direction of the EA renderer. Positive and negative
speeds, wrap/clamp, grid frames and both slots are tested automatically.

Valid mapper kinds that are not used in the content (among others Rotate, Sine, Step, ZigZag,
Screen, world-space and bump variants) are rejected with an exact diagnostic. Likewise the
primary gradient modes Bump Environment and Bump Environment Luminance are typed, but need
a tangent-space bump path and are deliberately diagnosed hard outside the audited
content. Add and Modulate2X are implemented and tested for their CPU semantics.

## Stride mapping

A combined W3D batch becomes one Stride material with one Stride pass per W3D pass.
The pass order is preserved. Four UV streams (`pass * 2 + stage`) and four color streams
(primary/secondary per pass) prevent a later pass from inheriting UVs or vertex colors of the first
pass. Blend, depth compare, depth write, alpha test and culling are set per pass.
Animated linear/grid mappers update dynamic vertex buffers from the explicit render time.

Textures are looked up through the case-insensitive runtime content file system. Candidates are the
given virtual reference, `Art/Textures/<Datei>` and DDS and TGA files of the same name.
If a texture is missing, an exact diagnostic and a visible magenta checkerboard appear; the
smoke test additionally fails. Loaded textures are cached by path and released deterministically
together with vertex/index buffers.

## Full-content acceptance

```powershell
dotnet run --project tests/CnCNew.W3D.Tests -- --content-dir .local/content
dotnet run --project tools/CnCNew.W3dTool -- audit .local/content
```

The audit processes 8,904/8,904 files, 70,587 meshes, 76,212 material passes, 74,374
texture stages, 77,672 shaders and 80,041 vertex materials. There are 64,962 single-pass and 5,625
two-pass meshes; per pass, no stage occurs 2,155 times, one stage 73,740 times and two stages 317 times.
The content contains 113,824 DCG sets; DIG and SCG do not occur in the shipped files,
their decoder and render path is therefore covered by synthetic regression tests.
Mapper distribution: stage 0 contains 77,389 UV, 2,397 Environment, 253 Linear and two
Grid mappers; stage 1 contains 79,933 UV and 108 Linear mappers.

## Visual regression

Captures live exclusively in the ignored directory `.local/visual-verification`; neither
original assets nor images rendered from them are committed. In smoke mode the capture time uses
a fixed 60 Hz time step. Two independent runs of the animated model were
bit-identical.

| Path | Model | SHA-256 |
| --- | --- | --- |
| Multi-pass, alpha test, additive, environment | `ABArFrcCmd.W3D` | `9FC7420083E96542CD96ADBCACDF5BD310EC2426E3B389F4144FB20C570E8C22` |
| Two stages, detail function | `AbbtCmdHQA.W3D` | `D40C4B03135A5AB5D950E7E81AE7E66874EE45FE31C4070B1D82D43E7A079ECC` |
| Animated mapper in stage 1 | `AVSoloDrn.W3D` | `A24DBFEC49658F1ECCDBE99B5D0346F45FA56CD9AF5FE05EC839CE49BFCE7E90` |
| Grid mapper | `ExEMPSphere.W3D` | `A6EB428C26002075AC311654985F98597674662DD84E3A3D989F46C6EA35DFD2` |

Example:

```powershell
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test `
  --content-dir .local/content --model-path Art/W3D/AVSoloDrn.W3D `
  --capture .local/visual-verification/animated-stage1.png
```
