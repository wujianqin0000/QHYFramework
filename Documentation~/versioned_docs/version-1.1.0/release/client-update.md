---
title: 客户端整包更新
---

# 客户端整包更新 {#client-update}

资源热更新无法替换已编译进客户端的 AOT/原生内容。Full Package 会生成新的客户端
包和 `latest.json`，旧客户端在 YooAsset 初始化前检查它。

## latest.json

```json
{
  "SchemaVersion": 1,
  "Platform": "Windows64",
  "Version": "v1.0.1",
  "AndroidVersionCode": 1000001,
  "PackageUrl": "https://example.com/Client/PC/v1.0.1/Client_Windows64_v1.0.1.zip",
  "PackageType": "zip",
  "FileName": "Client_Windows64_v1.0.1.zip",
  "SizeBytes": 43620762,
  "Sha256": "...",
  "EntryExecutable": "MyGame.exe",
  "PublishedAtUtc": "2026-07-27T08:00:00Z",
  "Mandatory": true
}
```

## 下载与校验

包保存到 `persistentDataPath/ClientUpdates`。服务器支持 Range 时断点续传，不支持则
重新下载。只有长度和 SHA-256 均匹配才进入安装。完整且已校验的包可在下次启动复用。
清单请求失败、损坏或没有更高版本会记录警告并继续当前客户端。

## Windows 安装

Boot 启动包内独立 updater 后退出游戏。updater 等旧进程结束，安全解压 ZIP 到同盘
staging，阻止 `../` 路径穿越；安装目录先改名 backup，再原子切换。启动新程序失败
时恢复 backup。无写权限会通过 `runas` 请求管理员权限。

## Android 安装

APK 通过 FileProvider 的 `content://` URI 交给系统安装器。未知来源权限缺失时先
打开设置页。应用无法静默安装，用户必须确认；签名不一致或 versionCode 未提高会被
Android 拒绝。

## 发布顺序

资源和客户端全部上传成功后，最后发布 `latest.json`。不要删除旧资源目录。首次拥有
此能力的基线客户端仍需人工分发一次。
