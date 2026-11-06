namespace PdfBuilder.Api.Files;

/// <summary>How a PDF is served, whichever endpoint serves it: never cached, never leaked through a referrer, never indexed.</summary>
internal static class PdfFileResponses
{
    /// <summary>A PDF download as a result. The response headers are set before the body is written.</summary>
    public static IResult Pdf(HttpContext http, Stream content, string fileId)
    {
        var headers = http.Response.Headers;
        headers.CacheControl = "private, no-store";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Robots-Tag"] = "noindex";

        return Results.File(content, "application/pdf", $"{fileId}.pdf");
    }
}
