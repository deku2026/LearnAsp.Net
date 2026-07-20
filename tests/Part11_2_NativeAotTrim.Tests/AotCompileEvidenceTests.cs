using System.Reflection;
using Part11_2_NativeAotTrim.Endpoints;

namespace Part11_2_NativeAotTrim.Tests;

public class AotCompileEvidenceTests
{
    [Fact]
    public void EndpointsLibraryExposesStaticallyDiscoveredMapMethod()
    {
        var method = typeof(CourseEndpoints).GetMethod(
            nameof(CourseEndpoints.MapCourseEndpoints),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
    }

    [Fact]
    public async Task AotAppCsprojDeclaresPublishAotAndIsAotCompatible()
    {
        var csprojPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Part11_2_NativeAotTrim", "Part11_2_NativeAotTrim.csproj");
        var csproj = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("<PublishAot>true</PublishAot>", csproj);
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", csproj);
    }

    [Fact]
    public async Task EndpointsLibraryCsprojDeclaresRdgAndAotCompatible()
    {
        var csprojPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Part11_2_NativeAotTrim.Endpoints", "Part11_2_NativeAotTrim.Endpoints.csproj");
        var csproj = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("<EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>", csproj);
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", csproj);
    }
}