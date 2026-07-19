# W7 · 消息模式、RabbitMQ 与分布式通信实战

W7 将 `Part06_1_MessagingPatterns`、`Part06_2_MessagingTools` 和
`Part07_DistributedComm` 从占位项目升级为可运行的消息与微服务实验。它不是用内存集合模拟成功路径：
写模型和消息状态落在 PostgreSQL，消息通过 RabbitMQ 4.x 真正路由，Capstone 3 同时启动 Gateway、
Catalog、Enrollment、Notices 四个独立进程并连接三个独立数据库。

## 学习边界与架构

```text
外部客户端
   │ JWT
   ▼
YARP Gateway ── X-Campus-User + 内部令牌 ──┬── REST ──▶ Enrollment ── gRPC ──▶ Catalog
                                          │               │                       │
                                          │               ├── Enrollment PG      └── Catalog PG
                                          │               └── Transactional Outbox
                                          │                         │
                                          └── REST ──▶ Notices ◀── RabbitMQ topic exchange
                                                               │
                                                               └── Inbox + Notices PG
```

- Gateway 是唯一公开入口，验证 JWT 后才注入用户身份。后端同时校验用户头和固定时间比较的内部令牌；
  单独伪造 `X-Campus-User` 会得到 403。生产环境仍必须用私有网络、Secret/KMS，最好升级到 mTLS
  或工作负载身份。
- Catalog、Enrollment、Notices 使用不同连接串和不同数据库，避免共享数据库造成的隐式耦合。
- 外部同步接口使用 REST；内部强类型查询使用 gRPC；跨边界状态通知使用 RabbitMQ 异步事件。
- 逻辑服务地址来自配置。本地使用明确 URL，生产环境可由 Aspire service reference、
  Kubernetes Service/DNS 或服务注册中心提供相同逻辑地址。

## Part06_1：可靠消息模式

Enrollment 和 Outbox 在同一个 EF Core/PostgreSQL 事务中提交，消除了“业务提交成功但消息没写入”
的双写窗口。后台 relay 使用 `FOR UPDATE SKIP LOCKED` 抢占批次，允许多个实例并行工作而不重复
领取同一行。消息发布后、标记成功前崩溃仍会重投，因此系统明确采用 **at-least-once**，不宣称
传输层 exactly-once。

消费者把 Inbox 的 `message_id` 唯一约束、`INSERT ... ON CONFLICT DO NOTHING` 和业务副作用放在
同一数据库事务中。这样重复投递仍然存在，但同一个消费者的业务效果只发生一次。这里实现的是
exactly-once effect，而不是不可能跨任意故障边界保证的 exactly-once transport。

失败路径也是真实状态：

- 瞬时失败使用指数退避和 jitter，避免恢复时同步重试风暴；
- 超过最大次数或确定性 poison message 进入 PostgreSQL dead-letter 表；
- Saga 编排器持久化 Payment、SeatReservation 和 Compensation 状态，座位失败时发出退款补偿；
- Saga 使用 PostgreSQL `xmin` 做乐观并发检查，避免两个回调覆盖状态。

实验中的轮询 relay 适合教学和中等吞吐；大规模生产还应设计已处理 Outbox 的分区/归档清理，
并评估 CDC（例如 Debezium）替代高频轮询。Saga 只能用补偿恢复业务一致性，不能把跨服务流程
伪装成 ACID 事务；补偿本身也必须幂等。

## Part06_2：RabbitMQ 真连接

项目直接使用 `RabbitMQ.Client`，将 broker 访问集中在本地适配器中，没有引入 MassTransit。
这是为了在实验中看清 publisher confirm、channel、ack/nack、prefetch、binding 和 DLX 的真实语义，
不是对上层框架的一般性否定；实际选型还要重新审查功能、维护成本与当时许可证。

声明的持久拓扑如下：

| 用途 | Exchange / Queue | 行为 |
|---|---|---|
| 领域事件 | `campus.events`（topic） | `enrollment.*` 路由到通知队列 |
| 命令演示 | `campus.commands`（direct） | 精确 routing key |
| 广播演示 | `campus.broadcasts`（fanout） | 忽略 routing key |
| 延迟重试 | `campus.retry` → `campus.notifications.retry.v1` | TTL 到期后 dead-letter 回事件 exchange |
| 最终死信 | `campus.dead-letter` → `campus.notifications.dlq.v1` | 达到上限或 poison 后保留排查 |

发布端使用持久消息、mandatory routing 和 publisher confirms；消费者使用长连接、自动恢复、
手动 ack、有限 prefetch，并在异步回调退出前复制 RabbitMQ client 交付的 body。成功提交
PostgreSQL Inbox/副作用后才 ack；瞬时失败 ack 原消息并发布到 retry queue；最终失败 reject，
由 DLX 路由到 DLQ。

RabbitMQ 适合低延迟工作队列、灵活路由和逐消息确认；Kafka 更接近可重放的分区日志，顺序和
消费者位点模型不同；Azure Service Bus 等托管 broker 则以云集成和运维托管换取平台约束。
这些产品不能只按 API 外观替换，必须重新评估顺序、重放、保留、背压和故障语义。

## Part07：Capstone 3

一个可部署程序集按 `Distributed:Role` 启动为四个独立进程。这保留共享构建产物的便利，但每个角色
具有独立端口、生命周期、健康检查和数据库；未来可以按 bounded context 拆成独立程序集，而不改变
网络契约。

完整链路为：

1. 客户端携带 JWT 调用 Gateway；
2. Gateway 鉴权、移除路由前缀并代理到 Enrollment；
3. Enrollment 通过 gRPC unary 查询 Catalog；
4. Enrollment 在本地 PostgreSQL 事务中写入 enrollment 与 outbox；
5. relay 将事件以 publisher confirm 发布到 RabbitMQ；
6. Notices 消费事件，把 Inbox 和通知副作用原子写入自己的 PostgreSQL；
7. 客户端经 Gateway 查询通知。

gRPC 同时实现 unary `GetCourse` 和 server-streaming `WatchAvailability`。Enrollment 的 gRPC 和
Notices typed `HttpClient` 使用 .NET 标准 resilience handler，配置 attempt/total timeout、retry 和
circuit breaker；测试会证明重试发生后断路器打开，并验证打开期间不再触达下游。

所有进程分别暴露 `/health/live` 和 `/health/ready`。live 只表示进程存活；ready 验证当前角色所需的
PostgreSQL、RabbitMQ 或 Gateway destinations。`X-Correlation-ID` 在入口校验后贯穿同步和异步链路。
W8 会把这个关联基础升级为 OpenTelemetry trace/baggage，而不是在 W7 提前制造不完整观测方案。

微服务拆分不是目标本身。真实迁移应先找边界和独立变化率，再用 strangler pattern 渐进切流；
部署使用 rolling、blue/green 或 canary 时，都必须配合向后兼容契约、数据库 expand/contract 和
可回滚消息版本。本实验事件名显式带 `.v1`，消费者对未知版本应隔离而不是静默误解。

## 使用已启动的 WSL 基础设施

当前 Docker Compose 的 PostgreSQL 和 RabbitMQ 已映射到 WSL localhost。首次运行
`Part06_1` 时 Development 环境会应用 EF Core migration：

```bash
cd /mnt/c/MyFile/ArcForges/LearnAsp.Net/.worktrees/campus-w7-part06-part07

dotnet run --project src/Part06_1_MessagingPatterns
dotnet run --project src/Part06_2_MessagingTools
```

Capstone 需要三个数据库。用 compose 中的管理员账号创建一次：

```bash
for db in campus_w7_catalog campus_w7_enrollment campus_w7_notices; do
  docker exec dotnet-postgres psql -U dotnet -d postgres \
    -c "SELECT 1 FROM pg_database WHERE datname = '$db'" -tA |
    grep -q 1 ||
    docker exec dotnet-postgres createdb -U dotnet "$db"
done
```

随后在四个终端分别设置角色和监听地址。Catalog 还要配置单独的 HTTP/2 端口：

```bash
Distributed__Role=Catalog \
Kestrel__Endpoints__Http__Url=http://127.0.0.1:6021 \
Kestrel__Endpoints__Http__Protocols=Http1 \
Kestrel__Endpoints__Grpc__Url=http://127.0.0.1:6121 \
Kestrel__Endpoints__Grpc__Protocols=Http2 \
dotnet run --project src/Part07_DistributedComm

Distributed__Role=Notices ASPNETCORE_URLS=http://127.0.0.1:6023 \
dotnet run --project src/Part07_DistributedComm

Distributed__Role=Enrollment ASPNETCORE_URLS=http://127.0.0.1:6022 \
dotnet run --project src/Part07_DistributedComm

Distributed__Role=Gateway ASPNETCORE_URLS=http://127.0.0.1:6020 \
dotnet run --project src/Part07_DistributedComm
```

仓库中的 signing key 和内部令牌只供 localhost 学习环境。生产部署必须通过环境/Secret 注入，
要求 HTTPS，限制后端网络入口，并轮换这些凭据。

## 真实测试边界

Windows/macOS 只运行跨平台通用测试。需要 PostgreSQL、RabbitMQ 或多进程网络的 W7 测试仅在
Debian WSL/Linux 运行：

```bash
export CAMPUS_W7_PG='Host=127.0.0.1;Port=5432;Database=postgres;Username=dotnet;Password=dotnet_dev'
export CAMPUS_W7_RABBITMQ='amqp://dotnet:dotnet_dev@127.0.0.1:5672/'

dotnet test tests/Part06_1_MessagingPatterns.Tests -c Release
dotnet test tests/Part06_2_MessagingTools.Tests -c Release
dotnet test tests/Part07_DistributedComm.Tests -c Release
```

三组测试覆盖真实事务回滚边界、relay 崩溃重投、Inbox 去重、并发 `SKIP LOCKED`、指数重试、
poison/DLQ、Saga 补偿、RabbitMQ 三种 exchange、publisher confirm、manual ack、TTL retry、DLX，
以及 Gateway 鉴权、伪造身份拒绝、gRPC unary/stream、resilience/circuit、PG×3、RabbitMQ 和四进程
健康检查。未连接基础设施时 Linux CI 会使用 Testcontainers；仓库的 Linux workflow 则显式安装并
复用固定版本 PostgreSQL 18.4 与 RabbitMQ 4.3.2 服务。
