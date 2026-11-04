using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PdfBuilder.Api.OpenApi;

namespace PdfBuilder.Api.Tests;

/// <summary>R12: the OpenAPI document declares X-Api-Key and requires it everywhere except anonymous operations.</summary>
public sealed class ApiKeySecurityTransformerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Protected_operations_require_the_api_key_scheme_and_anonymous_ones_do_not()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer<ApiKeySecurityTransformer>());
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapGet("/protected", () => "x");
        app.MapGet("/files/{fileId}", (string fileId) => fileId).AllowAnonymous();
        await app.StartAsync(Ct);

        var document = JsonDocument.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", Ct)).RootElement;

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());

        var paths = document.GetProperty("paths");
        var security = paths.GetProperty("/protected").GetProperty("get").GetProperty("security");
        Assert.True(Assert.Single(security.EnumerateArray()).TryGetProperty("ApiKey", out _));
        Assert.False(paths.GetProperty("/files/{fileId}").GetProperty("get").TryGetProperty("security", out _));
    }
}
