using FluentValidation;
using MediatR;

namespace PdfBuilder.Application.Behaviors;

/// <summary>
/// Runs every registered validator for a request before its handler. Handlers can therefore assume
/// a well-formed request and carry no defensive checks of their own.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var applicable = validators as IValidator<TRequest>[] ?? [.. validators];
        if (applicable.Length != 0)
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(applicable.Select(v => v.ValidateAsync(context, cancellationToken)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToArray();

            if (failures.Length != 0)
                throw new ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}
