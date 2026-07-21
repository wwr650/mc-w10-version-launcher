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
* 运行 `MCLauncher.exe` 启动程序

## 常见问题与解决方法

遗憾的是，随着向GDK的迁移，启动器的可靠性有所下降。
加上Windows 11在质量和稳定性方面的持续下降，这些问题大多似乎是由Windows本身引起的，而另一些则是启动器提取游戏文件的方法导致的。

由于我（@dktapps）已不再将Windows作为主要操作系统，并且觉得处理Windows问题非常繁琐，这些问题不太可能得到显著改善，因此以下列出一些已知问题及临时解决方法。

### 通过开始菜单启动游戏时出现 `Invalid argument` 错误

将启动器移动至较短的路径，移动后请至少从启动器内部启动一次游戏。

这似乎是 `Minecraft.Windows.exe` 的路径过长导致的问题，即使增加了 MAX_PATH，较新版本的Windows上仍会出现此问题。
旧版本没有此问题，因此尚不清楚是什么原因导致的。

### 解密Minecraft.Windows.exe失败

* 请确保在使用启动器之前已从Store安装了Minecraft（或Minecraft Preview）
* 需要有效的许可证，您无法使用启动器盗版游戏
* 有时使用 `工具 -> 清理以重新安装Microsoft Store的Minecraft` 可能有助于解决问题
* 如果所有方法都失败了，请尝试重新启动计算机。有时Windows的服务会无缘无故出现问题，重启可能会解决这些问题

### 无效的16位应用程序或SmartScreen显示"此应用无法在你的电脑上运行"

这通常是因为无法获取Minecraft exe的解密版本。请先尝试上述步骤，同时检查 `文件 -> 打开日志文件` 查看是否有任何错误信息

### `minecraft://` 及类似的深度链接无法使用

请检查 `HKEY_CLASSES_ROOT\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Extensions\windows.protocol\minecraft` 及类似的注册表项是否存在重复条目。

有时注册表中会出现这些协议的重复条目，原因尚不明确。

删除无效条目可能有助于修复深度链接。

## 自行编译启动器

您需要安装包含Windows 10 SDK 10.0.17763版本和.NET Framework 4.6.1 SDK的Visual Studio。若未预装，可通过Visual Studio安装程序获取这些组件。
只要未进行特殊修改，本项目在Visual Studio中可直接编译。

## 常见问题解答

**此工具能否同时运行多个《我的世界：基岩版》实例？**

截至目前，不能。该工具仅支持**安装**多个版本，但同一时间只能运行一个版本。
