using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.EntityFramework;

/// <summary>The Data Protection key ring in the block's own database, shared by every instance.</summary>
public static class DataProtectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Keeps the key ring in <c>data_protection_keys</c> of <typeparamref name="TContext"/>, whose model
        /// must call <see cref="ModelBuilderExtensions.AddBlocksConsoleSessions"/>, so a console session
        /// cookie made by one instance opens on every other one and survives restarts. The application name
        /// is <c>blocks.{blockCode}</c>, from <see cref="BlocksAuthOptions.BlockCode"/>, so another block's
        /// ring can never open this block's cookies.
        /// </summary>
        /// <remarks>
        /// The ring is stored unencrypted, and ASP.NET says so at start-up. That is accepted: a session
        /// also needs a live row whose id only the cookie carries, which a database reader cannot forge.
        /// The repository is written here rather than taken from
        /// <c>Microsoft.AspNetCore.DataProtection.EntityFrameworkCore</c>, whose package brings its own copy
        /// of Data Protection (and its cryptography packages) beside the one in the shared framework.
        /// </remarks>
        public IServiceCollection AddBlocksDataProtection<TContext>()
            where TContext : DbContext
        {
            services.AddDataProtection();
            services.AddOptions<KeyManagementOptions>()
                .Configure<IServiceScopeFactory>((options, scopes) => options.XmlRepository = new EfXmlRepository<TContext>(scopes));
            services.AddOptions<DataProtectionOptions>()
                .Configure<IOptions<BlocksAuthOptions>>((options, blocks) =>
                    options.ApplicationDiscriminator = $"blocks.{blocks.Value.BlockCode}");
            return services;
        }
    }
}

/// <summary>A row of <c>data_protection_keys</c>: one key element of the ring, as XML.</summary>
internal sealed class DataProtectionKeyRecord
{
    public int Id { get; set; }
    public string? FriendlyName { get; set; }
    public string? Xml { get; set; }
}

/// <summary>Data Protection's key storage over a block's context. Each call uses its own scope and context.</summary>
internal sealed class EfXmlRepository<TContext>(IServiceScopeFactory scopes) : IXmlRepository
    where TContext : DbContext
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        return db.Set<DataProtectionKeyRecord>()
            .AsNoTracking()
            .Where(key => key.Xml != null)
            .Select(key => key.Xml!)
            .AsEnumerable()
            .Select(xml => XElement.Parse(xml))
            .ToList()
            .AsReadOnly();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        db.Set<DataProtectionKeyRecord>().Add(new DataProtectionKeyRecord
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting)
        });
        db.SaveChanges();
    }
}
