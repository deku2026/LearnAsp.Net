using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Step02_DIConfigOptions.Tests;

public sealed class DiOptionsTests
{
    [Fact]
    public async Task Di_demo_shows_lifetime_rules()
    {
        await using var factory = new CampusWebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var json = await client.GetFromJsonAsync<JsonElement>("/di-demo");
        Assert.False(json.GetProperty("transient").GetProperty("same").GetBoolean());
        Assert.True(json.GetProperty("scoped").GetProperty("same").GetBoolean());
        Assert.NotEqual(Guid.Empty, json.GetProperty("singleton").GetGuid());
    }

    [Fact]
    public async Task Options_endpoint_returns_bound_values()
    {
        await using var factory = new CampusWebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Step02-DI", json.GetProperty("options").GetProperty("labName").GetString());
    }

    [Fact]
    public void Invalid_options_fail_on_start()
    {
        using var factory = new CampusWebApplicationFactory<Program>()
            .WithSetting("CampusLab:LabName", "invalid");

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var scope = factory.Services.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Step02_DIConfigOptions.CampusLabOptions>>().Value;
        });

        Assert.NotNull(ex);
    }
}
