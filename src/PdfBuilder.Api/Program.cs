using Blocks.ServiceDefaults;
using PdfBuilder.Api.Endpoints;
using PdfBuilder.Api.Messaging;
using PdfBuilder.Api.OpenApi;
using PdfBuilder.Application;
using PdfBuilder.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Give in-flight renders time to finish on SIGTERM instead of being cut off mid-job
// by a rolling deployment or a `docker stop`.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(60));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddMessaging(builder.Configuration)
    .AddProblemDetails()
    .AddOpenApi(options => options.AddDocumentTransformer<ApiKeySecurityTransformer>());

// Every endpoint needs a key or a console session unless it says otherwise below. There is no setting
// that turns this off.
builder.Services.AddBlocksAuthentication(PdfPermissions.BlockCode, PdfPermissions.Descriptors);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
// Forwarded headers from known proxies, authentication, the console rules, authorization, log scope.
app.UseBlocksAuthentication();

// Docs are open in non-production only. Put them behind authentication before enabling in production.
if (!app.Environment.IsProduction())
{
    app.MapOpenApi().AllowAnonymous();                          // /openapi/v1.json
    app.MapScalarApiReference(options => options
        .WithTitle("PDF Builder API")
        .WithTheme(ScalarTheme.DeepSpace)).AllowAnonymous();   // /scalar/v1
}

// Liveness answers "is the process up"; restarting on a failed dependency check would be wrong,
// so dependency checks belong to readiness, which only removes the instance from the load balancer.
// Both stay open: load balancers and orchestrators have no key.
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.MapPdfJobEndpoints();
app.MapFileEndpoints();
app.MapTemplateEndpoints();
app.MapBlocksAdmin();
app.MapBlocksConsole();

app.Run();

/// <summary>Exposed so the test host can reference this assembly's entry point.</summary>
public partial class Program;
