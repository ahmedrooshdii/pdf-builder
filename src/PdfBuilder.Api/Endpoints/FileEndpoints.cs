using Blocks.ServiceDefaults.Files;
using PdfBuilder.Api.Files;
using PdfBuilder.Application.Abstractions;

namespace PdfBuilder.Api.Endpoints;

/// <summary>
/// The redemption half of the Claim Check pattern: responses carry a URL that points here,
/// so the PDF bytes never travel through RabbitMQ.
/// </summary>
internal static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/files/{fileId}", DownloadAsync)
            .WithTags("Files")
            .WithName("DownloadPdf")
            .WithSummary("Download a generated PDF")
            .WithDescription("The link is the credential. When the deployment signs links, it carries `expires` and `sig` and stops working at `expires`; every other request is answered 404, as for a file that does not exist.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status404NotFound)
            // Open to anyone holding the link: end users and other services follow these links, and they have
            // no key. What the link is worth is decided by RequireFileSignature, not by authentication.
            .AllowAnonymous()
            .RequireFileSignature();

        return app;
    }

    private static async Task<IResult> DownloadAsync(string fileId, HttpContext http, IFileStorage storage, CancellationToken ct)
    {
        var stream = await storage.OpenReadAsync(fileId, ct);

        return stream is null
            ? Results.NotFound()
            : PdfFileResponses.Pdf(http, stream, fileId);
    }
}
