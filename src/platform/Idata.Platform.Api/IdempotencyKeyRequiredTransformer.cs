using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Idata.Platform.Api;

/// <summary>
/// Shënon parametrin header `Idempotency-Key` si të DETYRUESHËM në dokumentin
/// OpenAPI (concern i composition root-it). Runtime-i e mban parametrin
/// nullable që handler-i të prodhojë vetë 400-in STABIL
/// (INVALID_IDEMPOTENCY_KEY) kur mungon/është i pavlefshëm; ky transformer
/// siguron që kontrata publike ta deklarojë required.
/// </summary>
public sealed class IdempotencyKeyRequiredTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.OperationId == "UpdateMembershipRole")
        {
            var parameter = operation.Parameters?.FirstOrDefault(p =>
                string.Equals(p.Name, "Idempotency-Key", StringComparison.Ordinal));
            if (parameter is OpenApiParameter concrete)
            {
                concrete.Required = true;
            }
        }

        return Task.CompletedTask;
    }
}
