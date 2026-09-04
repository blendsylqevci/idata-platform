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
        (typeof(OpenIddictServerAspNetCoreBuilder), "EnableTokenEndpointPassthrough"),
        (typeof(OpenIddictValidationBuilder), "UseIntrospection"),
        (typeof(OpenIddictValidationBuilder), "SetClientId"),
        (typeof(OpenIddictValidationBuilder), "SetClientSecret"),
    ];

    /// <summary>
    /// B.2b — ZËVENDËSIM I KONTROLLUAR (jo heqje): passthrough-i i endpoint-it të
    /// AUTORIZIMIT del nga lista "e ndaluar kudo" dhe hyn në listën "vetëm te owner-i
    /// i aprovuar i interaksionit" (Identity.Infrastructure), sepse login/consent-i
    /// real kërkon dorëzimin e kërkesës TË VALIDUAR te sipërfaqja jonë. Passthrough-i
    /// i TOKEN endpoint-it mbetet i ndaluar KUDO: lëshimi i token-ave nuk kalon kurrë
    /// nga kodi ynë.
    /// </summary>
    private static readonly (Type Builder, string Member)[] InteractionOwnerOnlyMembers =
    [
        (typeof(OpenIddictServerAspNetCoreBuilder), "EnableAuthorizationEndpointPassthrough"),
    ];

    [Fact]
    public void Lista_e_ndaluar_perputhet_me_API_ne_reale_te_OpenIddict_7_6_0_asnje_hyrje_e_vdekur()
    {
        foreach (var (builder, member) in ForbiddenBuilderMembers.Concat(InteractionOwnerOnlyMembers))
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

            // Passthrough-i i autorizimit lejohet VETËM te owner-i i interaksionit.
            if (!string.Equals(assembly, IdentityInfrastructureAssembly, StringComparison.Ordinal))
            {
                foreach (var (builder, member) in InteractionOwnerOnlyMembers)
                {
                    Assert.DoesNotContain((builder.FullName!, member), members);
                }
            }
        }
    }

    /// <summary>Guard-i i zëvendësimit nuk është i vdekur: owner-i i interaksionit e përdor REALISHT.</summary>
    [Fact]
    public void Passthrough_i_autorizimit_ekziston_vetem_te_owner_i_i_interaksionit()
    {
        var members = MemberReferencesByAssembly()[IdentityInfrastructureAssembly];
        foreach (var (builder, member) in InteractionOwnerOnlyMembers)
        {
            Assert.Contains((builder.FullName!, member), members);
        }
    }

    /// <summary>
    /// B.2b — wiring-u i KËRKUAR i interaksionit te owner-i (login real, session
    /// cookie e veçantë, antiforgery, rate limiting) dhe te composition root-i
    /// (regjistrim + middleware + mapping), që asnjë kufi të mos jetë deklarativ.
    /// </summary>
    [Fact]
    public void Owner_i_interaksionit_dhe_composition_root_i_lidhin_kufijte_realisht()
    {
        var references = MemberReferencesByAssembly();
        var owner = references[IdentityInfrastructureAssembly];
        foreach (var required in (string[])["AddSignInManager", "AddCookie", "AddAntiforgery", "AddRateLimiter"])
        {
            Assert.Contains(owner, reference => reference.Member == required);
        }

        var host = references[ApiAssembly];
        Assert.Contains(
            ("Idata.Platform.Modules.Identity.Infrastructure.IdentityInteractiveAuthorizationServiceCollectionExtensions",
             "AddIdentityInteractiveAuthorization"),
            host);
        Assert.Contains(
            ("Idata.Platform.Modules.Identity.Presentation.InteractiveAuthorizationEndpoints",
             "MapIdentityInteractiveAuthorization"),
            host);
        foreach (var required in (string[])["UseRateLimiter", "UseAntiforgery"])
        {
            Assert.Contains(host, reference => reference.Member == required);
        }
    }

    /// <summary>
    /// Sipërfaqja interaktive mbetet e NGUSHTË: zero protokoll, zero store, zero
    /// kriptografi tokenash në assembly-n e Presentation-it.
    /// </summary>
    [Fact]
    public void Presentation_i_interaksionit_nuk_prek_protokollin_as_store_in()
    {
        var members = MemberReferencesByAssembly()["Idata.Platform.Modules.Identity.Presentation"];
        Assert.NotEmpty(members);
        Assert.DoesNotContain(members, reference =>
            reference.Type.StartsWith("OpenIddict", StringComparison.Ordinal)
            || reference.Type.StartsWith("Microsoft.IdentityModel", StringComparison.Ordinal)
            || reference.Type.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal)
            || reference.Type.StartsWith("Microsoft.Extensions.Identity", StringComparison.Ordinal)
            || reference.Type.StartsWith("Idata.Platform.Persistence", StringComparison.Ordinal));
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

    // ═══════ A2 (C1) — startup failure state: API-shape mbi sipërfaqen PUBLIKE ═══════

    /// <summary>
    /// Gjendja e dështimeve të startup-it NUK është API publike: asnjë tip i
    /// eksportuar nuk ka mutator `Fail`, asnjë veti publike `Failures` nuk kthen
    /// listë të ndryshueshme (as `IReadOnlyList` mbi `List`) — vetëm
    /// `ImmutableArray&lt;string&gt;`; tipi i state-it nuk eksportohet. Zero
    /// reflection mbi fusha private.
    /// </summary>
    [Fact]
    public void Startup_failure_state_nuk_eshte_public_dhe_snapshot_i_eshte_ImmutableArray()
    {
        var assembly = typeof(Idata.Platform.Modules.Identity.Infrastructure.IdentityAuthorityOptions).Assembly;
        var exported = assembly.GetExportedTypes();
        Assert.DoesNotContain(exported, type => type.Name.Contains("StartupState", StringComparison.Ordinal));
        foreach (var type in exported)
        {
            Assert.DoesNotContain(type.GetMethods(), method => method.Name == "Fail");
            foreach (var property in type.GetProperties().Where(property => property.Name == "Failures"))
            {
                Assert.Equal(typeof(System.Collections.Immutable.ImmutableArray<string>), property.PropertyType);
                Assert.Null(property.SetMethod);
            }
        }

        // Snapshot-i (jo-publik) ekziston me VETËM `ImmutableArray<string> Failures` pa setter.
        var snapshot = Assert.Single(assembly.GetTypes(), type => type.Name == "IdentityAuthorityStartupSnapshot");
        Assert.False(snapshot.IsPublic);
        var failures = Assert.Single(snapshot.GetProperties(), property => property.Name == "Failures");
        Assert.Equal(typeof(System.Collections.Immutable.ImmutableArray<string>), failures.PropertyType);
        Assert.Null(failures.SetMethod);
    }

    /// <summary>A1 (C1): zero predikat design-time / përjashtim runtime sipas identitetit të procesit.</summary>
    [Fact]
    public void Zero_perjashtim_runtime_sipas_identitetit_te_procesit()
    {
        var assembly = typeof(Idata.Platform.Modules.Identity.Infrastructure.IdentityAuthorityOptions).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            foreach (var member in type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain("DesignTime", member.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("GetDocument", member.Name, StringComparison.Ordinal);
            }
        }

        var members = MemberReferencesByAssembly()[IdentityInfrastructureAssembly];
        Assert.DoesNotContain(("System.Environment", "GetCommandLineArgs"), members);
        Assert.DoesNotContain(("System.Environment", "get_CommandLine"), members);
        Assert.DoesNotContain(("System.Environment", "ProcessPath"), members);
        Assert.DoesNotContain(("System.Environment", "get_ProcessPath"), members);
        Assert.DoesNotContain(("System.Diagnostics.Process", "GetCurrentProcess"), members);
        Assert.DoesNotContain(("System.AppDomain", "get_FriendlyName"), members);
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
