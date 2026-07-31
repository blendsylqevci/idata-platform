using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Skaner IL/metadata (correction R2 i PR #28, §5–§6) mbi binarët REALË —
/// autoriteti i kufirit identity-store (regex-i tekstual mbetet vetëm
/// defense-in-depth). Përdor VETËM System.Reflection.Metadata të
/// framework-ut (zero dependency e re). Rregullat:
///  - në assemblies JO-owner: ndalohen TypeRef-et ndaj TË GJITHA entiteteve
///    të modeluara në skemën `identity` (UserAccountRecord/UserClaimRecord/
///    IdentityUserLogin`1/IdentityUserToken`1 + katër entitetet OpenIddict
///    Application/Authorization/Scope/Token`1 — mbulon edhe Set&lt;T&gt;(),
///    typeof, generics; barazia me modelin REAL imponohet nga meta-testi i
///    plotësisë §5-R3), MemberRef-et ndaj DbSet-eve identity (Users/
///    UserClaims/UserLogins/UserTokens/UserPasskeys mbi IdentityUserContext/
///    PlatformDbContext) dhe emrat e records në blob-et e attributes
///    (bypass me string);
///  - në TË GJITHA production assemblies (edhe owners): ndalohet çdo
///    call/reference ekzekutues ndaj getter/setter-it të inherited
///    `IdentityUserClaim&lt;TKey&gt;.Id` (int) — anëtari mbetet CLR-only,
///    i EF-injoruar dhe i papërdorshëm në repo (CI guard).
/// Blob/signature i palexueshëm → finding fail-closed, kurrë silent pass.
/// </summary>
internal static class IdentityStoreIlScanner
{
    internal sealed record Finding(string Assembly, string Kind, string Detail);

    internal static readonly string[] ForbiddenRecordTypeNames =
        ["UserAccountRecord", "UserClaimRecord"];

    internal static readonly string[] ForbiddenRecordTypePrefixes =
        ["IdentityUserLogin`", "IdentityUserToken`",
         "OpenIddictEntityFrameworkCoreApplication`", "OpenIddictEntityFrameworkCoreAuthorization`",
         "OpenIddictEntityFrameworkCoreScope`", "OpenIddictEntityFrameworkCoreToken`"];

    internal static readonly string[] IdentityDbSetAccessors =
        ["get_Users", "set_Users", "get_UserClaims", "set_UserClaims",
         "get_UserLogins", "set_UserLogins", "get_UserTokens", "set_UserTokens",
         "get_UserPasskeys", "set_UserPasskeys"];

    internal static IReadOnlyList<Finding> Scan(string assemblyPath, bool isStoreOwner)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        var findings = new List<Finding>();

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var portableExecutable = new PEReader(stream);
            var reader = portableExecutable.GetMetadataReader();

            if (!isStoreOwner)
            {
                foreach (var handle in reader.TypeReferences)
                {
                    var typeName = reader.GetString(reader.GetTypeReference(handle).Name);
                    if (ForbiddenRecordTypeNames.Contains(typeName)
                        || ForbiddenRecordTypePrefixes.Any(prefix => typeName.StartsWith(prefix, StringComparison.Ordinal)))
                    {
                        findings.Add(new(assemblyName, "record-type-ref", typeName));
                    }
                }

                foreach (var handle in reader.CustomAttributes)
                {
                    string text;
                    try
                    {
                        text = System.Text.Encoding.UTF8.GetString(
                            reader.GetBlobBytes(reader.GetCustomAttribute(handle).Value));
                    }
                    catch (Exception exception)
                    {
                        findings.Add(new(assemblyName, "unreadable-attribute-blob", exception.GetType().Name));
                        continue;
                    }

                    foreach (var forbidden in ForbiddenRecordTypeNames.Concat(ForbiddenRecordTypePrefixes))
                    {
                        if (text.Contains(forbidden, StringComparison.Ordinal))
                        {
                            findings.Add(new(assemblyName, "record-attribute-string", forbidden));
                        }
                    }
                }
            }

            foreach (var handle in reader.MemberReferences)
            {
                var memberReference = reader.GetMemberReference(handle);
                var memberName = reader.GetString(memberReference.Name);
                var relevantForClaimId = memberName is "get_Id" or "set_Id";
                var relevantForDbSets = !isStoreOwner && IdentityDbSetAccessors.Contains(memberName);
                if (!relevantForClaimId && !relevantForDbSets)
                {
                    continue;
                }

                string parent;
                try
                {
                    parent = DescribeParent(reader, memberReference.Parent);
                }
                catch (Exception exception)
                {
                    // Fail-closed: prind i padekodueshëm i një anëtari relevant.
                    findings.Add(new(assemblyName, "unreadable-member-parent", $"{memberName}: {exception.GetType().Name}"));
                    continue;
                }

                if (relevantForClaimId && parent.Contains("IdentityUserClaim", StringComparison.Ordinal))
                {
                    findings.Add(new(assemblyName, "claim-id-access", $"{memberName} on {parent}"));
                }

                if (relevantForDbSets
                    && (parent.Contains("IdentityUserContext", StringComparison.Ordinal)
                        || parent.Contains("PlatformDbContext", StringComparison.Ordinal)))
                {
                    findings.Add(new(assemblyName, "identity-dbset-access", $"{memberName} on {parent}"));
                }
            }
        }
        catch (Exception exception)
        {
            // Fail-closed: assembly i palexueshëm nuk kalon kurrë silent.
            findings.Add(new(assemblyName, "unreadable-assembly", exception.GetType().Name));
        }

        return findings;
    }

    private static string DescribeParent(MetadataReader reader, EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name),
        HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
        HandleKind.TypeSpecification => DecodeTypeSpecification(reader, (TypeSpecificationHandle)parent),
        HandleKind.MethodDefinition => "method-definition",
        _ => parent.Kind.ToString(),
    };

    private static string DecodeTypeSpecification(MetadataReader reader, TypeSpecificationHandle handle)
    {
        var blobReader = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
        return new SignatureDecoder<string, object?>(TypeNameProvider.Instance, reader, genericContext: null)
            .DecodeType(ref blobReader);
    }

    /// <summary>Provider minimal emrash për dekodim TypeSpec (display-only).</summary>
    private sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
    {
        public static readonly TypeNameProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => DecodeTypeSpecification(reader, handle);

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => $"{genericType}<{string.Join(",", typeArguments)}>";

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[*]";

        public string GetByReferenceType(string elementType) => $"{elementType}&";

        public string GetPointerType(string elementType) => $"{elementType}*";

        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;
    }
}
