# MCLauncher

   [English](README.md) | [中文](README_zh-CN.md)

该工具允许您在同一台电脑上并行安装多个版本的《我的世界：Windows 10版》（基岩版）。
这对于需要并行测试测试版、正式版或其他版本，而无需反复卸载和重装游戏的情况非常实用。

## 翻译说明 -- 简体中文翻译(zh-cn)

已对用户界面进行翻译（主要为机器翻译，部分表述可能不够准确，但不影响正常使用）。

## 免责声明

此工具**无法**帮助您盗版游戏；您必须拥有一个能够从微软商店下载《我的世界》的微软账户。

## 系统要求

* 已关联微软商店且 **拥有《我的世界：Windows 10版》** 的微软账户
* 用户账户具备 **管理员权限** （或可访问具备此权限的账户）
* 在Windows 10设置中开启**开发人员模式**以允许应用安装
* 如需使用测试版，还需通过**Xbox Insider Hub订阅《我的世界》测试计划**
* 已安装[Microsoft Visual C++运行库](https://aka.ms/vs/16/release/vc_redist.x64.exe)

## 安装步骤

* 从[发布页面](https://github.com/MCMrARM/mc-w10-version-launcher/releases)下载最新版本，解压至任意目录
* 运行 `MCLauncher.exe`启动程序

## 自行编译启动器

您需要安装包含Windows 10 SDK 10.0.17763版本和.NET Framework 4.6.1 SDK的Visual Studio。若未预装，可通过Visual Studio安装程序获取这些组件。
只要未进行特殊修改，本项目在Visual Studio中可直接编译。

## 常见问题解答

**此工具能否同时运行多个《我的世界：基岩版》实例？**
截至目前，不能。该工具仅支持**安装**多个版本，但同一时间只能运行一个版本。
