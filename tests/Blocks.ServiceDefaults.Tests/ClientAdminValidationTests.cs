using Blocks.ServiceDefaults.Clients;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>FR-005 and FR-007: the rules for client ids, names, permissions and expiry.</summary>
public sealed class ClientAdminValidationTests
{
    private static readonly IReadOnlyList<string> Permissions = ["pdf:generate", "pdf:templates:read", "pdf:templates:write"];
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("ecommerce")]
    [InlineData("e")]
    [InlineData("erp-2")]
    [InlineData("a234567890123456789012345678901234567890")]   // 40 characters
    public void Valid_client_ids_pass(string id)
    {
        Assert.Empty(ClientAdminValidation.ValidateCreate(new CreateClientRequest(id, "E-commerce")));
    }

    [Theory]
    [InlineData("E-Commerce")]
    [InlineData("1abc")]
    [InlineData("-abc")]
    [InlineData("e_commerce")]
    [InlineData("e commerce")]
    [InlineData("")]
    [InlineData("a2345678901234567890123456789012345678901")]  // 41 characters
    public void Invalid_client_ids_name_the_id(string id)
    {
        var errors = ClientAdminValidation.ValidateCreate(new CreateClientRequest(id, "E-commerce"));

        Assert.Equal(["id"], errors.Keys);
    }

    [Fact]
    public void A_missing_id_is_refused()
    {
        Assert.Contains("id", ClientAdminValidation.ValidateCreate(new CreateClientRequest(null, "E-commerce")).Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_client_needs_a_name(string? name)
    {
        Assert.Equal(["name"], ClientAdminValidation.ValidateCreate(new CreateClientRequest("ecommerce", name)).Keys);
    }

    [Fact]
    public void Names_are_at_most_100_characters_after_trimming()
    {
        Assert.Empty(ClientAdminValidation.ValidateCreate(new CreateClientRequest("ecommerce", "  " + new string('n', 100) + "  ")));
        Assert.Equal(["name"], ClientAdminValidation.ValidateCreate(new CreateClientRequest("ecommerce", new string('n', 101))).Keys);
    }

    [Fact]
    public void An_update_may_leave_the_name_out_but_not_blank_it()
    {
        Assert.Empty(ClientAdminValidation.ValidateUpdate(new UpdateClientRequest(null, true)));
        Assert.Equal(["name"], ClientAdminValidation.ValidateUpdate(new UpdateClientRequest(" ", null)).Keys);
    }

    [Fact]
    public void A_key_with_a_name_and_known_permissions_and_no_expiry_passes()
    {
        var request = new IssueKeyRequest("production", ["pdf:generate", "pdf:templates:read"], null);

        Assert.Empty(ClientAdminValidation.ValidateIssue(request, Permissions, Now));
    }

    [Fact]
    public void A_key_needs_at_least_one_permission_and_the_message_lists_them()
    {
        var none = ClientAdminValidation.ValidateIssue(new IssueKeyRequest("production", [], null), Permissions, Now);
        var missing = ClientAdminValidation.ValidateIssue(new IssueKeyRequest("production", null, null), Permissions, Now);

        Assert.Contains("pdf:templates:write", Assert.Single(none["scopes"]), StringComparison.Ordinal);
        Assert.Contains("scopes", missing.Keys);
    }

    [Theory]
    [InlineData("pdf:everything")]
    [InlineData("admin")]
    [InlineData("eml:send")]
    public void Unknown_permissions_and_admin_are_refused_naming_the_known_ones(string scope)
    {
        var errors = ClientAdminValidation.ValidateIssue(new IssueKeyRequest("production", ["pdf:generate", scope], null), Permissions, Now);

        var message = Assert.Single(errors["scopes"]);
        Assert.Contains(scope, message, StringComparison.Ordinal);
        Assert.Contains("pdf:generate, pdf:templates:read, pdf:templates:write", message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_expiry_must_be_in_the_future()
    {
        Assert.Contains("expiresAt", ClientAdminValidation.ValidateIssue(
            new IssueKeyRequest("production", ["pdf:generate"], Now), Permissions, Now).Keys);
        Assert.Contains("expiresAt", ClientAdminValidation.ValidateIssue(
            new IssueKeyRequest("production", ["pdf:generate"], Now.AddMinutes(-1)), Permissions, Now).Keys);
        Assert.Empty(ClientAdminValidation.ValidateIssue(
            new IssueKeyRequest("production", ["pdf:generate"], Now.AddMinutes(1)), Permissions, Now));
    }
}
