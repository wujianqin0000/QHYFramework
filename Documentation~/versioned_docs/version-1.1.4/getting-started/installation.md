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
https://github.com/wujianqin0000/QHYFramework.git#v1.1.3
```

也可以直接编辑项目的 `Packages/manifest.json`：

```json
{
  "dependencies": {
    "com.wjq.qhy-framework": "https://github.com/wujianqin0000/QHYFramework.git#v1.1.3"
  }
}
```

## 自动发生的工作

包被解析后，QHY Framework 会检查并安装依赖、创建 `Assets/Game` 业务骨架、
Boot/Main 场景、BootUI Prefab、`QHYFrameworkSettings.asset`、热更新程序集和
YooAsset Collector 配置。默认 FTP 主机、用户名和密码为空，各平台版本从
`v1.0.0` 开始。

:::warning 不要提交凭据
FTP Password 会保存在项目资产里。生产项目应通过受控环境注入或至少确保该资产不会发布到公共仓库。
QHY Framework 的 UPM 包本身不携带作者测试服务器或密码。
:::

## 预期结果

Console 无编译错误，菜单出现 **QHY Framework**，Project 中出现
`Assets/Scenes/Boot.unity` 与 `Assets/Game/Content/Scenes/Main.unity`。

## 从 1.1.2 或更早版本升级

HybridCLR 8.12.0 用户应至少升级到 QHY Framework 1.1.3。1.1.2 的依赖初始化器
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
