using System.Reflection;
using Blocks.ServiceDefaults.EntityFramework;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// Constitution Principle IX: the shared packages serve every block, so they may not know any one of
/// them. A reference from a Blocks.* assembly to a block's own assembly fails here, not in review.
/// </summary>
public sealed class ArchitectureTests
{
    public static TheoryData<string> SharedAssemblies() =>
    [
        typeof(BlocksAuthExtensions).Assembly.GetName().Name!,
        typeof(ModelBuilderExtensions).Assembly.GetName().Name!,
        "Blocks.Cli"
    ];

    [Theory]
    [MemberData(nameof(SharedAssemblies))]
    public void A_shared_assembly_references_no_block_assembly(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var blockReferences = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("PdfBuilder", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(blockReferences);
    }
}
