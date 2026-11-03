using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PdfBuilder.Api.Endpoints;
using PdfBuilder.Domain.Exceptions;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// How a failure becomes a response. The new case is the database being unreachable: callers
/// need a status that says "retry", and nothing in the body may reveal where the database is.
/// </summary>
public sealed class ApiExceptionHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(DefaultHttpContext Http, bool Handled, JsonElement Body)> HandleAsync(Exception exception)
    {
        // AddOptions: the web host registers it, a bare ServiceCollection does not.
        await using var provider = new ServiceCollection().AddOptions().AddProblemDetails().BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = new MemoryStream();

        var handled = await new ApiExceptionHandler(provider.GetRequiredService<IProblemDetailsService>())
            .TryHandleAsync(http, exception, Ct);

        http.Response.Body.Position = 0;
        var body = handled
            ? (await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: Ct)).RootElement.Clone()
            : default;

        return (http, handled, body);
    }

    [Fact]
    public async Task An_unreachable_database_is_a_503_that_says_when_to_retry()
    {
        var unreachable = new NpgsqlException("Failed to connect to 10.0.0.7:5432", new SocketException());

        var (http, handled, body) = await HandleAsync(unreachable);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, http.Response.StatusCode);
        Assert.Equal("5", http.Response.Headers.RetryAfter.ToString());
        Assert.Equal("STORAGE_UNAVAILABLE", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_response_never_repeats_what_the_provider_said_about_the_connection()
    {
        var leaky = new NpgsqlException(
            "Failed to connect to secret-host.internal: Host=secret-host.internal;Password=hunter2",
            new SocketException());

        var (_, _, body) = await HandleAsync(leaky);

        var text = body.GetRawText();
        Assert.DoesNotContain("secret-host", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrapped_connection_failure_is_recognised_too()
    {
        var wrapped = new InvalidOperationException("saving failed", new NpgsqlException("down", new TimeoutException()));

        var (http, handled, _) = await HandleAsync(wrapped);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, http.Response.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_database_error_is_not_mistaken_for_an_outage()
    {
        // A permanent error (a missing database, a bad password) will not heal by retrying.
        var (http, handled, _) = await HandleAsync(new InvalidOperationException("some other failure"));

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
    }

    [Fact]
    public async Task Existing_mappings_are_unchanged()
    {
        var (http, handled, body) = await HandleAsync(new TemplateNotFoundException("invoice"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, http.Response.StatusCode);
        Assert.False(http.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal("TEMPLATE_NOT_FOUND", body.GetProperty("code").GetString());
    }
}
