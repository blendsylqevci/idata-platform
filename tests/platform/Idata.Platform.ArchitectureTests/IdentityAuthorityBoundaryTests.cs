using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Kufiri i authority-t OpenIddict co-located (ADR-028, B.2a) mbi ARTEFAKTET
/// production (MemberRefs nga snapshot-et e verifikuara të katalogut):
///  - ZERO grant/veti të ndaluara në ÇDO assembly production (client-credentials,
///    password, implicit, hybrid, refresh, device, custom/none, anonymous clients,
///    degraded mode, ignore-permissions, passthrough, TLS-off, introspection);
///  - wiring-u i KËRKUAR prezent te owner-i i protokollit (code+PKCE, issuer,
///    endpoint-e, credentials, UseAspNetCore, UseLocalServer, token-entry
///    validation, audiences) dhe te composition root-i (AddIdentityAuthority,
///    UseAuthentication/UseAuthorization) — guard-et s'janë të vdekura;
///  - neutralitet produkti (zero PLANET/KABI) dhe zero material sekret në src.
/// </summary>
public sealed class IdentityAuthorityBoundaryTests
{
    private const string IdentityInfrastructureAssembly = "Idata.Platform.Modules.Identity.Infrastructure";
    private const string ApiAssembly = "Idata.Platform.Api";
    private const string DependencyInjectionNamespace = "Microsoft.Extensions.DependencyInjection";

    /// <summary>(tip i plotë i parent-it, anëtar) për çdo MemberRef të çdo assembly production.</summary>
    private static IReadOnlyDictionary<string, HashSet<(string Type, string Member)>> MemberReferencesByAssembly()
    {
        var result = new Dictionary<string, HashSet<(string, string)>>(StringComparer.Ordinal);
        foreach (var entry in PlatformProjectCatalog.DiscoverProduction())
        {
            // Rruga e sanksionuar (C6 call-site parity): ReadVerifiedImage → Read.
            var image = PlatformProjectCatalog.ReadVerifiedImage(entry);
            using var stream = image.OpenImage();
            using var portableExecutable = new PEReader(stream);
            var reader = portableExecutable.GetMetadataReader();
            var set = new HashSet<(string, string)>();
            foreach (var handle in reader.MemberReferences)
            {
                var member = reader.GetMemberReference(handle);
                if (member.Parent.Kind != HandleKind.TypeReference)
                {
                    continue;
                }

                var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                var ns = reader.GetString(type.Namespace);
                var name = reader.GetString(type.Name);
                set.Add((string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}", reader.GetString(member.Name)));
            }

            result[entry.AssemblyName] = set;
        }

        Assert.Contains(IdentityInfrastructureAssembly, result.Keys);
        Assert.Contains(ApiAssembly, result.Keys);
        return result;
    }

    // ═══════ Anëtarë të NDALUAR në production (çdo assembly) ═══════

    private static readonly (Type Builder, string Member)[] ForbiddenBuilderMembers =
    [
        (typeof(OpenIddictServerBuilder), "AllowClientCredentialsFlow"),
        (typeof(OpenIddictServerBuilder), "AllowPasswordFlow"),
        (typeof(OpenIddictServerBuilder), "AllowImplicitFlow"),
        (typeof(OpenIddictServerBuilder), "AllowHybridFlow"),
        (typeof(OpenIddictServerBuilder), "AllowRefreshTokenFlow"),
        (typeof(OpenIddictServerBuilder), "AllowDeviceAuthorizationFlow"),
        (typeof(OpenIddictServerBuilder), "AllowCustomFlow"),
        (typeof(OpenIddictServerBuilder), "AllowNoneFlow"),
        (typeof(OpenIddictServerBuilder), "AcceptAnonymousClients"),
        (typeof(OpenIddictServerBuilder), "EnableDegradedMode"),
        (typeof(OpenIddictServerBuilder), "IgnoreEndpointPermissions"),
        (typeof(OpenIddictServerBuilder), "IgnoreGrantTypePermissions"),
        (typeof(OpenIddictServerBuilder), "IgnoreScopePermissions"),
        (typeof(OpenIddictServerBuilder), "IgnoreResponseTypePermissions"),
        (typeof(OpenIddictServerBuilder), "DisableAccessTokenEncryption"),
        (typeof(OpenIddictServerBuilder), "DisableTokenStorage"),
        (typeof(OpenIddictServerAspNetCoreBuilder), "DisableTransportSecurityRequirement"),
        (typeof(OpenIddictServerAspNetCoreBuilder), "EnableAuthorizationEndpointPassthrough"),
        (typeof(OpenIddictServerAspNetCoreBuilder), "EnableTokenEndpointPassthrough"),
        (typeof(OpenIddictValidationBuilder), "UseIntrospection"),
        (typeof(OpenIddictValidationBuilder), "SetClientId"),
        (typeof(OpenIddictValidationBuilder), "SetClientSecret"),
    ];

    [Fact]
    public void Lista_e_ndaluar_perputhet_me_API_ne_reale_te_OpenIddict_7_6_0_asnje_hyrje_e_vdekur()
    {
        foreach (var (builder, member) in ForbiddenBuilderMembers)
        {
            Assert.True(builder.GetMethods().Any(candidate => candidate.Name == member),
                $"Anëtari i ndaluar nuk ekziston në API-në reale: {builder.Name}.{member}");
        }
    }

    [Fact]
    public void Zero_grant_passthrough_tls_off_ose_introspection_ne_asnje_assembly_production()
    {
        var references = MemberReferencesByAssembly();
        foreach (var (assembly, members) in references)
        {
            foreach (var (builder, member) in ForbiddenBuilderMembers)
            {
                Assert.DoesNotContain((builder.FullName!, member), members);
            }

            // Asnjë referencë me emër të ndaluar drejt ÇDO tipi (edhe extension-e).
            Assert.DoesNotContain(members, reference =>
                ForbiddenBuilderMembers.Any(forbidden => forbidden.Member == reference.Member)
                && reference.Type.StartsWith(DependencyInjectionNamespace, StringComparison.Ordinal)
                && reference.Type.Contains("OpenIddict", StringComparison.Ordinal));
            _ = assembly;
        }
    }

    // ═══════ Wiring i KËRKUAR (guard-et jo të vdekura) ═══════

    [Fact]
    public void Owner_i_protokollit_referon_wiring_un_e_plote_server_dhe_validation()
    {
        var members = MemberReferencesByAssembly()[IdentityInfrastructureAssembly];
        var server = typeof(OpenIddictServerBuilder).FullName!;
        var validation = typeof(OpenIddictValidationBuilder).FullName!;
        foreach (var required in (string[])
            ["AllowAuthorizationCodeFlow", "RequireProofKeyForCodeExchange", "SetIssuer",
             "SetTokenEndpointUris", "SetAuthorizationEndpointUris",
             "AddSigningCertificate", "AddEncryptionCertificate",
             "AddEphemeralSigningKey", "AddEphemeralEncryptionKey",
             "AddDevelopmentSigningCertificate", "AddDevelopmentEncryptionCertificate",
             "AddEventHandler"])
        {
            Assert.Contains((server, required), members);
        }

        foreach (var required in (string[])["AddAudiences", "EnableTokenEntryValidation", "Configure"])
        {
            Assert.Contains((validation, required), members);
        }

        // Extension-et: UseAspNetCore (server + validation) dhe UseLocalServer (ServerIntegration).
        Assert.Contains(($"{DependencyInjectionNamespace}.OpenIddictServerAspNetCoreExtensions", "UseAspNetCore"), members);
        Assert.Contains(($"{DependencyInjectionNamespace}.OpenIddictValidationAspNetCoreExtensions", "UseAspNetCore"), members);
        Assert.Contains(($"{DependencyInjectionNamespace}.OpenIddictValidationServerIntegrationExtensions", "UseLocalServer"), members);
        Assert.Contains(($"{DependencyInjectionNamespace}.AuthenticationServiceCollectionExtensions", "AddAuthentication"), members);
        Assert.Contains(($"{DependencyInjectionNamespace}.PolicyServiceCollectionExtensions", "AddAuthorization"), members);
    }

    [Fact]
    public void Composition_root_i_konsumon_authority_n_pa_referuar_OpenIddict_dhe_ndez_middleware_in_real()
    {
        var members = MemberReferencesByAssembly()[ApiAssembly];
        Assert.Contains(
            ("Idata.Platform.Modules.Identity.Infrastructure.IdentityAuthorityServiceCollectionExtensions", "AddIdentityAuthority"),
            members);
        Assert.Contains(("Microsoft.AspNetCore.Builder.AuthAppBuilderExtensions", "UseAuthentication"), members);
        Assert.Contains(("Microsoft.AspNetCore.Builder.AuthorizationAppBuilderExtensions", "UseAuthorization"), members);
        Assert.DoesNotContain(members, reference =>
            reference.Type.StartsWith("OpenIddict", StringComparison.Ordinal)
            || reference.Type.StartsWith("Microsoft.IdentityModel", StringComparison.Ordinal)
            || (reference.Type.StartsWith(DependencyInjectionNamespace, StringComparison.Ordinal)
                && reference.Type.Contains("OpenIddict", StringComparison.Ordinal)));
    }

    // ═══════ Neutralitet produkti + zero material sekret ═══════

    private static readonly Regex ProductNames = new("PLANET|KABI", RegexOptions.CultureInvariant);

    // Pattern-i ndërtohet me bashkim që burimi i këtij file-i të mos përmbajë vetë header-at PEM.
    private static readonly Regex SecretMaterial = new(
        string.Concat("-----BEGIN ", "[A-Z ]*PRIVATE KEY-----", "|", "-----BEGIN ", "CERTIFICATE-----", "|MII[A-Za-z0-9+/]{60,}"),
        RegexOptions.CultureInvariant);

    private static IEnumerable<string> SourceFiles(string relativeRoot)
    {
        var root = Path.Combine(PlatformProjectCatalog.RepoRoot(), relativeRoot);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets" or ".json");
    }

    [Fact]
    public void Zero_emra_produkti_ne_src_platform_dhe_guard_i_gjalle()
    {
        Assert.Matches(ProductNames, "client-PLANET-x");
        Assert.Matches(ProductNames, "aud:KABI");
        foreach (var file in SourceFiles("src/platform"))
        {
            Assert.False(ProductNames.IsMatch(File.ReadAllText(file)), $"Emër produkti në src: {file}");
        }
    }

    [Fact]
    public void Zero_material_sekret_ne_src_dhe_tests_dhe_zero_appsettings_ne_host()
    {
        // Fixture-t ndërtohen në runtime (burimi i këtij file-i s'përmban vetë pattern-in).
        Assert.Matches(SecretMaterial, string.Concat("-----BEGIN ", "RSA PRIVATE KEY-----"));
        Assert.Matches(SecretMaterial, string.Concat("-----BEGIN ", "CERTIFICATE-----"));
        Assert.Matches(SecretMaterial, "MII" + new string('A', 70));
        foreach (var file in SourceFiles("src/platform").Concat(SourceFiles("tests/platform")))
        {
            Assert.False(SecretMaterial.IsMatch(File.ReadAllText(file)), $"Material sekret në: {file}");
        }

        var apiRoot = Path.Combine(PlatformProjectCatalog.RepoRoot(), "src", "platform", "Idata.Platform.Api");
        Assert.Empty(Directory.EnumerateFiles(apiRoot, "appsettings*.json", SearchOption.TopDirectoryOnly));
    }
}
