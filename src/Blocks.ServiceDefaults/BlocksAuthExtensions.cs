using Blocks.ServiceDefaults.Authentication;
using Blocks.ServiceDefaults.Clients;
using Blocks.ServiceDefaults.Identity;
using Blocks.ServiceDefaults.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
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
        /// Authentication for products (<c>X-Api-Key</c>), for the operator's scripts (the admin key in the
        /// same header) and for the operator's console (a session cookie), behind one <c>Blocks</c> scheme;
        /// one policy per permission; and a fallback policy that requires an authenticated caller, so an
        /// endpoint is closed unless it opts out with <c>AllowAnonymous</c>. The block must also register an
        /// <see cref="IClientKeyStore"/> and an <see cref="IConsoleSessionStore"/> (the EntityFramework package does).
        /// </summary>
        /// <param name="blockCode">The prefix of this block's keys, for example <c>pdf</c>.</param>
        /// <param name="permissions">Every permission a key of this block can hold.</param>
        public IServiceCollection AddBlocksAuthentication(string blockCode, IReadOnlyList<string> permissions)
        {
            ArgumentNullException.ThrowIfNull(permissions);
            return services.AddBlocksAuthentication(blockCode, permissions.Select(name => new PermissionDescriptor(name, name)).ToList());
        }

        /// <summary>As the other overload, with the description the console shows for each permission.</summary>
        public IServiceCollection AddBlocksAuthentication(string blockCode, IReadOnlyList<PermissionDescriptor> permissions)
        {
            ArgumentNullException.ThrowIfNull(permissions);
            var names = permissions.Select(permission => permission.Name).ToList();

            services.AddOptions<BlocksAuthOptions>()
                .Configure<IConfiguration>((options, configuration) =>
                {
                    options.BlockCode = blockCode;
                    options.Permissions = names;
                    options.PermissionDescriptors = permissions;
                    options.AdminKeyHash = configuration[BlocksAuthOptions.AdminKeyHashKey];
                    options.ReadConsoleSettings(configuration);
                })
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BlocksAuthOptions>, BlocksAuthOptionsValidator>());

            // Forwarded headers are believed only from the proxies the operator names.
            services.AddOptions<ForwardedHeadersOptions>()
                .Configure<IOptions<BlocksAuthOptions>>((forwarded, blocks) =>
                {
                    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                    forwarded.ForwardLimit = 1;
                    foreach (var network in blocks.Value.KnownNetworks)
                        forwarded.KnownIPNetworks.Add(network);
                });

            services.TryAddSingleton(TimeProvider.System);
            services.AddProblemDetails();
            services.AddHttpContextAccessor();

            services.TryAddSingleton<KeyCache>();
            services.TryAddSingleton<KeyUsageRecorder>();
            services.TryAddSingleton<SignInThrottle>();
            services.AddHostedService<KeyUsageFlushService>();
            services.TryAddScoped<ICurrentClient, HttpCurrentClient>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IConfigureOptions<CookieAuthenticationOptions>, ConsoleSessionCookie>());

            var cookieName = ConsoleSessionDefaults.CookieName(blockCode);
            services.AddAuthentication(BlocksAuthenticationDefaults.PolicyScheme)
                .AddPolicyScheme(BlocksAuthenticationDefaults.PolicyScheme, null, options =>
                    // A key always wins; the cookie is used only when it is the one credential sent. With
                    // neither, the key handler answers, exactly as before the console had a session.
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(ApiKeyAuthenticationDefaults.HeaderName)
                        || !context.Request.Cookies.ContainsKey(cookieName)
                            ? ApiKeyAuthenticationDefaults.Scheme
                            : ConsoleSessionDefaults.Scheme)
                .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationDefaults.Scheme, null)
                .AddCookie(ConsoleSessionDefaults.Scheme);

            var authorization = services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder(BlocksAuthenticationDefaults.PolicyScheme)
                    .RequireAuthenticatedUser()
                    .Build())
                .AddPolicy(BlocksClaims.AdminScope, policy => policy
                    .AddAuthenticationSchemes(BlocksAuthenticationDefaults.PolicyScheme)
                    .RequireClaim(BlocksClaims.Scope, BlocksClaims.AdminScope))
                .AddPolicy(ConsoleSessionEndpoints.SessionPolicy, policy => policy
                    .AddAuthenticationSchemes(ConsoleSessionDefaults.Scheme)
                    .RequireAuthenticatedUser());

            // Permissions are independent of each other; the operator's authority satisfies every one.
            foreach (var permission in names)
                authorization.AddPolicy(permission, policy => policy
                    .AddAuthenticationSchemes(BlocksAuthenticationDefaults.PolicyScheme)
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context =>
                        context.User.HasClaim(BlocksClaims.Scope, permission)
                        || context.User.HasClaim(BlocksClaims.Scope, BlocksClaims.AdminScope)));

            return services;
        }
    }

    /// <summary>
    /// The stud's part of the pipeline, in order: forwarded headers from known proxies, authentication,
    /// the <c>X-Console</c> rule for cookie-authenticated changes, the <c>X-Client-Id</c> check,
    /// authorization, and the <c>ClientId</c>/<c>KeyId</c> log scope. After <c>UseExceptionHandler</c>.
    /// </summary>
    public static IApplicationBuilder UseBlocksAuthentication(this IApplicationBuilder app)
    {
        app.UseForwardedHeaders();
        app.UseAuthentication();
        app.UseMiddleware<ConsoleHeaderMiddleware>();
        app.UseMiddleware<ActingClientMiddleware>();
        app.UseAuthorization();
        app.UseBlocksClientLogScope();
        return app;
    }

    /// <summary>Puts <c>ClientId</c> and <c>KeyId</c>, or <c>Admin</c>, on every log line of the request. After <c>UseAuthorization</c>.</summary>
    public static IApplicationBuilder UseBlocksClientLogScope(this IApplicationBuilder app) =>
        app.UseMiddleware<ClientLogScopeMiddleware>();

    /// <summary>The management operations under <c>/api/admin</c>, all requiring the operator's authority.</summary>
    public static IEndpointRouteBuilder MapBlocksAdmin(this IEndpointRouteBuilder endpoints)
    {
        ClientAdminEndpoints.Map(endpoints);
        return endpoints;
    }

    /// <summary>Sign-in, session and sign-out for the operator's console under <c>/api/console/session</c>.</summary>
    public static IEndpointRouteBuilder MapBlocksConsole(this IEndpointRouteBuilder endpoints)
    {
        ConsoleSessionEndpoints.Map(endpoints);
        return endpoints;
    }

    /// <summary>The endpoint needs <paramref name="permission"/>; a key without it is refused with <c>AUTH_SCOPE_MISSING</c>.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(permission);
}
