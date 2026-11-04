using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults;

/// <summary>How a block takes the authentication stud: register, add to the pipeline, protect endpoints.</summary>
public static class BlocksAuthExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// The <c>X-Api-Key</c> scheme, one policy per permission, and a fallback policy that requires an
        /// authenticated caller, so an endpoint is closed unless it opts out with <c>AllowAnonymous</c>.
        /// The block must also register an <see cref="IClientKeyStore"/> (the EntityFramework package does).
        /// </summary>
        /// <param name="blockCode">The prefix of this block's keys, for example <c>pdf</c>.</param>
        /// <param name="permissions">Every permission a key of this block can hold.</param>
        public IServiceCollection AddBlocksAuthentication(string blockCode, IReadOnlyList<string> permissions)
        {
            ArgumentNullException.ThrowIfNull(permissions);

            services.AddOptions<BlocksAuthOptions>()
                .Configure<IConfiguration>((options, configuration) =>
                {
                    options.BlockCode = blockCode;
                    options.Permissions = permissions;
                    options.AdminKeyHash = configuration[BlocksAuthOptions.AdminKeyHashKey];
                })
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BlocksAuthOptions>, BlocksAuthOptionsValidator>());

            services.TryAddSingleton(TimeProvider.System);
            services.AddProblemDetails();
            services.AddHttpContextAccessor();

            services.TryAddSingleton<KeyCache>();
            services.TryAddSingleton<KeyUsageRecorder>();
            services.AddHostedService<KeyUsageFlushService>();
            services.TryAddScoped<ICurrentClient, HttpCurrentClient>();

            services.AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
                .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationDefaults.Scheme, null);

            var authorization = services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder(ApiKeyAuthenticationDefaults.Scheme)
                    .RequireAuthenticatedUser()
                    .Build())
                .AddPolicy(BlocksClaims.AdminScope, policy => policy
                    .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme)
                    .RequireClaim(BlocksClaims.Scope, BlocksClaims.AdminScope));

            // Permissions are independent of each other; the admin key's authority satisfies every one.
            foreach (var permission in permissions)
                authorization.AddPolicy(permission, policy => policy
                    .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme)
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context =>
                        context.User.HasClaim(BlocksClaims.Scope, permission)
                        || context.User.HasClaim(BlocksClaims.Scope, BlocksClaims.AdminScope)));

            return services;
        }
    }

    /// <summary>Puts <c>ClientId</c> and <c>KeyId</c>, or <c>Admin</c>, on every log line of the request. After <c>UseAuthorization</c>.</summary>
    public static IApplicationBuilder UseBlocksClientLogScope(this IApplicationBuilder app) =>
        app.UseMiddleware<ClientLogScopeMiddleware>();

    /// <summary>The management operations under <c>/api/admin/clients</c>, all requiring the admin key.</summary>
    public static IEndpointRouteBuilder MapBlocksAdmin(this IEndpointRouteBuilder endpoints)
    {
        ClientAdminEndpoints.Map(endpoints);
        return endpoints;
    }

    /// <summary>The endpoint needs <paramref name="permission"/>; a key without it is refused with <c>AUTH_SCOPE_MISSING</c>.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(permission);
}
