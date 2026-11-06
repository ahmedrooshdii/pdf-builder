using Blocks.ServiceDefaults.Files;
using Microsoft.Extensions.Options;
using PdfBuilder.Infrastructure.Storage;

namespace PdfBuilder.Api.Files;

/// <summary>A link a holder can GET without credentials, and when it stops working (null: it does not expire).</summary>
public readonly record struct FileLink(string Url, DateTimeOffset? ExpiresAt);

/// <summary>
/// The one place file links are made, from a file id and the service's public address. Links are made
/// when a caller asks for one, never stored: with signing on, every call makes a fresh one.
/// </summary>
/// <remarks>
/// With <c>Blocks:Files:RequireSignature</c> off the link is the plain <c>/files/{fileId}</c> and has no expiry,
/// exactly as before signing existed; with it on, the link carries a signature and stops working after
/// <c>Blocks:Files:UrlLifetime</c>.
/// </remarks>
public sealed class PdfFileLinks(
    IOptions<StorageOptions> storage,
    IOptions<BlocksFilesOptions> files,
    FileLinkSigner signer,
    TimeProvider clock)
{
    private readonly string _baseUrl = storage.Value.PublicBaseUrl;

    public FileLink For(string fileId)
    {
        var url = $"{_baseUrl}/files/{fileId}";
        if (!files.Value.RequireSignature)
            return new FileLink(url, null);

        var signed = signer.Sign(fileId, clock.GetUtcNow());
        return new FileLink($"{url}?{signed.Query}", signed.ExpiresAt);
    }
}
