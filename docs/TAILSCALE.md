# Tailscale 手机出口

0.1.49 在“设置 → 代理”中增加了 Tailscale 手机出口，默认关闭。

开启“通过当前代理节点中转”后，连接顺序为：

本机应用 → 当前代理节点 → Tailscale DERP 中继 → iPhone → 手机当前网络 → 网站。

要让最终出口使用蜂窝网络，请关闭 iPhone 的 Wi-Fi。Tailscale 的 DERP 只转发加密的隧道数据，最终连接网站的是手机。

## 配置步骤

1. 在 iPhone 的官方 Tailscale App 中登录，开启 **Run as Exit Node**；在 Tailscale 管理控制台批准手机作为出口节点。使用支持 iPhone 出口节点的新版 App。
2. 在同一 tailnet 的管理控制台创建认证密钥，填写到 TIZI 的“认证密钥”框。按你的 tailnet 策略完成设备批准。认证密钥无需发给其他人。
3. 在 TIZI 中填写 iPhone 的 Tailscale IP，通常是 `100.x.x.x`。此处不是手机的公网 IP，也不是 Wi-Fi 地址。
4. 保留“通过当前代理节点中转”，选择可用的代理节点并保存设置，再连接。首次加入 tailnet 和连接手机可能比普通节点慢，可以取消连接。
5. 需要接管整机流量时使用 **TUN + 全局**。系统代理模式仅覆盖遵循系统代理的软件；规则模式中的直连规则仍会直连。
6. 查看右下角“出口：国家”，悬停查看观察到的出口 IP，并与手机使用蜂窝网络时的出口核对。

“通过当前代理节点中转”会强制使用 DERP，避免 Tailscale 的对等直连省略代理这一跳。关闭该项时，连接顺序变为本机 → Tailscale → 手机。

“接受子网路由”用于访问其他设备发布的内网网段，仅使用手机互联网出口时无需开启。“允许访问本地网络”按需要选择。

修改 Tailscale 配置需要先断开连接。TIZI 将自己注册为独立设备；登录身份在本机使用 Windows DPAPI 加密保存，重连继续使用。更换控制服务器或认证密钥会使用独立登录身份。卸载时按现有规则保留个人数据。

手机出口或代理节点不可用时，TIZI 代理策略没有直连回退选项。连接前会通过固定到手机的本地检测入口验证连接，成功后才启用系统代理或 TUN。原有直连模式及直连规则保持明确的直连行为。

## 出口国家

右下角显示查询服务从实际出口 IP 得到的地理位置国家，不采用节点名称、旗帜或 IP 注册地。多来源给出的国家冲突、结果缺失或失败时显示未知。连接、切换节点和路由模式会重新查询；保持连接时约每两分钟刷新，后台刷新期间保留上次结果，断开时清除。

关闭 IP 类型和原生信息检测后，仍保留轻量的出口国家查询。上下行速度继续在概览页显示。IP 地理位置数据库的判断不等于物理位置证明。

## 验证范围

已检查设置保存、认证信息加密、IP 参数校验，以及随附 mihomo 1.19.32 的配置和隔离启动。合成本地代理已收到 Tailscale 控制连接；切换代理节点保持手机为最终策略。真实 iPhone、蜂窝网络、tailnet 权限及 DERP 端到端吞吐尚未联调，配置通过不等于手机实际连通。

参考：[Tailscale 官方 iOS 出口设置](https://tailscale.com/docs/features/exit-nodes?tab=ios)、[mihomo Tailscale 配置](https://wiki.metacubex.one/config/proxies/tailscale/)。

## English

In **Settings → Proxy**, enable **Tailscale phone exit**, enter an authentication key from the same tailnet and the iPhone's Tailscale IP, then save while disconnected. On the official iPhone app, enable **Run as Exit Node**, approve it in the admin console, and turn off Wi-Fi for a cellular exit.

With transit enabled, the path is PC → selected proxy → DERP → iPhone → internet. Transit mode forces DERP; disabling transit allows direct Tailscale transport. Use TUN and Global mode for full-device capture. Direct rules remain direct. The footer shows the country observed for the exit IP; hover for the IP. Unknown or conflicting results never use the node label or registered country as a fallback.

The feature is disabled by default. Authentication state is encrypted locally. Core configuration and local synthetic proxy tests have passed; an actual iPhone/tailnet/cellular path still requires an end-to-end check.
