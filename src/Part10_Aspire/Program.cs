var builder = DistributedApplication.CreateBuilder(args);
builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(dashboard => dashboard
        .WithHostPort(18888)
        .WithImage(
            "mcr.microsoft.com/dotnet/aspire-dashboard",
            "13.4.2"));
var bindAddress = builder.ExecutionContext.IsPublishMode
    ? "0.0.0.0"
    : "127.0.0.1";

var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var rabbitPassword = builder.AddParameter("rabbit-password", secret: true);
var gatewaySigningKey = builder.AddParameter("gateway-signing-key", secret: true);
var gatewayInternalToken = builder.AddParameter("gateway-internal-token", secret: true);

var postgres = builder.AddPostgres(
        "postgres",
        password: postgresPassword)
    .WithImage("postgres", "18.4-alpine")
    .WithDataVolume();
var catalogDatabase = postgres.AddDatabase("catalog-db", "campus_w7_catalog");
var enrollmentDatabase = postgres.AddDatabase(
    "enrollment-db",
    "campus_w7_enrollment");
var noticesDatabase = postgres.AddDatabase("notices-db", "campus_w7_notices");
var troubleshootingDatabase = postgres.AddDatabase(
    "troubleshooting-db",
    "campus_w8_troubleshooting");

var rabbit = builder.AddRabbitMQ("rabbitmq", password: rabbitPassword)
    .WithManagementPlugin()
    .WithImageTag("4.3.2-management-alpine")
    .WithDataVolume();
var redis = builder.AddRedis("redis")
    .WithImage("redis", "8.8.0-alpine")
    .WithDataVolume();

var catalog = builder.AddProject<Projects.Part07_DistributedComm>("catalog")
    .WithEnvironment("Distributed__Role", "Catalog")
    .WithEnvironment("ConnectionStrings__Catalog", catalogDatabase)
    .WithHttpEndpoint(
        port: 6201,
        targetPort: 6201,
        name: "http",
        isProxied: false)
    .WithHttpEndpoint(
        port: 6301,
        targetPort: 6301,
        name: "grpc",
        isProxied: false)
    .WithEnvironment(
        "Kestrel__Endpoints__Http__Url",
        $"http://{bindAddress}:6201")
    .WithEnvironment("Kestrel__Endpoints__Http__Protocols", "Http1")
    .WithEnvironment(
        "Kestrel__Endpoints__Grpc__Url",
        $"http://{bindAddress}:6301")
    .WithEnvironment("Kestrel__Endpoints__Grpc__Protocols", "Http2")
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(catalogDatabase);

var notices = builder.AddProject<Projects.Part07_DistributedComm>("notices")
    .WithEnvironment("Distributed__Role", "Notices")
    .WithEnvironment("ConnectionStrings__Messaging", noticesDatabase)
    .WithEnvironment("ConnectionStrings__RabbitMQ", rabbit)
    .WithEnvironment("GatewayAuth__InternalToken", gatewayInternalToken)
    .WithHttpEndpoint(
        port: 6203,
        targetPort: 6203,
        name: "http",
        isProxied: false)
    .WithEnvironment(
        "Kestrel__Endpoints__Http__Url",
        $"http://{bindAddress}:6203")
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(noticesDatabase)
    .WaitFor(rabbit);

var enrollment = builder.AddProject<Projects.Part07_DistributedComm>("enrollment")
    .WithEnvironment("Distributed__Role", "Enrollment")
    .WithEnvironment("ConnectionStrings__Enrollment", enrollmentDatabase)
    .WithEnvironment("ConnectionStrings__RabbitMQ", rabbit)
    .WithEnvironment("GatewayAuth__InternalToken", gatewayInternalToken)
    .WithEnvironment("Distributed__CatalogGrpcUrl", catalog.GetEndpoint("grpc"))
    .WithEnvironment("Distributed__NoticesUrl", notices.GetEndpoint("http"))
    .WithHttpEndpoint(
        port: 6202,
        targetPort: 6202,
        name: "http",
        isProxied: false)
    .WithEnvironment(
        "Kestrel__Endpoints__Http__Url",
        $"http://{bindAddress}:6202")
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(catalog)
    .WaitFor(notices)
    .WaitFor(enrollmentDatabase)
    .WaitFor(rabbit);

var gateway = builder.AddProject<Projects.Part07_DistributedComm>("gateway")
    .WithEnvironment("Distributed__Role", "Gateway")
    .WithEnvironment("Distributed__CatalogHttpUrl", catalog.GetEndpoint("http"))
    .WithEnvironment("Distributed__EnrollmentUrl", enrollment.GetEndpoint("http"))
    .WithEnvironment("Distributed__NoticesUrl", notices.GetEndpoint("http"))
    .WithEnvironment("GatewayAuth__SigningKey", gatewaySigningKey)
    .WithEnvironment("GatewayAuth__InternalToken", gatewayInternalToken)
    .WithHttpEndpoint(
        port: 6200,
        targetPort: 6200,
        name: "http",
        isProxied: false)
    .WithEnvironment(
        "Kestrel__Endpoints__Http__Url",
        $"http://{bindAddress}:6200")
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(catalog)
    .WaitFor(enrollment)
    .WaitFor(notices);

var troubleshooting = builder
    .AddProject<Projects.Part08_2_TroubleshootingProcess>("troubleshooting")
    .WithEnvironment(
        "ConnectionStrings__Troubleshooting",
        troubleshootingDatabase)
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(troubleshootingDatabase);

builder.AddProject<Projects.Part08_1_OpenTelemetry>("telemetry-lab")
    .WithEnvironment(
        "Troubleshooting__BaseUrl",
        troubleshooting.GetEndpoint("http"))
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(troubleshooting);

builder.AddProject<Projects.Part09_Deployment>("deployment-lab")
    .WithHttpHealthCheck("/health/ready");

_ = gateway;
_ = redis;

builder.Build().Run();
