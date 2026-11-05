using Blocks.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Http;

namespace Blocks.ServiceDefaults.Problems;

/// <summary>
/// The refusal for an operation that needs a client when the caller has none: the operator who has not
/// chosen one. Public so that a block's own exception handler, for a write that reached its store with no
/// client, answers exactly as the stud's endpoint filter does.
/// </summary>
public static class ClientRequiredProblem
{
    /// <summary>What the caller is told to do.</summary>
    public const string Message = "Choose a client: send X-Client-Id with the client to act as.";

    /// <summary><c>400</c>, code <c>VALIDATION_FAILED</c>, with the message under the <c>X-Client-Id</c> error.</summary>
    public static HttpValidationProblemDetails Create() =>
        BlocksProblems.Validation(new Dictionary<string, string[]>
        {
            [ConsoleSessionDefaults.ClientIdHeaderName] = [Message]
        });
}
