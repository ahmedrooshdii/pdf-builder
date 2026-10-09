using System.ComponentModel.DataAnnotations;

namespace PdfBuilder.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Directory rendered PDFs are written to. Relative paths resolve against the app directory.</summary>
    [Required]
    public string RootPath { get; init; } = "data/files";

    /// <summary>
    /// Externally reachable base URL of this service, used to build the download URL that goes
    /// back to the caller. Behind a reverse proxy this is the public address, not the container's.
    /// </summary>
    [Required]
    [Url]
    public string PublicBaseUrl
    {
        // C# 14 `field`: normalise once on the way in, so URL building never doubles a slash.
        get;
        init => field = value.TrimEnd('/');
    } = "http://localhost:8080";
}
