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
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> DownloadAsync(string fileId, IFileStorage storage, CancellationToken ct)
    {
        var stream = await storage.OpenReadAsync(fileId, ct);

        return stream is null
            ? Results.NotFound()
            : Results.File(stream, "application/pdf", $"{fileId}.pdf");
    }
}
