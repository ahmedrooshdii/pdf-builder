using System.ComponentModel.DataAnnotations;

namespace PdfBuilder.Infrastructure.Templates;

public sealed class TemplateStoreOptions
{
    public const string Section = "TemplateStore";

    /// <summary>
    /// Directory holding the built-in <c>{templateKey}.html</c> files. Read-only; CI replaces its
    /// contents on every deploy. Relative paths resolve against the app directory.
    /// </summary>
    [Required]
    public string RootPath { get; init; } = "Templates";

    /// <summary>
    /// Directory for templates written through the API. Must be writable and must survive
    /// deployments, so in a container it belongs on the data volume, never next to
    /// <see cref="RootPath"/>. Relative paths resolve against the app directory.
    /// </summary>
    [Required]
    public string AuthoredRootPath { get; init; } = "data/templates";
}
