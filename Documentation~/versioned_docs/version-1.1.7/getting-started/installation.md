---
title: 安装
---

# 通过 Git UPM 安装 {#installation}

## 前置条件

确认项目使用 Unity 2022.3 LTS，并已安装 Git。首次导入会自动安装 QFramework、
HybridCLR 与 YooAsset，网络较慢时请等待 Package Manager 完成解析。

## 安装步骤

1. Unity 菜单打开 **Window → Package Manager**。
2. 点击左上角 **+**。
3. 选择 **Add package from git URL...**。
4. 输入：

```text
https://github.com/wujianqin0000/QHYFramework.git
```

![Unity Package Manager 中已安装的 QHY Framework](/img/screenshots/upm-install.webp)

如果仓库使用 UPM 分支或标签，可固定版本：

```text
https://github.com/wujianqin0000/QHYFramework.git#v1.1.7
```

也可以直接编辑项目的 `Packages/manifest.json`：

```json
{
  "dependencies": {
    "com.wjq.qhy-framework": "https://github.com/wujianqin0000/QHYFramework.git#v1.1.7"
  }
}
```

## 自动发生的工作

包被解析后，QHY Framework 会检查并安装依赖、创建 `Assets/Game` 业务骨架、
Boot/Main 场景、BootUI Prefab、`QHYFrameworkSettings.asset`、热更新程序集和
YooAsset Collector 配置。FTP 配置只在 QHY Release 窗口出现，不属于 Settings Asset；
各平台客户端版本从 `v1.0.0` 开始。

:::warning 不要提交凭据
FTP Password 不会写进项目资产。Windows Release 窗口默认使用凭据管理器安全记住，
也可通过 `QHY_FTP_PASSWORD`/CI Secret 注入。不要把密码放入 URL、命令行或日志。
:::

## 预期结果

Console 无编译错误，菜单出现 **QHY Framework**，Project 中出现
`Assets/Scenes/Boot.unity` 与 `Assets/Game/Content/Scenes/Main.unity`。

## 从 1.1.4 或更早版本升级

升级到本次变更后，首次加载保持 `collectorManagementMode = InitializeOnly`，已有 YooAsset Package
不会被重建或覆盖。若要启用新的默认拆包管理，先备份 Collector 配置，人工确认旧 QHY
Collector 并添加 `QHYFramework.Managed:v2`，再选择 ManagedGroupsOnly。不要仅凭组名
删除或接管配置。

第一次发布没有资源基线，会以 `<ClientVersion>-r0001` 建立完整远端 Bundle 种子库；
之后才按 `publish-plan.json` 增量上传。HotUpdateOnly 必须找到已登记 Published 的 Full Package 客户端和
AOT 快照基线。首次加载旧 Settings 时框架会自动重新序列化并移除遗留的
`ftpPassword` YAML 字段；升级后可再扫描工程确认无凭据残留。

## 从 1.1.2 或更早版本升级 HybridCLR

HybridCLR 8.12.0 用户应升级到包含该修复的当前代码或后续稳定版。1.1.2 的依赖初始化器
可能把有效的 `HybridCLRData/LocalIl2CppData-*/il2cpp/build/deploy` 目录误判为未安装，
在 `Generate/All` 后切回 Unity 全局 IL2CPP，生成“构建成功但启动失败”的客户端。

升级包后无需修改 HybridCLR、YooAsset 或 QFramework 源码，也无需复制
`HybridCLRData`。执行一次 **Full Package Build**；框架会校验本地工具链及版本，必要时
自动重新安装，并在 Player 构建后验证 HybridCLR 原生运行时。

## 常见错误

- `Permission denied (publickey)`：使用 HTTPS URL，或先给 GitHub 配置 SSH Key。
- 长路径失败：启用 `core.longpaths`，并把 Unity 项目放在较短目录。
- IL2CPP 目录不存在：回到 Unity Hub 补装对应平台的 IL2CPP 模块。

**下一步：** [了解自动初始化](automatic-setup.md)。
