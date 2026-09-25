# Quiescent.Core Migration Status

Fork of CmlLib.Core (+ auth & installer satellites) renamed and restructured for
NativeAOT-readiness. Owned by the Yorii Launcher project.

## Layout

| Project | Forked from | Notes |
|---|---|---|
| `Quiescent.Core.Commons` | CmlLib.Core.Commons | now hosts `Serialization/QuiescentJson` facade |
| `Quiescent.Core` | CmlLib.Core 4.0.6 | net8.0 |
| `Quiescent.XboxAuthNet` | XboxAuthNet 3.x | multi-target: `net8.0` (AOT-clean) / `net8.0-windows` (legacy WinForms+WebView2 broker for interactive login parity) |
| `Quiescent.XboxAuthNet.Game` | XboxAuthNet.Game 1.4.x | net8.0 |
| `Quiescent.Core.Auth.Microsoft` | CmlLib.Core.Auth.Microsoft 3.3.x | net8.0 |
| `Quiescent.Core.Installer.Forge` | CmlLib.Core.Installer.Forge 1.1.1 | net8.0 |
| `Quiescent.Core.Installer.NeoForge` | CmlLib.Core.Installer.NeoForge 4.0.1 (Gml-Launcher fork) | net8.0 |
| `Quiescent.AotSmokeTest` | new | PublishAot=true harness, 3/3 passing |

Namespace mapping: `CmlLib.* -> Quiescent.Core.*`, `XboxAuthNet* -> Quiescent.XboxAuthNet*`.

## Done

- All namespaces/assemblies renamed; zero upstream references remain.
- Retargeted netstandard2.0/net6 TFMs -> net8.0; dropped
  TunnelVisionLabs.ReferenceAssemblyAnnotator + .NET 5 ref pack download.
- WinForms broker isolated behind the `net8.0-windows` TFM only
  (`ENABLE_WEBVIEW2`). The plain `net8.0` lib is AOT-clean.
- Anonymous-type Xbox request bodies replaced with named
  `XboxLiveAuthRequestBody` (JsonObject properties).
- All serializer call sites routed through `QuiescentJson` facade or use
  AOT-safe JsonElement/JsonNode APIs.
- `IsAotCompatible=true` analyzers enabled on all libs.
- Yorii Launcher switched from NuGet packages to ProjectReferences;
  usings/linker.xml updated; builds clean (Debug/x64).
- NativeAOT smoke test (`PublishAot=true`, win-x64): offline session,
  local version JSON parsing, live Mojang manifest (907 versions) - all pass
  in the published native exe.

## Source-gen pass (implemented)

Per-library `[JsonSerializable]` contexts now live in each project under
`Json/*JsonContext.cs` and self-register into `JsonSerializerRegistry` via
ModuleInitializer. All serializer call sites route through the
`QuiescentJson` facade, which resolves JsonTypeInfo from the combined
resolver first and only falls back to reflection when a type is not
annotated (JIT builds / dev runs).

Build with `-p:DefineConstants=QUIESCENT_SKIP_SOURCEGEN` to compile without
contexts (used in environments lacking full System.Text.Json source-gen).

To finish hard-mode AOT on your machine:
1. Build Release normally (contexts active) and confirm zero IL3050/IL2026
   warnings from Quiescent.* projects.
2. Remove `TrimmerRootAssembly` roots from the smoke test one by one;
   if the AOT binary still passes 3/3, drop
   `JsonSerializerIsReflectionEnabledByDefault` too.
3. Replace the WinForms+WebView2 interactive-login broker with a custom
   `IWebUI` backed by WinUI 3 WebView2 (also removes the App.xaml.cs runtime
   WebView2 assembly-loading hack, which is itself AOT-hostile).

## Known follow-ups

- Interactive MSA login currently uses the legacy broker path (unchanged
  behavior vs upstream); port to custom IWebUI for full AOT.
- LiteLoader version JSON payload is now the named record `LiteLoaderVersionFile`.
- Forge/NeoForge installers were not exercised by the smoke test (need real
  installs to validate).
