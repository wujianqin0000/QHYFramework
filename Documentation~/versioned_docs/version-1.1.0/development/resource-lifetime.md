---
title: 资源生命周期
---

# 资源加载与释放 {#resource-lifetime}

## Loader 与使用者同生命周期

```csharp
public sealed class CharacterView : MonoBehaviour
{
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
}
```

QHY 适配资源对象持有 YooAsset `AssetHandle`。Loader 回收时，QFramework 引用计数
下降并释放 Handle；Bundle 只有在所有使用者释放后才可卸载。

## 避免这些错误

- 临时局部 Loader 加载完立即回收，但把资源交给长期对象继续用。
- 重复异步请求后只释放其中一份引用。
- 直接调用 YooAsset 却丢失 Handle。
- 场景卸载后仍保留指向场景资源的静态引用。

`autoUnloadBundleWhenUnused` 会更积极回收内存，但可能增加反复加载；移动端内存紧张
时开启，并用 Profiler 验证抖动。`clearUnusedCacheAfterUpdate` 清理不再属于当前
Manifest 的缓存文件，不影响正在使用的 Handle。
