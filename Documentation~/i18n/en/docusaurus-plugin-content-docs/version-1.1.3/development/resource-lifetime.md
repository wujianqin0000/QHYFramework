---
title: Resource lifetime
---

# Resource loading and release {#resource-lifetime}

Keep a loader for the lifetime of its consumer:

```csharp
private QFramework.ResLoader loader;

private void Awake()
{
    loader = QFramework.ResLoader.Allocate();
    var icon = loader.LoadSync<Sprite>("HeroIcon");
}

private void OnDestroy()
{
    loader?.Recycle2Cache();
    loader = null;
}
```

The QHY resource wrapper owns the YooAsset `AssetHandle`; recycling the loader
reduces QFramework references and releases it. Do not recycle a temporary
loader while a long-lived object still uses its asset, lose handles from direct
YooAsset calls, or keep static scene-resource references after unload.

`autoUnloadBundleWhenUnused` saves memory but may increase reload churn.
Measure with Profiler before enabling it globally.
