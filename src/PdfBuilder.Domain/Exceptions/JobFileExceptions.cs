namespace PdfBuilder.Domain.Exceptions;

/// <summary>
/// Codes for job-file failures. HTTP-only, like <see cref="TemplateErrorCodes"/>: they never travel on
/// the message bus, so they are not part of the published contract.
/// </summary>
public static class JobFileErrorCodes
{
    public const string JobHasNoFile = "JOB_HAS_NO_FILE";
    public const string FileExpired = "FILE_EXPIRED";
}

/// <summary>The job has no file to link to: it is still pending or it failed.</summary>
/// <remarks>The message names no job and no file.</remarks>
public sealed class JobHasNoFileException()
    : DomainException(JobFileErrorCodes.JobHasNoFile, "The job has no file: it did not succeed.");

/// <summary>The job's file was deleted after the retention period, or is gone from storage.</summary>
/// <remarks>The message names no job and no file.</remarks>
public sealed class FileExpiredException()
    : DomainException(JobFileErrorCodes.FileExpired, "The file has expired and was deleted. Submit the job again to get a new one.");
