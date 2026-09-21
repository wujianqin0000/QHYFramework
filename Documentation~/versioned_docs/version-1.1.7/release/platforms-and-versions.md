---
title: 平台与版本
---

# 平台配置与独立版本 {#platforms-versions}

| 概念 | 示例 | 什么时候改变 |
|---|---|---|
| ClientVersion | `v1.0.4` | FullPackage，客户端/AOT/原生配置变化 |
| ResourceVersion | `v1.0.4-r0007` | 每次真实资源或代码热更新 |

版本自动化默认同时管理两种版本；FullPackage 提供 Major、Minor、Patch，HotUpdateOnly 选择下一未占用修订。无实际变化不消耗 ResourceVersion。

## 在发布工具中切换平台

修改 **目标平台** 会立即调用 Unity 的 Active Build Target 切换，不再只是修改发布参数。
QHY 会先保存原平台的客户端/资源版本、Development、自动版本和 FTP 偏好，切换成功后加载
目标平台自己的偏好。已经选中 `QHYFrameworkSettings` 时，其 Inspector 会同步刷新并显示
新平台的 BaseURL。

切换可能触发资源重新导入和脚本编译；在构建、上传、编译或 AssetDatabase 更新期间选择器
不可用。目标平台缺少 Unity Build Support 或切换失败时，发布工具恢复为 Unity 实际仍然
激活的平台并给出错误，不会出现“界面显示 Android、工程仍是 Windows”的状态。

Settings 只显示当前 BuildTarget 的 BaseURL，并另有一个所有平台共用的 `gameDirectory`。BaseURL 只填写服务器资源总根；不同平台的配置仍独立保存。schema v5 路径为：

~~~text
{resourceBaseUrl}/{gameDirectory}/{platform}/
├─ cdn/{bundles,versions,clients}
└─ origin/{current,qhy.json}
~~~

`gameDirectory` 必须是单个路径段，只允许小写字母、数字、点、短横线和下划线。同一服务器根下每个游戏使用不同值。schema v5 不兼容旧发布目录和状态，清空后重新 FullPackage，详见 [FTP、手动上传与可选 CDN](ftp-cdn.md)。
