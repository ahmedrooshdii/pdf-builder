using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PdfBuilder.Infrastructure.Persistence;

namespace PdfBuilder.Infrastructure.Tests.Persistence;

/// <summary>
/// Constitution Principle III's gate: every table that holds client data has the client query filter.
/// A table added later with a <c>ClientId</c> and no filter fails here, not in a cross-client leak.
/// Builds the model only; nothing connects to a database.
/// </summary>
public sealed class ClientFilterModelTests
{
    /// <summary>
    /// Entities with a client id that are deliberately not filtered. An API key is read before any client is
    /// known (authentication finds the key, and the client with it), so filtering it by caller could never work.
    /// </summary>
    private static readonly string[] NotFiltered = ["ApiKeyRecord"];

    private static IModel Model()
    {
        using var db = new PdfBuilderDbContextFactory().CreateDbContext([]);
        return db.Model;
    }

    [Fact]
    public void Every_entity_with_a_client_id_has_the_client_filter()
    {
        var withClient = Model().GetEntityTypes()
            .Where(entity => entity.FindProperty("ClientId") is not null)
            .ToList();

        // The two tables of this feature must be among them, or the check proves nothing.
        Assert.Contains(withClient, entity => entity.ClrType.Name == "AuthoredTemplateRecord");
        Assert.Contains(withClient, entity => entity.ClrType.Name == "PdfJobRecord");

        var unfiltered = withClient
            .Where(entity => !NotFiltered.Contains(entity.ClrType.Name, StringComparer.Ordinal))
            .Where(entity => !entity.GetDeclaredQueryFilters().Any(filter => filter.Key == PdfBuilderDbContext.ClientFilter))
            .Select(entity => entity.ClrType.Name)
            .ToList();

        Assert.Empty(unfiltered);
    }
}
