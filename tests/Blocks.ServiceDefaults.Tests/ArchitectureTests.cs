using System.Reflection;
using System.Text.RegularExpressions;
using Blocks.ServiceDefaults.EntityFramework;

namespace Blocks.ServiceDefaults.Tests;

/// <summary>
/// Constitution Principle IX: the shared packages serve every block, so they may not know any one of
/// them. A reference from a Blocks.* assembly to a block's own assembly fails here, not in review.
/// </summary>
public sealed partial class ArchitectureTests
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

    /// <summary>
    /// The only methods that may read past the <c>"client"</c> query filter. A name that says what it does
    /// is the point: a cross-client read has to be written on purpose and be easy to find (research R2).
    /// </summary>
    private static readonly string[] MethodsAllowedToIgnoreTheClientFilter =
    [
        "DeleteExpiredJobsOfAllClientsAsync",   // the retention sweeper has no caller
        "MarkExpiredFilesOfAllClientsAsync",    // the file sweeper has no caller
        "AssignUnownedToDefaultClientAsync",    // the upgrade gives old rows their owner
        "ClaimAsync"                            // tells another client's job id from a free one, nothing more
    ];

    [GeneratedRegex(@"^\s*(?:public|private|internal|protected)\b[^=;]*?\b(?<name>\w+)\s*(?:<[^>]*>)?\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex MethodDeclaration();

    [Fact]
    public void The_client_query_filter_is_ignored_only_by_the_methods_that_say_so()
    {
        var root = RepositoryRoot();
        var separator = Path.DirectorySeparatorChar;
        var offenders = new List<string>();
        var seen = 0;

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                                 && !p.Contains($"{separator}bin{separator}", StringComparison.Ordinal)))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                // A comment may talk about the filter; only a call counts.
                if (!lines[i].Contains("IgnoreQueryFilters", StringComparison.Ordinal)
                    || lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                seen++;
                var method = EnclosingMethod(lines, i);
                if (method is null || !MethodsAllowedToIgnoreTheClientFilter.Contains(method, StringComparer.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(root, path)}:{i + 1} in {method ?? "?"}");
            }
        }

        Assert.True(seen > 0, "The scan found no IgnoreQueryFilters at all; it is looking in the wrong place.");
        Assert.Empty(offenders);
    }

    /// <summary>The nearest method declaration at or above <paramref name="index"/>.</summary>
    private static string? EnclosingMethod(string[] lines, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            var match = MethodDeclaration().Match(lines[i]);
            if (match.Success && !lines[i].Contains(" class ", StringComparison.Ordinal))
                return match.Groups["name"].Value;
        }

        return null;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PdfBuilder.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("The repository root (PdfBuilder.slnx) was not found above the test output.");
    }
}
