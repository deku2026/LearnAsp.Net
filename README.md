# LearnAspNet · 现代 ASP.NET Core 10 / .NET 10 实战学习仓库

按 `C:/MyFile/ArcForges/ArchitectureDesign/ASP.NetStudy/ASP.NET-Core-net10-学习路线图.md`
逐步实现的 **31 个独立 `WebApplication` 实验**，每个对应路线图里的一个「步骤 N」或「第 N 部分-M」详解 md。
已推进到 W7；完成阶段使用真实应用代码、数据库/中间件和集成测试，后续阶段保留可运行占位。

阶段 14（Unity 接轨附录）不涉及；本仓不重写路线图，文档保留在 ArcForges 原位。

## 已实现阶段

- W1–W2：Step01–08
- W3：Step09–10
- W4：Part03_1–4
- W5：Part04_1–3
- W6：Part05_1–2，详见 [Keycloak + SPA/BFF 安全实战](docs/w6-part05-security-lab.md)
- W7：Part06_1–Part07，详见 [消息模式、RabbitMQ 与分布式通信实战](docs/w7-messaging-distributed-lab.md)

## 工作原理

整仓一个 SLNX 解决方案 + **31 个独立 `Microsoft.NET.Sdk.Web` exe**。每个占位项目长这样：

```csharp
// LearnAspNet · Step01 · 承载与启动模型 (placeholder)
// Doc: ASP.NetStudy/步骤1-承载与启动模型-完整实施指南.md

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Step01 · HostStartup — placeholder, fill src/Step01_HostStartup/Program.cs");

app.Run();

public partial class Program;
```

`public partial class Program;` 留给后面 `WebApplicationFactory<Program>` 集测引用。

## 命令速查

```pwsh
# 在 worktree 里
cd C:\MyFile\LearnAsp.Net\.worktree\scaffold

# 一把编一遍 (Debug + Release CI 都验)
dotnet build LearnAspNet.slnx -c Debug
dotnet build LearnAspNet.slnx -c Release

# 跑某一步 (端口在 launchSettings.json 里固定，5001-5031 一一对应)
dotnet run --project src/Step01_HostStartup
# 然后浏览器开 http://localhost:5001/

# 热重载
dotnet watch run --project src/Step01_HostStartup

# 格式化全仓 (pre-commit 也跑这个)
dotnet format LearnAspNet.slnx
```

## 端口分配

| # | 项目 | 端口 | 对应文档 |
|---:|---|---:|---|
|  1 | `Step01_HostStartup` | 5001 | 步骤1-承载与启动模型 |
|  2 | `Step02_DIConfigOptions` | 5002 | 步骤2-依赖注入-配置-Options |
|  3 | `Step03_MiddlewarePipeline` | 5003 | 步骤3-中间件管道 |
|  4 | `Step04_RoutingEndpoints` | 5004 | 步骤4-路由与终结点 |
|  5 | `Step05_MinimalApiVsController` | 5005 | 步骤5-MinimalAPI与Controller |
|  6 | `Step06_BindingValidationProblemDetails` | 5006 | 步骤6-模型绑定-校验-ProblemDetails |
|  7 | `Step07_AuthnAuthzEntry` | 5007 | 步骤7-认证授权接入点 |
|  8 | `Step08_LoggingErrorsHealth` | 5008 | 步骤8-日志-错误处理-健康检查 |
|  9 | `Step09_IntegrationTesting` | 5009 | 步骤9-集成测试 |
| 10 | `Step10_HttpFoundation` | 5010 | 步骤10-HTTP底座 |
| 11 | `Part03_1_ApiDesign` | 5011 | 第3部分-1-生产API设计 |
| 12 | `Part03_2_AppArchitecture` | 5012 | 第3部分-2-应用架构 |
| 13 | `Part03_3_ProjectStructure` | 5013 | 第3部分-3-项目结构 |
| 14 | `Part03_4_ArchTesting` | 5014 | 第3部分-4-架构测试与契约兼容 |
| 15 | `Part04_1_EFCore` | 5015 | 第4部分-1-EFCore核心 |
| 16 | `Part04_2_Caching` | 5016 | 第4部分-2-缓存三层 |
| 17 | `Part04_3_MultiTenant` | 5017 | 第4部分-3-多租户 |
| 18 | `Part05_1_AuthnAuthz` | 5018 | 第5部分-1-认证授权核心 |
| 19 | `Part05_2_SpaAuth` | 5019 | 第5部分-2-前后端分离SPA认证 |
| 20 | `Part06_1_MessagingPatterns` | 5020 | 第6部分-1-消息模式 |
| 21 | `Part06_2_MessagingTools` | 5021 | 第6部分-2-消息中间件与工具 |
| 22 | `Part07_DistributedComm` | 5022 | 第7部分-分布式与微服务通信 |
| 23 | `Part08_1_OpenTelemetry` | 5023 | 第8部分-1-OpenTelemetry |
| 24 | `Part08_2_TroubleshootingProcess` | 5024 | 第8部分-2-生产排障流程 |
| 25 | `Part09_Deployment` | 5025 | 第9部分-部署 |
| 26 | `Part10_Aspire` | 5026 | 第10部分-Aspire (后期升级为 Aspire.Hosting.Sdk) |
| 27 | `Part11_1_PerformanceAdvanced` | 5027 | 第11部分-1-性能进阶 |
| 28 | `Part11_2_NativeAotTrim` | 5028 | 第11部分-2-NativeAOT与Trim |
| 29 | `Part11_3_FrameworkSource` | 5029 | 第11部分-3-框架源码精读 |
| 30 | `Part12_ElectiveBranches` | 5030 | 第12部分-选学方向支线 |
| 31 | `Part13_Summary` | 5031 | 第13部分-全路线总结与融会贯通 |

## 关键路径

```
.
├── .editorconfig / .gitattributes / .gitignore / .pre-commit-config.yaml
├── .codespell-ignore / .config/dotnet-tools.json
├── .vsconfig / .vscode/{extensions,settings,tasks}.json
├── .github/
│   ├── dependabot.yml                 # github-actions / nuget / dotnet-sdk / pre-commit (weekly)
│   └── workflows/
│       ├── linux-ci.yml               # build Debug + Release + pre-commit
│       ├── macos-ci.yml
│       ├── windows-ci.yml
│       └── codeql.yml                 # csharp autobuild + security-and-quality
├── Directory.Build.props              # net10.0 / LangVersion 14.0 / Nullable / CPM
│                                      #   + InterceptorsNamespaces for ASP.NET 10 validation gen
├── Directory.Build.targets
├── Directory.Packages.props           # Central Package Management (empty for now)
├── NuGet.config
├── global.json                        # 10.0.301 / latestPatch
├── LearnAspNet.slnx                   # 31 项目
├── LICENSE / README.md / SECURITY.md
└── src/
    ├── Step01_HostStartup/            # 1 个 Web exe
    │   ├── Step01_HostStartup.csproj
    │   ├── Program.cs
    │   ├── appsettings.json
    │   ├── appsettings.Development.json
    │   └── Properties/launchSettings.json
    ├── Step02_DIConfigOptions/
    ├── … (其余 29 个)
    └── Part13_Summary/
```

## 现状

- 运行时: **.NET 10 LTS + ASP.NET Core 10 + C# 14** (`net10.0` / `LangVersion 14.0`)
- SDK: `.NET 10 SDK 10.0.301`, `rollForward: latestPatch`
- 解决方案: **SLNX**
- 包管理: **CPM** (`Directory.Packages.props` 集中管所有包版本)
- 占位规模: **31 个 `Microsoft.NET.Sdk.Web` exe** — 每个一个最小可跑的 `WebApplication`
- 校验: ASP.NET Core 10 内置校验生成器命名空间 (`Microsoft.AspNetCore.Http.Validation.Generated`) 已在 `Directory.Build.props` 注册
- CI: 三 OS build (Debug + Release) + `pre-commit run --all-files` (含 `dotnet format --verify-no-changes`)
- CodeQL: csharp, security-and-quality, autobuild, push/PR + 月度 cron
- Dependabot: github-actions / nuget / dotnet-sdk / pre-commit 周更

## 填一个占位

1. 选一个 `src/StepNN_xxx/Program.cs`，顶部注释里写了对应的 `ASP.NetStudy/*.md` 文档；
2. 翻文档；按文档 "二、核心概念逐个击破" 在 `Program.cs` 里实现 — DI、middleware、endpoint、配置、Options、authn/authz... 都直接堆进去；
3. 配 `https://learn.microsoft.com/aspnet/core/?view=aspnetcore-10.0` + `dotnet/aspnetcore` 源码 + `WebApplicationFactory` 集测 进入循环；
4. `dotnet run --project src/StepNN_xxx` 或者 `dotnet watch run --project src/StepNN_xxx`，开 `http://localhost:50NN/`；
5. 后期可以另起 `tests/StepNN_xxx.Tests/` 用 `WebApplicationFactory<Program>` 钉行为 (步骤9 那一节就是讲这个)。

## 添加新占位

直接 `cp -r` 现成项目改名 (改目录名 + 改 csproj 文件名)，改 `Program.cs` 顶部注释字段 + 提示字符串 + `launchSettings.json` 端口。下次 `dotnet build` 自动接上 — SDK-style 项目默认 GLOB `**/*.cs`。

## Worktree 用法

主 checkout (`C:/MyFile/LearnAsp.Net`) 故意保持空 (只有 `LICENSE`)，所有占位放进 worktree 分支:

```pwsh
git -C C:\MyFile\LearnAsp.Net worktree list
# C:/MyFile/LearnAsp.Net                    [main]      <- 空, 只有 LICENSE
# C:/MyFile/LearnAsp.Net/.worktree/scaffold [scaffold]  <- 这里
```

要再起独立练习分支：

```pwsh
git -C C:\MyFile\LearnAsp.Net worktree add -b step03-middleware .worktree/step03-middleware scaffold
```

`.worktree/` 已加进 `.gitignore`。

## 与路线图的关系

- 本仓只放 **占位 + 通用基建 + CI**，**不在仓内**重写路线图；
- 路线图文档保留在原位 (`C:/MyFile/ArcForges/ArchitectureDesign/ASP.NetStudy/`)，仓内通过注释 + 本 README 引用相对路径。

## License

见 `LICENSE`。
