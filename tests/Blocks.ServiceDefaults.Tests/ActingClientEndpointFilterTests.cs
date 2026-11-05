using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blocks.ServiceDefaults.Tests.Support;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// Feature 003, User Story 5: an endpoint that is about one client's data. A client key is that client; the
/// operator has to choose one, and the one chosen has to exist. contracts/http-client-scoping.md.
/// </summary>
public sealed class ActingClientEndpointFilterTests
{
    private const string ChooseAClient = "Choose a client: send X-Client-Id with the client to act as.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpClient Acting(HttpClient client, string clientId)
    {
        client.DefaultRequestHeaders.Add("X-Client-Id", clientId);
        return client;
    }

    [Fact]
    public async Task A_client_key_passes_and_costs_no_lookup()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", [StudHost.Generate], host.Clock.Now);
        var before = host.Store.ClientReads;

        var answer = await host.Client(key).GetFromJsonAsync<JsonElement>("/acting", Ct);

        Assert.Equal("ecommerce", answer.GetProperty("clientId").GetString());
        Assert.Equal(before, host.Store.ClientReads);
    }

    [Fact]
    public async Task A_client_key_cannot_choose_another_client_with_the_header()
    {
        await using var host = await StudHost.StartAsync();
        var key = host.Store.Seed("ecommerce", [StudHost.Generate], host.Clock.Now);

        var answer = await Acting(host.Client(key), "erp").GetFromJsonAsync<JsonElement>("/acting", Ct);

        Assert.Equal("ecommerce", answer.GetProperty("clientId").GetString());
    }

    [Fact]
    public async Task The_operator_with_no_client_is_asked_to_choose_one()
    {
        await using var host = await StudHost.StartAsync();

        using var response = await host.Admin().GetAsync("/acting", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("VALIDATION_FAILED", problem.GetProperty("code").GetString());
        Assert.Equal([ChooseAClient], problem.GetProperty("errors").GetProperty("X-Client-Id").EnumerateArray().Select(m => m.GetString()));
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task The_operator_acting_as_a_client_that_is_not_registered_is_told_it_does_not_exist()
    {
        await using var host = await StudHost.StartAsync();

        using var response = await Acting(host.Admin(), "nobody").GetAsync("/acting", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("CLIENT_NOT_FOUND", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_operator_acting_as_a_registered_client_works_inside_it()
    {
        await using var host = await StudHost.StartAsync();
        await host.RegisterAsync("ecommerce", Ct);

        var answer = await Acting(host.Admin(), "ecommerce").GetFromJsonAsync<JsonElement>("/acting", Ct);

        Assert.Equal("ecommerce", answer.GetProperty("clientId").GetString());
    }

    [Fact]
    public async Task The_operator_can_work_inside_a_disabled_client()
    {
        await using var host = await StudHost.StartAsync();
        await host.RegisterAsync("ecommerce", Ct);
        using var disabled = await host.Admin().PatchAsJsonAsync("/api/admin/clients/ecommerce", new { disabled = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        // Only the client's own keys are refused (feature 002); the operator can still inspect and prepare its data.
        using var response = await Acting(host.Admin(), "ecommerce").GetAsync("/acting", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_console_session_is_held_to_the_same_rules_as_the_admin_key()
    {
        await using var host = await StudHost.StartAsync();
        await host.RegisterAsync("ecommerce", Ct);
        var cookie = await host.SignInAsync(Ct);

        using var none = await host.Session(cookie).GetAsync("/acting", Ct);
        using var unknown = await Acting(host.Session(cookie), "nobody").GetAsync("/acting", Ct);
        var chosen = await Acting(host.Session(cookie), "ecommerce").GetFromJsonAsync<JsonElement>("/acting", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("ecommerce", chosen.GetProperty("clientId").GetString());
    }

    [Fact]
    public async Task A_storage_outage_while_checking_the_chosen_client_is_a_503_never_client_not_found()
    {
        await using var host = await StudHost.StartAsync();
        await host.RegisterAsync("ecommerce", Ct);
        host.Store.Unavailable = true;

        using var response = await Acting(host.Admin(), "ecommerce").GetAsync("/acting", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("5", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("STORAGE_UNAVAILABLE", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Authentication_and_permission_are_checked_before_the_client()
    {
        await using var host = await StudHost.StartAsync();
        var readOnly = host.Store.Seed("ecommerce", [StudHost.TemplatesRead], host.Clock.Now);

        using var anonymous = await host.Client().GetAsync("/acting", Ct);
        using var noPermission = await host.Client(readOnly).GetAsync("/acting", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noPermission.StatusCode);
    }
}
