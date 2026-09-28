# Direct3D stage shaders

`PianoPbr.hlsl` is the first real GPU shader asset for the planned 3D stage renderer. It uses Direct3D Shader Model 5.0 vertex and pixel stages, tangent-free world-space normal lighting, Cook–Torrance GGX BRDF, directional and point lights, emissive material response, and ACES tone mapping.

The current WPF `PianoStage` still owns the live renderer. This source is not yet wired into the application: the next migration step is the Direct3D 11 swap-chain host and mesh/material pipeline. Do not describe the stage as GPU-rendered until that host compiles and runs the shader.
