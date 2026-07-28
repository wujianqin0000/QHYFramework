---
title: HybridCLR fundamentals
---

# HybridCLR fundamentals {#hybridclr}

- **AOT assemblies** are converted to native code by IL2CPP.
- **HotUpdate assemblies** are downloaded as YooAsset resources and loaded at runtime.
- **Supplemental metadata** supplies generic metadata; it does not replace native AOT code.

```mermaid
sequenceDiagram
  participant B as Boot(AOT)
  participant Y as YooAsset
  participant H as HybridCLR
  Y->>B: AOTMetadata/*.dll.bytes
  B->>H: LoadMetadataForAOTAssembly
  Y->>B: Game.HotUpdate.dll.bytes
  B->>B: Assembly.Load(bytes)
  B->>Y: LoadSceneAsync("Main")
```

Gameplay code and remote content usually use HotUpdateOnly. Boot, native
plugins, Player settings, and IL2CPP changes require Full Package.

An AOT difference produces a strong warning but does not block HotUpdateOnly.
You must still use metadata compatible with the target published client and
must not call new native AOT implementations that client does not contain.
