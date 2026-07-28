using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Idata.Platform.Api;

/// <summary>
/// Ruajtja e RENDIT KANONIK të kontratës së commit-uar
/// (contracts/openapi/platform-v1.json): moduli teknik i host-it
/// ("Idata.Platform.Api") para "AccessControlEndpoints". Pa këtë, kalimi i
/// PlatformStatus nga minimal API te MVC controllers (ADR-029, Increment A)
/// do t'i zhvendoste paths/tags në fund të dokumentit dhe do të prishte
/// byte-identitetin e kontratës pa asnjë ndryshim semantik. Renditja është
/// STABILE (rendi relativ brenda të njëjtit tag ruhet) dhe deterministe.
/// </summary>
internal sealed class CanonicalContractOrderTransformer : IOpenApiDocumentTransformer
{
    private static readonly string[] TagPrecedence = ["Idata.Platform.Api", "AccessControlEndpoints"];

    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths is { Count: > 0 })
        {
            var ordered = document.Paths.OrderBy(pair => Precedence(FirstTagOf(pair.Value))).ToArray();
            document.Paths.Clear();
            foreach (var (path, item) in ordered)
            {
                document.Paths.Add(path, item);
            }
        }

        if (document.Tags is { Count: > 0 })
        {
            var orderedTags = document.Tags.OrderBy(tag => Precedence(tag.Name)).ToArray();
            document.Tags.Clear();
            foreach (var tag in orderedTags)
            {
                document.Tags.Add(tag);
            }
        }

        return Task.CompletedTask;
    }

    private static string? FirstTagOf(IOpenApiPathItem item)
        => item.Operations?.Values.FirstOrDefault()?.Tags?.FirstOrDefault()?.Name;

    private static int Precedence(string? tagName)
    {
        var index = Array.IndexOf(TagPrecedence, tagName ?? string.Empty);
        return index < 0 ? TagPrecedence.Length : index;
    }
}
