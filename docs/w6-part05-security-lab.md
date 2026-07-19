# W6 · Part05 身份认证与 SPA/BFF 安全实战

W6 把 `Part05_1_AuthnAuthz` 和 `Part05_2_SpaAuth` 从占位项目升级为一套可运行、可攻击面验证、可横向扩容的安全实验。身份提供方使用 Keycloak 26.7，服务端会话、令牌和 Data Protection 密钥使用 Redis 8.8；浏览器不保存 OAuth/OIDC token。

## 架构边界

```text
同源 SPA
  │  HttpOnly + SameSite=Lax 的不透明会话 Cookie
  │  X-CSRF: 1
  ▼
Part05_2 BFF ── Authorization: Bearer（仅服务端）──▶ Part05_1 API
  │                                                   │
  ├── OIDC Authorization Code + PKCE + PAR            ├── JWT/JWKS 五项校验
  ├── YARP 反向代理                                   ├── scope/role/owner 授权
  ├── refresh token 刷新与撤销                         ├── 显式 CORS
  └── Redis ticket + Data Protection                  └── 按 user/IP 分区限流
           │
           ├──────── Redis :6380
           └──────── Keycloak :8082
```

`Campus.Security` 是两个宿主共享的安全基础设施：`ITicketStore` 只把 256 位随机键放进浏览器 Cookie，完整认证票据和 token 存在 Redis；Data Protection 密钥也存在 Redis，因此多个 BFF 实例可解密同一个 Cookie 并读取同一会话。Back-channel logout 优先按 `sid` 撤销具体会话，只有令牌没有 `sid` 时才回退到 `sub`。

## 本地启动

WSL 中的 Docker Compose 只负责基础设施，`.NET` 构建、应用和测试可直接在 Windows 运行。确认 Keycloak `http://localhost:8082` 和 Redis `localhost:6380` 已启动后，在 Windows PowerShell 执行：

```powershell
pwsh ./scripts/Initialize-W6Keycloak.ps1
```

脚本从自动测试共用的 Realm 模板创建 `campus-w6`，生成随机用户密码和两个 confidential client secret，并在结束时打印需要设置的环境变量。它不会把 secret 写入仓库或 `appsettings.json`。Realm 已存在时脚本默认拒绝覆盖；明确需要重建本地实验 Realm 时才使用 `-Force`。

在两个 PowerShell 窗口分别运行：

```powershell
# 窗口 1：使用脚本打印的 campus-web secret
$env:Security__WebClientSecret='<generated campus-web secret>'
dotnet run --project src/Part05_1_AuthnAuthz

# 窗口 2：使用脚本打印的 campus-bff secret
$env:Bff__ClientSecret='<generated campus-bff secret>'
dotnet run --project src/Part05_2_SpaAuth
```

打开 `http://localhost:5019`，使用 `alice`、`bob` 或 `admin-user` 以及脚本打印的密码登录。Development 配置只为本地 HTTP 把 Secure Cookie 关闭；非 Development 默认要求 HTTPS、`__Host-` Cookie 和 `Secure`。

## 核心接口与行为

`Part05_1`：

- `GET /auth/login`、`GET /auth/me`、`POST /auth/logout`：服务端 Web/OIDC 流程，不向响应暴露 token。
- `POST /backchannel-logout`：验证签名、issuer、audience、过期时间、events 和 nonce 约束后写入 Redis 撤销标记。
- `/api/identity`、`/api/courses`：真实 bearer token、scope 策略和 owner 资源授权；`Admin` 可跨 owner。
- `/api/admin/audit`：角色策略。
- API 拒绝响应使用 401/403/429 Problem Details；限流按 `sub`，匿名时按 IP 分区，并返回 `Retry-After`。

`Part05_2`：

- `GET /bff/login`、`GET /bff/user`、`POST /bff/logout`：BFF 会话生命周期，`/bff/user` 只返回显示身份所需的有限声明。
- `/bff/api/**`：要求同源请求和 `X-CSRF: 1`，随后从 Redis 会话取出或刷新 access token，再由 YARP 发往 API。
- `POST /bff/backchannel-logout`：使对应服务端会话失效。
- 静态 SPA 带 CSP、`nosniff`、Referrer Policy 和 Permissions Policy；BFF 不启用 CORS，跨源预检不会得到许可。

## 真实测试

跨平台通用测试不需要 Docker：

```powershell
dotnet test LearnAspNet.GenericTests.slnx -c Release
```

W6 的真实集成测试仍由 Windows `.NET` 测试进程运行，但连接 WSL Docker 中已启动的 Keycloak/Redis：

```powershell
dotnet test tests/Part05_Security.IntegrationTests/Part05_Security.IntegrationTests.csproj -c Release
```

Fixture 会从 `deploy/keycloak/campus-w6-realm.template.json` 创建随机命名的隔离 Realm，生成随机密码和 client secret，启动真实 Kestrel 宿主，验证完成后删除 Realm。覆盖内容包括：

- Keycloak discovery/JWKS 与真实 JWT 的 issuer、audience、lifetime、签名密钥校验；
- OIDC Authorization Code、PKCE S256、PAR、state、nonce；
- scope、realm role、owner 资源授权和错误 audience；
- Redis 服务端 ticket、Data Protection 共享密钥以及双 BFF 实例共享会话；
- YARP bearer 注入、CSRF/Origin 拒绝、access token 到期刷新；
- refresh token 撤销、会话注销、429 Problem Details 与 `Retry-After`。

如果本机服务不可用，Fixture 只在 Linux 上回退到 Testcontainers。CI 因而保持清晰边界：Windows/macOS 构建全部项目并只跑通用测试；Linux 构建全部项目并跑包含 PostgreSQL、Redis、Keycloak 的完整 Docker 测试。

## 上线前必须替换的边界

- `RequireHttpsMetadata=true`，所有外部地址使用 HTTPS；不要把 Development 的 HTTP Cookie 设置带到生产。
- client secret 使用 Secret Manager、环境注入或正式密钥系统，不能写进配置文件和镜像。
- Redis 启用认证、TLS、网络隔离和高可用；Data Protection key ring 还应使用证书或 KMS 做静态加密。
- CORS origin、BFF `PublicOrigin`、redirect URI 和 post-logout URI 使用逐环境精确白名单，禁止通配符。
- 多实例高并发 token rotation 需要分布式刷新锁；本实验的进程内 per-session 锁用于说明单实例并发折叠，Redis 共享的是会话状态而不是锁。
- 资源存储目前是专注授权语义的内存实验仓储；进入后续持久化阶段时必须在数据库查询和写入条件中继续执行租户/owner 约束。
