---
title: 自动初始化
---

# 自动初始化做了什么 {#automatic-setup}

初始化是**补齐式、幂等**流程，不是“重置项目”。每次导入或构建前只创建缺失项，
不会清空已有的业务目录，也不会覆盖你已经保存的 Main 场景或自定义 BootUI。

## 创建与配置内容

- 创建 `Assets/Game/Config/QHYFrameworkSettings.asset`。
- 从包模板复制可编辑的 `Assets/Game/Boot/BootUI.prefab`。
- 缺失时创建 Boot 和 Main；场景包含 Camera，Main 额外包含 UGUI Text 和热更测试脚本。
- 创建 `Assets/Game/Scripts/HotUpdate/Game.HotUpdate.asmdef`。
- 准备 `Generated/HotUpdate` 与 `Generated/AOTMetadata` 输出目录。
- 配置 Boot 为内置启动场景，并保证 Main 作为 YooAsset 资源场景。
- 配置 YooAsset 的 `GamePackage` 与基础 Collector。

## 已有项目的安全边界

初始化不会删除 `Assets/Scenes` 中的其他场景，也不会要求该目录只包含 Boot。若当前
Untitled 场景未保存，自动初始化会关闭它再创建宿主场景，不保留默认 Camera、Light
或其他临时对象。已经保存的场景不会被这种处理影响。

:::note 构建前自动准备
用户不需要再点击“Prepare Project”。Full Package 与 HotUpdateOnly 的入口会自动执行必要校验和生成步骤。
:::

## 自定义之后如何保护

首次生成后可以编辑 BootUI Prefab、Main 场景和热更新脚本。建议纳入版本控制。
如果你主动删除某个必需文件，下一次自动准备会把缺失模板重新创建。

**预期结果：** 重复触发自动初始化不会改变现有业务内容。

**下一步：** [认识目录结构](project-layout.md)。
