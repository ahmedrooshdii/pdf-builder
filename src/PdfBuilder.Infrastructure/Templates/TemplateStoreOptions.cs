using System.ComponentModel.DataAnnotations;

namespace PdfBuilder.Infrastructure.Templates;

public sealed class TemplateStoreOptions
{
    public const string Section = "TemplateStore";

    /// <summary>Directory holding <c>{templateId}.html</c> files. Relative paths resolve against the app directory.</summary>
    [Required]
    public string RootPath { get; init; } = "Templates";
}
