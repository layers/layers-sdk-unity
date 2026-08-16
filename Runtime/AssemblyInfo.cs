// AssemblyInfo.cs
// Layers Unity SDK
//
// Grants the test assemblies access to this assembly's `internal` types
// (MockPlatform, ILayersPlatform, JsonHelper, ExceptionModule, etc.) so
// EditMode tests can exercise SDK internals without making them part of
// the public API surface.
//
// NOTE: a JSON "internalsVisibleTo" key previously sat in
// Layers.Unity.asmdef. That key is not part of Unity's asmdef schema —
// Unity's asmdef reader silently ignores unrecognized fields — so it never
// generated an actual [InternalsVisibleTo] attribute. This file is the real
// mechanism; the asmdef field has been removed.
//
// Names below MUST exactly match the "name" field of the corresponding
// test asmdef (Tests/Runtime/Layers.Unity.Tests.Runtime.asmdef and
// Tests/Editor/Layers.Unity.Tests.Editor.asmdef).

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Layers.Unity.Tests.Runtime")]
[assembly: InternalsVisibleTo("Layers.Unity.Tests.Editor")]
