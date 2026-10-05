using Idata.Platform.Modules.AccessControl.Presentation;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Idata.Platform.Api;

/// <summary>
/// Deklaron `roleId` (uuid) dhe `expectedVersion` (int64) të DETYRUESHME dhe
/// JO-null në dokumentin OpenAPI (concern i composition root-it). Runtime-i i mban
/// fushat nullable VETËM që mungesa të dallohet nga vlerat legjitime, dhe
/// endpoint-i prodhon vetë 400-in STABIL (INVALID_REQUEST_BODY) kur mungojnë ose
/// janë null; ky transformer siguron që kontrata publike të mbetet e pandryshuar
/// (njësoj si `IdempotencyKeyRequiredTransformer` për header-in).
/// </summary>
public sealed class UpdateMembershipRoleRequestContractTransformer : IOpenApiSchemaTransformer
{
    private static readonly string[] RequiredProperties = ["roleId", "expectedVersion"];

    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(UpdateMembershipRoleRequest) && schema.Properties is { } properties)
        {
            foreach (var name in RequiredProperties)
            {
                if (properties.TryGetValue(name, out var property) && property is OpenApiSchema { Type: { } type } concrete)
                {
                    concrete.Type = type & ~JsonSchemaType.Null;
                }

                (schema.Required ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
            }
        }

        return Task.CompletedTask;
    }
}
