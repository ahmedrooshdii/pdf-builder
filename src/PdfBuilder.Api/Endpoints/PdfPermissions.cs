using Blocks.ServiceDefaults.Authentication;

namespace PdfBuilder.Api.Endpoints;

/// <summary>
/// What a PDF Builder key can be allowed to do. Independent of each other: writing templates does not
/// include reading them. The operator's authority (admin key or console session) satisfies all of them.
/// </summary>
internal static class PdfPermissions
{
    /// <summary>The prefix of this block's keys: <c>pdf_{keyId}_{secret}</c>.</summary>
    public const string BlockCode = "pdf";

    /// <summary>Submit jobs and read them.</summary>
    public const string Generate = "pdf:generate";

    /// <summary>List and read templates.</summary>
    public const string TemplatesRead = "pdf:templates:read";

    /// <summary>Create, duplicate, edit, publish, delete and preview templates.</summary>
    public const string TemplatesWrite = "pdf:templates:write";

    public static IReadOnlyList<string> All { get; } = [Generate, TemplatesRead, TemplatesWrite];

    /// <summary>The same permissions with what each allows, as the console's create-key form lists them.</summary>
    public static IReadOnlyList<PermissionDescriptor> Descriptors { get; } =
    [
        new(Generate, "Submit jobs and read them."),
        new(TemplatesRead, "List and read templates."),
        new(TemplatesWrite, "Create, duplicate, edit, publish, delete and preview templates.")
    ];
}
