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
    /// Where the previous version kept templates written through the API, as one JSON file each.
    /// Templates now live in the database; this directory is read <b>once</b>, on the first start
    /// against an empty database, to import what is there, and is never written or changed. After a
    /// successful upgrade it can be removed. Relative paths resolve against the app directory.
    /// </summary>
    [Required]
    public string AuthoredRootPath { get; init; } = "data/templates";
}
