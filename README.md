# TIZI

面向 Windows 10 / 11 x64 的代理客户端，提供订阅与节点管理、分流规则、网络监控和机场导航。

[下载最新安装包](https://github.com/IZILE/TIZI/releases/latest) · [使用说明](docs/INSTALLING.md) · [功能与限制](docs/FEATURES.md) · [构建说明](BUILDING.md)

## 功能

- 导入和导出订阅、节点链接、二维码、JSON、Clash YAML 及部分 Shadowrocket 配置。
- 订阅用量、到期信息、节点延迟测试和可编辑的分流规则。
- 随窗口宽度变化的节点宫格，当前节点蓝色强调，自动展开所属订阅；顶部节点下拉保持原布局并在展开时测试延迟。
- 本地代理、Windows 系统代理与 TUN；支持取消正在进行的连接。
- 概览显示上传、下载、延迟和本次连接时长；延迟曲线按需开启。
- 节点与内容项单击选择、双击执行，带动画的右键菜单和可撤销移除。
- 长按进入多选或全选，红色批量移除按钮从工具栏右侧展开；普通单项操作使用右键菜单。
- 机场目录与可选的在线搜索，显示公开来源及价格条件。
- OpenAI Responses、OpenAI 兼容 Chat、Claude Messages 与 Gemini GenerateContent 接口，支持各厂商独立密钥和自定义地址。
- 应用内检查 GitHub 新版本，下载并校验 SHA-256 后安装。
- Windows 用户级加密配置、备份恢复、托盘与可选登录启动。

## 安装

从 Releases 下载 `TIZI-Setup.exe`。更新前退出 TIZI，运行安装程序；默认安装到当前用户的本地程序目录。安装包自带 .NET 运行时及代理内核。

0.1.22 统一概览字号，默认窗口调整为 1032 × 722，增加节点宫格、长按多选、应用内更新及多厂商 API。自动订阅更新失败只写日志，手动更新失败才显示通知。

## 配置与隐私

发布内容不包含个人订阅、节点凭据、OpenAI API 密钥或本地 API 设置。在线搜索功能需使用者自行配置兼容 API；密钥保存在当前 Windows 用户的加密配置中。

## 构建与许可

使用 .NET SDK 10 和 Avalonia 12.1.3。具体步骤和固定版本的 mihomo、GeoIP 来源见 [BUILDING.md](BUILDING.md)。本项目采用 [GPL-3.0](LICENSE)，第三方许可位于 [licenses](licenses)，随包内核的对应上游源码位于 [sources](sources)。

协议入口可配置不代表所有服务均已实测互通。尚未实现的功能与验证范围见 [功能对照](docs/FEATURES.md)。
