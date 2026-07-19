using System.Net;
using System.Reflection;
using Campus.Testing;
using NetArchTest.Rules;
using Part03_2.Catalog;
using Part03_2.Catalog.Contracts;
using Part03_2.Enrollment;
using Part03_2.Enrollment.Contracts;
using Part03_2.Notices;
using Part03_3.Application;
using Part03_3.Contracts;
using Part03_3.Domain;
using Part03_3.Infrastructure;

namespace Part03_4_ArchTesting.Tests;

public sealed class LayerArchitectureTests
{
    private static readonly Assembly Domain = typeof(Course).Assembly;
    private static readonly Assembly Application = typeof(ICourseRepository).Assembly;
    private static readonly Assembly Infrastructure = typeof(InMemoryCourseRepository).Assembly;
    private static readonly Assembly Contracts = typeof(CourseResponse).Assembly;

    [Fact]
    public void Domain_does_not_reference_application_or_infrastructure()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Part03_3.Application",
                "Part03_3.Infrastructure",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();
        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void Application_does_not_reference_infrastructure()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOn("Part03_3.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void Contracts_does_not_reference_domain()
    {
        var refs = Contracts.GetReferencedAssemblies().Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Part03_3.Domain", refs);
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
        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void Handlers_are_internal()
    {
        var handlers = Application.GetTypes()
            .Where(t => typeof(ICreateCourseHandler).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .ToList();
        Assert.NotEmpty(handlers);
        Assert.All(handlers, handler => Assert.True(handler.IsNotPublic, $"{handler.FullName} must be internal."));
    }

    [Fact]
    public void Course_entity_has_private_parameterless_ctor()
    {
        var ctor = typeof(Course).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsPrivate);
    }

    [Fact]
    public void Infrastructure_references_application_and_domain()
    {
        var refs = Infrastructure.GetReferencedAssemblies().Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Part03_3.Application", refs);
        Assert.Contains("Part03_3.Domain", refs);
    }

    private static string Fail(NetArchTest.Rules.TestResult r)
        => "Violations: " + string.Join(", ", r.FailingTypeNames ?? Array.Empty<string>());
}

public sealed class ModuleBoundaryTests
{
    [Fact]
    public void Enrollment_impl_depends_on_catalog_contracts_only_not_impl()
    {
        var refs = typeof(EnrollmentModule).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Part03_2.Catalog.Contracts", refs);
        Assert.DoesNotContain("Part03_2.Catalog", refs);
    }

    [Fact]
    public void Notices_impl_depends_on_enrollment_contracts_only_not_impl()
    {
        var refs = typeof(NoticesModule).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Part03_2.Enrollment.Contracts", refs);
        Assert.DoesNotContain("Part03_2.Enrollment", refs);
    }

    [Fact]
    public void Catalog_impl_does_not_depend_on_enrollment()
    {
        var refs = typeof(CatalogModule).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Part03_2.Enrollment", refs);
        Assert.DoesNotContain("Part03_2.Enrollment.Contracts", refs);
    }

    [Fact]
    public void Catalog_contracts_do_not_depend_on_implementations()
    {
        var refs = typeof(ICatalogModule).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Part03_2.Catalog", refs);
        Assert.DoesNotContain("Part03_2.Enrollment", refs);
        Assert.DoesNotContain("Part03_2.Notices", refs);
    }

    [Fact]
    public void Enrollment_contracts_do_not_depend_on_implementations()
    {
        var refs = typeof(IEnrollmentModule).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Part03_2.Catalog", refs);
        Assert.DoesNotContain("Part03_2.Enrollment", refs);
        Assert.DoesNotContain("Part03_2.Notices", refs);
    }
}

public sealed class ArchHostSmokeTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ArchHostSmokeTests(CampusWebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Arch_lab_root_ok()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Arch_summary_ok()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/arch/summary")).StatusCode);
    }
}
