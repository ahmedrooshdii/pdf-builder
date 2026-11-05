namespace Blocks.ServiceDefaults.Authentication;

/// <summary>A permission a client key of a block can hold, with the one line the console shows for it.</summary>
/// <param name="Name">The scope, for example <c>pdf:generate</c>.</param>
/// <param name="Description">What it allows, as a sentence, for example "Submit jobs and read them."</param>
public sealed record PermissionDescriptor(string Name, string Description);
