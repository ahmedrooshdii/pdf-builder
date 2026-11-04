using Blocks.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PdfBuilder.Api.OpenApi;

/// <summary>
/// Declares the <c>X-Api-Key</c> scheme in the OpenAPI document and requires it on every operation
/// that is not anonymous, so Scalar and generated clients send the header.
/// </summary>
internal sealed class ApiKeySecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyAuthenticationDefaults.Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthenticationDefaults.HeaderName,
            Description = "A client key (pdf_…) issued by the operator, or the admin key (adm_…)."
        };

        // The operations that opted out with AllowAnonymous, as "METHOD /path".
        var anonymous = context.DescriptionGroups
            .SelectMany(group => group.Items)
            .Where(description => description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            .Select(description => $"{description.HttpMethod} /{description.RelativePath}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationDefaults.Scheme, document)] = []
        };

        foreach (var (path, item) in document.Paths)
        {
            foreach (var (method, operation) in item.Operations ?? [])
            {
                if (!anonymous.Contains($"{method.Method} {path}"))
                    operation.Security = [requirement];
            }
        }

        return Task.CompletedTask;
    }
}
