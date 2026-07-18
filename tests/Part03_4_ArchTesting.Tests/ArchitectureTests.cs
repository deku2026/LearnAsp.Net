using System.Net;
using System.Reflection;
using Campus.Testing;
using NetArchTest.Rules;
using Part03_3.Application;
using Part03_3.Domain;
using Part03_3.Infrastructure;

namespace Part03_4_ArchTesting.Tests;

public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Course).Assembly;
    private static readonly Assembly Application = typeof(ICourseRepository).Assembly;
    private static readonly Assembly Infrastructure = typeof(InMemoryCourseRepository).Assembly;

    [Fact]
    public void Domain_does_not_reference_application_or_infrastructure()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny("Part03_3.Application", "Part03_3.Infrastructure", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Application_does_not_reference_infrastructure()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOn("Part03_3.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Handlers_are_sealed_and_suffixed()
    {
        var result = Types.InAssembly(Application)
            .That()
            .ImplementInterface(typeof(ICreateCourseHandler))
            .Should()
            .BeSealed()
            .And()
            .HaveNameEndingWith("Handler")
            .GetResult();

        Assert.True(result.IsSuccessful, Format(result));
    }

    [Fact]
    public void Infrastructure_references_application_and_domain_assemblies()
    {
        var refs = Infrastructure.GetReferencedAssemblies().Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Part03_3.Application", refs);
        Assert.Contains("Part03_3.Domain", refs);
    }

    private static string Format(TestResult result)
        => result.IsSuccessful
            ? "ok"
            : "Violations: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>());
}

public sealed class ArchHostSmokeTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ArchHostSmokeTests(CampusWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Arch_lab_root_ok()
    {
        var r = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Arch_summary_lists_layer_laws()
    {
        var r = await _client.GetAsync("/arch/summary");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}
