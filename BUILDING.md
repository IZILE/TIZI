TIZI 使用 .NET SDK 10、Avalonia 12.1.3，目标为 Windows x64。

```
dotnet restore TIZI.slnx
dotnet build TIZI.slnx -c Release
dotnet test tests/Tizi.Tests/Tizi.Tests.csproj -c Release
dotnet publish src/Tizi.Desktop/Tizi.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false
```

将 mihomo 官方 1.19.32 Windows amd64 compatible 构建放入 engine/mihomo.exe。下载地址、源代码地址和 SHA-256 位于 engine/provenance.json，程序在启动内核前检查哈希。上游版本源码归档与本次交付一同提供于 sources/mihomo-v1.19.32-source.tar.gz；上游构建工具链、依赖版本和编译说明位于归档的 go.mod、go.sum、Makefile 及 .github/workflows 中。

安装包使用 Inno Setup 7 编译：

```
ISCC.exe /DPublishDir=<绝对发布目录> /DDeliveryDir=<绝对输出目录> installer/TIZI.iss
```

发布目录需包含 TIZI.exe、engine、licenses、sources、LICENSE、TIZI.ico 及使用说明。无须混淆、裁剪或删除正常功能来缩小体积。构建输出及个人凭据不属于源代码归档。

GeoIP 规则使用随包提供的 `engine/geoip.dat`，来源与 SHA-256 记录于 `engine/geodata-source.json`；程序在复制到内核会话前验证完整性。MetaCubeX/meta-rules-dat 的生成器源码归档位于 `sources/meta-rules-dat-source.tar.gz`，许可位于 `licenses/Meta-rules-dat-LICENSE.txt`。构建与打包须保留数据库、来源元数据和两份上游源码归档。
