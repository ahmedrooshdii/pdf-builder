using PdfBuilder.Contracts;

namespace PdfBuilder.Domain.Exceptions;

/// <summary>
/// The job id a message asked to use already belongs to another client's job.
/// </summary>
/// <remarks>
/// The message says only that, never whose job it is or what it holds: the id is not a secret, but
/// nothing about another client's job may be described to a caller (feature 003, FR-015). Permanent, so
/// the publisher gets an answer at once and the other client's job is not touched.
/// </remarks>
public sealed class JobIdTakenException()
    : DomainException(PdfErrorCodes.ValidationFailed, "CorrelationId is already used by another job; send a new one.");
