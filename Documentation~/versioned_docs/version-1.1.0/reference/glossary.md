---
title: 术语表
---

# 术语表 {#glossary}

| 术语 | 解释 |
|---|---|
| AOT | Ahead-of-Time，IL2CPP 构建时转换为原生代码的程序集 |
| HotUpdate | 客户端运行时下载并加载的托管 DLL |
| 补充元数据 | 给 HybridCLR 泛型解释执行补足信息的裁剪 AOT DLL |
| Package | YooAsset 独立资源集合，拥有版本与 Manifest |
| Address | 业务加载资产使用的稳定逻辑名 |
| Collector | 决定哪些资产进入 Package 的编辑器收集规则 |
| Manifest | Asset、Bundle、依赖和哈希的版本索引 |
| Bundle | YooAsset 下载与加载的资源归档 |
| Handle | YooAsset 资源/场景加载生命周期对象 |
| HostPlayMode | 内置首包结合远端 CDN 的运行模式 |
| EditorSimulateMode | 编辑器直接模拟构建清单的快速模式 |
| Full Package | 资源、热更 DLL、AOT 与 Player 的完整客户端构建 |
| HotUpdateOnly | 不重建 Player 的代码/资源热更新 |
| latest.json | 指向当前最新完整客户端包的跨版本清单 |
| Baseline | 已发布客户端对应的版本、AOT 或签名记录 |
| UPM | Unity Package Manager 标准包格式 |
