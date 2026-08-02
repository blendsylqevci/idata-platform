using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// ZERO Identity roles — imponim STRUKTUROR (correction R3 i PR #28, §7):
/// traversal REKURSIV, cycle-safe, mbi tipet e TË GJITHA assemblies
/// production të derivuara nga katalogu kanonik (§3 — jo listë e hardcoded).
/// Universi i markerëve të ndaluar derivohet MEKANIKISHT nga tipet publike
/// role-specifike të framework-ut të pinuar (meta-test barazie më poshtë) +
/// një shtesë e dokumentuar (IdentityDbContext — konteksti mbart-role).
/// Mbulimi i pretenduar = vetëm ai i provuar nga fixtures diskriminuese me
/// LOKATOR të synuar dege — property/event fixtures janë pa backing-fields
/// që dega përkatëse të mos kalojë fshehurazi përmes degës së fields.
/// </summary>
public sealed class IdentityRoleSurfaceTests
{
    /// <summary>
    /// §2-R4: assemblies të DERIVUARA nga katalogu kanonik (MSBuild real) dhe
    /// të ngarkuara me identitet të VERIFIKUAR kundër TargetPath (MVID match).
    /// </summary>
    private static readonly Assembly[] AllPlatformAssemblies =
        [.. PlatformProjectCatalog.DiscoverProduction().Select(PlatformProjectCatalog.LoadVerified)];

    [Fact]
    public void Zero_Identity_roles_ne_asnje_tip_te_platformes_traversal_rekursiv()
    {
        Assert.NotEmpty(AllPlatformAssemblies);
        foreach (var assembly in AllPlatformAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var finding = RoleSurfaceScanner.FindForbiddenSurface(type);
                Assert.True(finding is null, $"{type.FullName}: {finding}");
            }
        }
    }

    /// <summary>
    /// Meta-testi i katalogut (§3-R3): seti i assemblies të skanuara ==
    /// saktësisht seti i AssemblyName-ve të katalogut kanonik — zero
    /// zëvendësim i heshtur në load.
    /// </summary>
    [Fact]
    public void Assemblies_e_skanuara_perputhen_me_katalogun_kanonik()
    {
        var loaded = AllPlatformAssemblies
            .Select(assembly => assembly.GetName().Name ?? string.Empty)
            .Order(StringComparer.Ordinal).ToArray();
        var catalog = PlatformProjectCatalog.DiscoverProduction()
            .Select(entry => entry.AssemblyName)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(catalog, loaded);
        Assert.Equal(loaded.Length, loaded.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Meta-testi i PLOTËSISË së markerëve (§7-R3): universi role-specifik
    /// derivohet MEKANIKISHT nga tipet publike me "Role" në emër të tre
    /// assemblies të pinuara të store-it (Extensions.Identity.Core/.Stores +
    /// AspNetCore.Identity.EntityFrameworkCore) dhe duhet të jetë saktësisht
    /// markerët − shtesa e vetme e dokumentuar (IdentityDbContext). Tip i ri
    /// role-specifik në framework OSE marker i vdekur = RED.
    /// </summary>
    [Fact]
    public void Markeret_e_ndaluar_perputhen_me_universin_role_specifik_te_framework_ut()
    {
        Assembly[] pinnedFrameworkAssemblies =
            [typeof(RoleManager<>).Assembly,
             typeof(IdentityRole<>).Assembly,
             typeof(RoleStore<,,>).Assembly];

        var derived = pinnedFrameworkAssemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsPublic && type.Name.Contains("Role", StringComparison.Ordinal))
            .Select(type => (type.FullName ?? type.Name).Split('`')[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] documentedExtras =
            ["Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext"];
        var markers = RoleSurfaceScanner.ForbiddenMarkers
            .Except(documentedExtras, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(derived, markers);
    }

    /// <summary>
    /// Fixtures diskriminuese — SECILA degë provohet me LOKATOR të synuar:
    /// gjetja duhet të vijë nga dega e pretenduar, jo rastësisht nga një
    /// degë tjetër (p.sh. backing-field në vend të property-t).
    /// </summary>
    [Theory]
    [InlineData(typeof(FixtureBaseExposure), "base type", "/base")]
    [InlineData(typeof(IFixtureInterfaceExposure), "interface", "/interface")]
    [InlineData(typeof(FixtureFieldExposure), "field", "/field:manager")]
    [InlineData(typeof(FixturePropertyExposure), "property", "/property:Store")]
    [InlineData(typeof(FixtureConstructorExposure), "constructor parameter", "/ctor-param:manager")]
    [InlineData(typeof(FixtureMethodParameterExposure), "method parameter", "/param:Consume.link")]
    [InlineData(typeof(FixtureMethodReturnExposure), "method return", "/return:Produce")]
    [InlineData(typeof(FixtureEventExposure), "event", "/event:Changed")]
    [InlineData(typeof(FixtureGenericArgumentExposure), "generic argument", "/property:Items/generic-arg")]
    [InlineData(typeof(FixtureGenericConstraintExposure<>), "generic constraint", "/generic-constraint")]
    [InlineData(typeof(FixtureAttributeExposure), "attribute Type argument", "/attribute/ctor-arg")]
    [InlineData(typeof(FixtureNestedExposure), "nested type", "/nested:Inner")]
    [InlineData(typeof(FixtureMethodGenericConstraintExposure), "METHOD generic constraint", "/method-generic-constraint:Consume")]
    [InlineData(typeof(FixtureMethodAttributeExposure), "attribute mbi method", "/method-attr:Marked")]
    [InlineData(typeof(FixturePropertyAttributeExposure), "attribute mbi property", "/property-attr:Value")]
    [InlineData(typeof(FixtureFieldAttributeExposure), "attribute mbi field", "/field-attr:Field")]
    [InlineData(typeof(FixtureEventAttributeExposure), "attribute mbi event", "/event-attr:Changed")]
    [InlineData(typeof(FixtureParameterAttributeExposure), "attribute mbi parameter", "/param-attr:Consume.value")]
    [InlineData(typeof(FixtureReturnAttributeExposure), "attribute mbi return value", "/return-attr:Produce")]
    [InlineData(typeof(FixtureGenericParameterAttributeExposure<>), "attribute mbi generic parameter", "/generic-param-attr:T")]
    [InlineData(typeof(FixtureNamedAttributeArgumentExposure), "named attribute argument", "/named-arg:TypeValue")]
    [InlineData(typeof(FixtureArrayAttributeArgumentExposure), "array attribute argument", "/ctor-arg/element")]
    [InlineData(typeof(FixtureByRefExposure), "by-ref parameter", "/param:Consume.role")]
    [InlineData(typeof(FixtureArrayFieldExposure), "array element type", "/field:Roles")]
    [InlineData(typeof(FixtureDeepGenericExposure), "deep generic nesting", "/property:Deep")]
    [InlineData(typeof(FixtureRoleValidatorExposure), "IRoleValidator (§7-R3)", "/property:Validator")]
    [InlineData(typeof(FixtureUserRoleStoreExposure), "IUserRoleStore (§7-R3)", "/field:store")]
    public void Traversal_i_kap_te_gjitha_llojet_e_ekspozimit_fixtures(
        Type fixture, string branch, string expectedLocator)
    {
        var finding = RoleSurfaceScanner.FindForbiddenSurface(fixture);
        Assert.False(finding is null, $"Dega '{branch}' NUK u kap nga traversal-i.");
        Assert.Contains(expectedLocator, finding!, StringComparison.Ordinal);
    }

    /// <summary>Markerët e rinj §7-R3 raportohen me EMRIN e vet (jo rastësisht).</summary>
    [Fact]
    public void IRoleValidator_dhe_IUserRoleStore_raportohen_me_emrin_e_vet()
    {
        var validatorFinding = RoleSurfaceScanner.FindForbiddenSurface(typeof(FixtureRoleValidatorExposure));
        Assert.NotNull(validatorFinding);
        Assert.Contains("Microsoft.AspNetCore.Identity.IRoleValidator", validatorFinding, StringComparison.Ordinal);

        var userRoleStoreFinding = RoleSurfaceScanner.FindForbiddenSurface(typeof(FixtureUserRoleStoreExposure));
        Assert.NotNull(userRoleStoreFinding);
        Assert.Contains("Microsoft.AspNetCore.Identity.IUserRoleStore", userRoleStoreFinding, StringComparison.Ordinal);
    }

    [Fact]
    public void Traversal_nuk_jep_false_positive_mbi_tip_te_paster()
    {
        Assert.Null(RoleSurfaceScanner.FindForbiddenSurface(typeof(FixtureCleanType)));
    }

    // ── §6-R4: UNION — skanimi metadata assembly-wide i referencave
    //    EKZEKUTUESE (edhe brenda trupave të metodave) ──

    /// <summary>
    /// §6-R4: ZERO referenca role në METADATA-t e plota të çdo assembly
    /// production (TypeRef/TypeSpec/MemberRef/MethodSpec/StandaloneSig/
    /// CustomAttribute të dekoduara strukturisht) — UNION me traversal-in e
    /// deklaruar të reflection-it, JO zëvendësim i tij.
    /// </summary>
    [Fact]
    public void Zero_role_referenca_ne_metadata_te_asnje_assembly_production()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        Assert.NotEmpty(catalog);
        foreach (var entry in catalog)
        {
            var findings = RoleMetadataScanner.Scan(Path.Combine(
                repoRoot, entry.CanonicalTargetPath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(findings.Count == 0,
                $"{entry.CanonicalPath}: {string.Join(" · ", findings.Select(f => $"{f.Table}[{f.Token}]:{f.Detail}"))}");
        }
    }

    /// <summary>
    /// §6-R4: fixtures BODY-ONLY — referenca role që jetojnë VETËM në trupin
    /// e metodës (sipërfaqja e deklaruar është krejt benigne) kapen nga
    /// tabela përkatëse e metadata-ve, me lokator determinist tabelë+token.
    /// </summary>
    [Theory]
    [InlineData("TypeSpecification", "RoleManager`1")]
    [InlineData("MethodSpecification", "IdentityRole`1")]
    [InlineData("StandaloneSignature", "RoleManager`1")]
    [InlineData("MemberReference", "get_Logger")]
    [InlineData("TypeReference", "IdentityRole`1")]
    [InlineData("CustomAttribute", "IdentityRole`1")]
    public void Metadata_skan_kap_referencat_body_only_sipas_tabeles(string table, string detailFragment)
    {
        var findings = RoleMetadataScanner.Scan(
            Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll"));
        Assert.Contains(findings, finding =>
            finding.Table == table
            && finding.Detail.Contains(detailFragment, StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.Table.StartsWith("unreadable", StringComparison.Ordinal));
    }

    /// <summary>§6-R4: file i palexueshëm si PE/metadata = gjetje fail-closed, kurrë silent.</summary>
    [Fact]
    public void Metadata_skan_deshton_fail_closed_mbi_file_te_palexueshem()
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"idata_jo_pe_{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllText(temporary, "kjo nuk eshte assembly");
            var findings = RoleMetadataScanner.Scan(temporary);
            Assert.Contains(findings, finding => finding.Table == "unreadable-assembly");
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>
    /// §6-R4: matching-u i markerëve është arity-aware EKZAKT — emra që
    /// thjesht fillojnë me markerin NUK kapen (jo loose StartsWith).
    /// </summary>
    [Fact]
    public void Matching_i_markereve_eshte_arity_aware_jo_loose()
    {
        Assert.True(RoleMetadataScanner.IsRoleMarker("Microsoft.AspNetCore.Identity.IdentityRole"));
        Assert.True(RoleMetadataScanner.IsRoleMarker("Microsoft.AspNetCore.Identity.IdentityRole`1"));
        Assert.True(RoleMetadataScanner.IsRoleMarker("Microsoft.AspNetCore.Identity.RoleManager`1"));
        Assert.False(RoleMetadataScanner.IsRoleMarker("Microsoft.AspNetCore.Identity.IdentityRoleX"));
        Assert.False(RoleMetadataScanner.IsRoleMarker("Microsoft.AspNetCore.Identity.RoleManagerFactory"));
        Assert.False(RoleMetadataScanner.IsRoleMarker("Microsoft.AspNetCore.Identity.IdentityRole`X"));
    }

    /// <summary>
    /// Fixtures BODY-ONLY (§6-R4): sipërfaqja e deklaruar (parametra/return/
    /// atribute) është krejt benigne — referenca role ekziston VETËM në IL
    /// body → e padukshme për traversal-in e deklaruar, e kapshme VETËM nga
    /// tabelat e metadata-ve.
    /// </summary>
    internal static class RoleBodyFixtures
    {
        internal static object BodyTypeOf()
            => typeof(RoleManager<IdentityRole<Guid>>);

        internal static IdentityBuilder BodyAddRoles(IdentityBuilder builder)
            => builder.AddRoles<IdentityRole<Guid>>();

        internal static void BodyLocalOnly()
        {
            RoleManager<IdentityRole<Guid>>? local = null;
            _ = local;
        }

        internal static object? BodyMemberRefOnly()
        {
            RoleManager<IdentityRole<Guid>>? manager = null;
            return manager?.Logger;
        }
    }

    // ── Fixtures (VETËM në test assembly — jashtë platform assemblies) ──

    internal sealed class FixtureCleanType
    {
        public Guid Id { get; set; }
        public List<string> Values { get; } = [];

        // Inverse §7-R3: user-store abstraksionet e lejuara të të njëjtit
        // namespace NUK raportohen — nuk ndalohet krejt namespace-i.
        public IUserClaimStore<IdentityUser<Guid>>? BenignUserStore => null;
    }

    internal class FixtureBaseExposure : IdentityRole<Guid>;

    internal interface IFixtureInterfaceExposure : IRoleStore<IdentityRole<Guid>>;

    internal sealed class FixtureFieldExposure
    {
#pragma warning disable CS0169 // fixture — fusha ekziston vetëm për t'u skanuar
        private RoleManager<IdentityRole<Guid>>? manager;
#pragma warning restore CS0169
    }

    internal sealed class FixturePropertyExposure
    {
        // Expression-bodied — ZERO backing field; dega e property-t e vetmja rrugë.
        public RoleStore<IdentityRole<Guid>, Microsoft.EntityFrameworkCore.DbContext, Guid>? Store => null;
    }

    internal sealed class FixtureConstructorExposure
    {
        public FixtureConstructorExposure(RoleManager<IdentityRole<Guid>>? manager) => _ = manager;
    }

    internal sealed class FixtureMethodParameterExposure
    {
        public void Consume(IdentityUserRole<Guid> link) => _ = link;
    }

    internal sealed class FixtureMethodReturnExposure
    {
        public IdentityRoleClaim<Guid>? Produce() => null;
    }

    internal sealed class FixtureEventExposure
    {
        // Add/remove eksplicite — ZERO backing field; dega e event-it e vetmja rrugë.
        public event Action<IdentityRole<Guid>>? Changed
        {
            add { }
            remove { }
        }
    }

    internal sealed class FixtureGenericArgumentExposure
    {
        public List<IdentityRole<Guid>> Items => [];
    }

    internal sealed class FixtureGenericConstraintExposure<TRole>
        where TRole : IdentityRole<Guid>;

    [AttributeUsage(AttributeTargets.All)]
    internal sealed class RoleMarkerAttribute(Type marker) : Attribute
    {
        public Type Marker { get; } = marker;
    }

    [RoleMarker(typeof(IdentityRole<Guid>))]
    internal sealed class FixtureAttributeExposure;

    internal sealed class FixtureNestedExposure
    {
        internal sealed class Inner : IdentityRole<Guid>;
    }

    // ── Fixtures shtesë (correction R2 — silent-pass-et e mbyllura) ──

    internal sealed class FixtureMethodGenericConstraintExposure
    {
        public void Consume<TRole>()
            where TRole : IdentityRole<Guid>
            => _ = typeof(TRole);
    }

    internal sealed class FixtureMethodAttributeExposure
    {
        [RoleMarker(typeof(IdentityRole<Guid>))]
        public void Marked()
        {
        }
    }

    internal sealed class FixturePropertyAttributeExposure
    {
        [RoleMarker(typeof(IdentityRole<Guid>))]
        public int Value { get; set; }
    }

    internal sealed class FixtureFieldAttributeExposure
    {
        [RoleMarker(typeof(IdentityRole<Guid>))]
        public int Field = 0;
    }

    internal sealed class FixtureEventAttributeExposure
    {
        [RoleMarker(typeof(IdentityRole<Guid>))]
        public event Action? Changed
        {
            add { }
            remove { }
        }
    }

    internal sealed class FixtureParameterAttributeExposure
    {
        public void Consume([RoleMarker(typeof(IdentityRole<Guid>))] int value) => _ = value;
    }

    internal sealed class FixtureReturnAttributeExposure
    {
        [return: RoleMarker(typeof(IdentityRole<Guid>))]
        public int Produce() => 0;
    }

    internal sealed class FixtureGenericParameterAttributeExposure<[RoleMarker(typeof(IdentityRole<Guid>))] T>;

    [AttributeUsage(AttributeTargets.All)]
    internal sealed class RoleMarkerNamedAttribute : Attribute
    {
        public Type? TypeValue { get; set; }
    }

    [RoleMarkerNamed(TypeValue = typeof(IdentityRole<Guid>))]
    internal sealed class FixtureNamedAttributeArgumentExposure;

    [AttributeUsage(AttributeTargets.All)]
    internal sealed class RoleMarkerArrayAttribute(params Type[] markers) : Attribute
    {
        public Type[] Markers { get; } = markers;
    }

    [RoleMarkerArray(typeof(string), typeof(IdentityRole<Guid>))]
    internal sealed class FixtureArrayAttributeArgumentExposure;

    internal sealed class FixtureByRefExposure
    {
        public void Consume(ref IdentityRole<Guid> role) => _ = role;
    }

    internal sealed class FixtureArrayFieldExposure
    {
        public IdentityRole<Guid>[]? Roles = null;
    }

    internal sealed class FixtureDeepGenericExposure
    {
        public List<Dictionary<string, IdentityRole<Guid>[]>>? Deep => null;
    }

    // ── Fixtures §7-R3: markerët e shtuar nga derivimi i framework-ut ──

    internal sealed class FixtureRoleValidatorExposure
    {
        // IRoleValidator<object>: asnjë marker tjetër në generic args — vetëm
        // vetë IRoleValidator duhet ta prodhojë gjetjen (diskriminues).
        public IRoleValidator<object>? Validator => null;
    }

    internal sealed class FixtureUserRoleStoreExposure
    {
#pragma warning disable CS0169 // fixture — fusha ekziston vetëm për t'u skanuar
        private IUserRoleStore<IdentityUser<Guid>>? store;
#pragma warning restore CS0169
    }
}

/// <summary>
/// Skaneri rekursiv cycle-safe i sipërfaqes së tipeve për markerët e ndaluar
/// të Identity roles. Anëtarët (fields/properties/events/ctors/methods/
/// attributes/nested) zgjerohen VETËM për tipet e deklaruara në assembly-n e
/// rrënjës (tipet e framework-ut skanohen në nivel emri/generic args/base/
/// interfaces — mjafton për detektim dhe mban traversal-in të kufizuar; JO
/// "universal reflection traversal" — reflektimi dinamik mbetet kufi i hapur
/// ENG-006). Markerët mbulohen nga meta-testi i derivimit të framework-ut.
/// </summary>
internal static class RoleSurfaceScanner
{
    internal static readonly string[] ForbiddenMarkers =
        ["Microsoft.AspNetCore.Identity.IdentityRole",
         "Microsoft.AspNetCore.Identity.IdentityUserRole",
         "Microsoft.AspNetCore.Identity.IdentityRoleClaim",
         "Microsoft.AspNetCore.Identity.RoleManager",
         "Microsoft.AspNetCore.Identity.EntityFrameworkCore.RoleStore",
         "Microsoft.AspNetCore.Identity.IRoleStore",
         "Microsoft.AspNetCore.Identity.IRoleClaimStore",
         "Microsoft.AspNetCore.Identity.IQueryableRoleStore",
         "Microsoft.AspNetCore.Identity.IRoleValidator",
         "Microsoft.AspNetCore.Identity.IUserRoleStore",
         "Microsoft.AspNetCore.Identity.RoleStoreBase",
         "Microsoft.AspNetCore.Identity.RoleValidator",
         "Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext"];

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static string? FindForbiddenSurface(Type root)
        => Scan(root, root.Assembly, [], "rrënja");

    private static string? Scan(Type? type, Assembly rootAssembly, HashSet<Type> visited, string path)
    {
        if (type is null || !visited.Add(type))
        {
            return null;
        }

        try
        {
            return ScanCore(type, rootAssembly, visited, path);
        }
        catch (Exception exception)
        {
            // FAIL-CLOSED (§4, correction R2): surface i palexueshëm nuk
            // kalon kurrë silent — raportohet si gjetje.
            return $"{path} → SURFACE I PALEXUESHËM ({exception.GetType().Name})";
        }
    }

    private static string? ScanCore(Type type, Assembly rootAssembly, HashSet<Type> visited, string path)
    {
        var comparableName =
            (type.IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName) ?? type.Name;
        if (ForbiddenMarkers.Any(marker => comparableName.StartsWith(marker, StringComparison.Ordinal)))
        {
            return $"{path} → {comparableName}";
        }

        // Element types: array / by-ref / pointer — GJITHMONË.
        if (type.HasElementType
            && Scan(type.GetElementType(), rootAssembly, visited, $"{path}/element") is { } elementFinding)
        {
            return elementFinding;
        }

        // Generic arguments — GJITHMONË (edhe për tipe framework-u).
        foreach (var argument in type.GenericTypeArguments)
        {
            if (Scan(argument, rootAssembly, visited, $"{path}/generic-arg") is { } genericFinding)
            {
                return genericFinding;
            }
        }

        // Generic parameter-at e TIPIT: constraints + attributes mbi ta.
        if (type.IsGenericTypeDefinition)
        {
            foreach (var parameter in type.GetGenericArguments())
            {
                foreach (var constraint in parameter.GetGenericParameterConstraints())
                {
                    if (Scan(constraint, rootAssembly, visited, $"{path}/generic-constraint") is { } constraintFinding)
                    {
                        return constraintFinding;
                    }
                }

                if (ScanAttributes(parameter.GetCustomAttributesData(), rootAssembly, visited,
                        $"{path}/generic-param-attr:{parameter.Name}") is { } parameterAttributeFinding)
                {
                    return parameterAttributeFinding;
                }
            }
        }

        // Base chain + interfaces — GJITHMONË (name-level për framework).
        if (Scan(type.BaseType, rootAssembly, visited, $"{path}/base") is { } baseFinding)
        {
            return baseFinding;
        }

        foreach (var contract in type.GetInterfaces())
        {
            if (Scan(contract, rootAssembly, visited, $"{path}/interface") is { } interfaceFinding)
            {
                return interfaceFinding;
            }
        }

        // Attributes mbi vetë tipin — GJITHMONË.
        if (ScanAttributes(type.GetCustomAttributesData(), rootAssembly, visited, $"{path}/attribute") is { } typeAttributeFinding)
        {
            return typeAttributeFinding;
        }

        // Anëtarët — VETËM për tipet e assembly-t të rrënjës (kufizim i
        // qëllimshëm i dokumentuar: tipet e jashtme skanohen në nivel
        // emri/base/interfaces/generics — mjafton për detektim pa e bërë
        // traversal-in të pakufizuar; JO "universal reflection traversal").
        if (type.Assembly != rootAssembly)
        {
            return null;
        }

        foreach (var field in type.GetFields(AllDeclared))
        {
            if (Scan(field.FieldType, rootAssembly, visited, $"{path}/field:{field.Name}") is { } fieldFinding)
            {
                return fieldFinding;
            }

            if (ScanAttributes(field.GetCustomAttributesData(), rootAssembly, visited, $"{path}/field-attr:{field.Name}") is { } fieldAttributeFinding)
            {
                return fieldAttributeFinding;
            }
        }

        foreach (var property in type.GetProperties(AllDeclared))
        {
            if (Scan(property.PropertyType, rootAssembly, visited, $"{path}/property:{property.Name}") is { } propertyFinding)
            {
                return propertyFinding;
            }

            if (ScanAttributes(property.GetCustomAttributesData(), rootAssembly, visited, $"{path}/property-attr:{property.Name}") is { } propertyAttributeFinding)
            {
                return propertyAttributeFinding;
            }
        }

        // Events PARA metodave: ndryshe accessor-ët add_/remove_ (parametri i
        // handler-it) do ta hijezonin degën e event-it në degën e metodave —
        // lokatori i synuar duhet të jetë "/event:".
        foreach (var eventInfo in type.GetEvents(AllDeclared))
        {
            if (Scan(eventInfo.EventHandlerType, rootAssembly, visited, $"{path}/event:{eventInfo.Name}") is { } eventFinding)
            {
                return eventFinding;
            }

            if (ScanAttributes(eventInfo.GetCustomAttributesData(), rootAssembly, visited, $"{path}/event-attr:{eventInfo.Name}") is { } eventAttributeFinding)
            {
                return eventAttributeFinding;
            }
        }

        foreach (var constructor in type.GetConstructors(AllDeclared))
        {
            if (ScanAttributes(constructor.GetCustomAttributesData(), rootAssembly, visited, $"{path}/ctor-attr") is { } constructorAttributeFinding)
            {
                return constructorAttributeFinding;
            }

            foreach (var parameter in constructor.GetParameters())
            {
                if (Scan(parameter.ParameterType, rootAssembly, visited, $"{path}/ctor-param:{parameter.Name}") is { } ctorFinding)
                {
                    return ctorFinding;
                }

                if (ScanAttributes(parameter.GetCustomAttributesData(), rootAssembly, visited, $"{path}/ctor-param-attr:{parameter.Name}") is { } ctorParameterAttributeFinding)
                {
                    return ctorParameterAttributeFinding;
                }
            }
        }

        foreach (var method in type.GetMethods(AllDeclared))
        {
            if (Scan(method.ReturnType, rootAssembly, visited, $"{path}/return:{method.Name}") is { } returnFinding)
            {
                return returnFinding;
            }

            if (ScanAttributes(method.GetCustomAttributesData(), rootAssembly, visited, $"{path}/method-attr:{method.Name}") is { } methodAttributeFinding)
            {
                return methodAttributeFinding;
            }

            if (ScanAttributes(method.ReturnParameter.GetCustomAttributesData(), rootAssembly, visited, $"{path}/return-attr:{method.Name}") is { } returnAttributeFinding)
            {
                return returnAttributeFinding;
            }

            // Generic parameter-at e METODËS: constraints + attributes.
            if (method.IsGenericMethodDefinition)
            {
                foreach (var parameter in method.GetGenericArguments())
                {
                    foreach (var constraint in parameter.GetGenericParameterConstraints())
                    {
                        if (Scan(constraint, rootAssembly, visited, $"{path}/method-generic-constraint:{method.Name}") is { } methodConstraintFinding)
                        {
                            return methodConstraintFinding;
                        }
                    }

                    if (ScanAttributes(parameter.GetCustomAttributesData(), rootAssembly, visited,
                            $"{path}/method-generic-param-attr:{method.Name}") is { } methodGenericAttributeFinding)
                    {
                        return methodGenericAttributeFinding;
                    }
                }
            }

            foreach (var parameter in method.GetParameters())
            {
                if (Scan(parameter.ParameterType, rootAssembly, visited, $"{path}/param:{method.Name}.{parameter.Name}") is { } parameterFinding)
                {
                    return parameterFinding;
                }

                if (ScanAttributes(parameter.GetCustomAttributesData(), rootAssembly, visited, $"{path}/param-attr:{method.Name}.{parameter.Name}") is { } parameterAttributeFinding)
                {
                    return parameterAttributeFinding;
                }
            }
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (Scan(nested, rootAssembly, visited, $"{path}/nested:{nested.Name}") is { } nestedFinding)
            {
                return nestedFinding;
            }
        }

        return null;
    }

    /// <summary>
    /// Skanon një grup attributes: tipin e attribute-it + argumentet e
    /// konstruktorit + NAMED arguments — përfshirë vlera Type të vetme dhe
    /// arrays/collections/nested values që përmbajnë Type.
    /// </summary>
    private static string? ScanAttributes(
        IEnumerable<CustomAttributeData> attributes, Assembly rootAssembly, HashSet<Type> visited, string path)
    {
        foreach (var attribute in attributes)
        {
            if (Scan(attribute.AttributeType, rootAssembly, visited, $"{path}/type") is { } attributeTypeFinding)
            {
                return attributeTypeFinding;
            }

            foreach (var argument in attribute.ConstructorArguments)
            {
                if (ScanAttributeValue(argument, rootAssembly, visited, $"{path}/ctor-arg") is { } argumentFinding)
                {
                    return argumentFinding;
                }
            }

            foreach (var named in attribute.NamedArguments)
            {
                if (ScanAttributeValue(named.TypedValue, rootAssembly, visited, $"{path}/named-arg:{named.MemberName}") is { } namedFinding)
                {
                    return namedFinding;
                }
            }
        }

        return null;
    }

    private static string? ScanAttributeValue(
        CustomAttributeTypedArgument argument, Assembly rootAssembly, HashSet<Type> visited, string path)
    {
        if (argument.Value is Type typeValue)
        {
            return Scan(typeValue, rootAssembly, visited, path);
        }

        if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> elements)
        {
            foreach (var element in elements)
            {
                if (ScanAttributeValue(element, rootAssembly, visited, $"{path}/element") is { } elementFinding)
                {
                    return elementFinding;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Skaneri METADATA assembly-wide i referencave EKZEKUTUESE role (§6-R4) —
/// UNION me RoleSurfaceScanner-in e deklaruar, JO zëvendësim. Iteron TË
/// PLOTA (pa "kur janë relevante") tabelat: TypeReference · TypeSpecification
/// · MemberReference (prind + firmë) · MethodSpecification ·
/// StandaloneSignature (locals/firma metodash) · CustomAttribute (dekodim
/// STRUKTUROR i vlerave — jo kërkim loose UTF-8). Matching-u i markerëve =
/// EKZAKT ose arity-aware (kurrë StartsWith i lirshëm). Token/blob/firmë e
/// palexueshme = gjetje fail-closed. Lokatori = tabela + tokeni metadata
/// (determinist); dekodimi i IL bodies NUK është autoriteti i detektimit.
/// </summary>
internal static class RoleMetadataScanner
{
    internal sealed record MetadataFinding(string Assembly, string Table, string Token, string Detail);

    internal static bool IsRoleMarker(string fullName)
    {
        // Emrat e serializuar të atributeve mbartin edhe assembly qualifier /
        // argumente gjenerike — krahasohet vetëm pjesa emërore.
        var comparable = fullName;
        var cut = comparable.IndexOfAny([',', '[']);
        if (cut >= 0)
        {
            comparable = comparable[..cut];
        }

        foreach (var marker in RoleSurfaceScanner.ForbiddenMarkers)
        {
            if (comparable.Equals(marker, StringComparison.Ordinal))
            {
                return true;
            }

            if (comparable.Length > marker.Length + 1
                && comparable[marker.Length] == '`'
                && comparable.StartsWith(marker, StringComparison.Ordinal)
                && comparable[(marker.Length + 1)..].All(char.IsAsciiDigit))
            {
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<MetadataFinding> Scan(string assemblyPath)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        var findings = new List<MetadataFinding>();

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var portableExecutable = new PEReader(stream);
            var reader = portableExecutable.GetMetadataReader();
            var collector = new CollectingTypeProvider(reader);

            foreach (var handle in reader.TypeReferences)
            {
                var metadataToken = Token(handle);
                try
                {
                    Report(findings, assemblyName, "TypeReference", metadataToken,
                        FullNameOfReference(reader, handle));
                }
                catch (Exception exception)
                {
                    findings.Add(new(assemblyName, "unreadable-type-reference", metadataToken, exception.GetType().Name));
                }
            }

            for (var row = 1; row <= reader.GetTableRowCount(TableIndex.TypeSpec); row++)
            {
                var handle = MetadataTokens.TypeSpecificationHandle(row);
                var metadataToken = Token(handle);
                try
                {
                    foreach (var name in collector.CollectTypeSpecification(handle))
                    {
                        Report(findings, assemblyName, "TypeSpecification", metadataToken, name);
                    }
                }
                catch (Exception exception)
                {
                    findings.Add(new(assemblyName, "unreadable-type-specification", metadataToken, exception.GetType().Name));
                }
            }

            foreach (var handle in reader.MemberReferences)
            {
                var metadataToken = Token(handle);
                try
                {
                    var member = reader.GetMemberReference(handle);
                    var memberName = reader.GetString(member.Name);
                    var collected = new List<string>();
                    collected.AddRange(collector.CollectParent(member.Parent));
                    collected.AddRange(collector.CollectMemberSignature(member));
                    foreach (var name in collected)
                    {
                        if (IsRoleMarker(name))
                        {
                            findings.Add(new(assemblyName, "MemberReference", metadataToken, $"{memberName} → {name}"));
                        }
                    }
                }
                catch (Exception exception)
                {
                    findings.Add(new(assemblyName, "unreadable-member-reference", metadataToken, exception.GetType().Name));
                }
            }

            for (var row = 1; row <= reader.GetTableRowCount(TableIndex.MethodSpec); row++)
            {
                var handle = MetadataTokens.MethodSpecificationHandle(row);
                var metadataToken = Token(handle);
                try
                {
                    var specification = reader.GetMethodSpecification(handle);
                    foreach (var name in collector.CollectMethodSpecification(specification))
                    {
                        Report(findings, assemblyName, "MethodSpecification", metadataToken, name);
                    }
                }
                catch (Exception exception)
                {
                    findings.Add(new(assemblyName, "unreadable-method-specification", metadataToken, exception.GetType().Name));
                }
            }

            for (var row = 1; row <= reader.GetTableRowCount(TableIndex.StandAloneSig); row++)
            {
                var handle = MetadataTokens.StandaloneSignatureHandle(row);
                var metadataToken = $"0x{MetadataTokens.GetToken(handle):X8}";
                try
                {
                    foreach (var name in collector.CollectStandaloneSignature(handle))
                    {
                        Report(findings, assemblyName, "StandaloneSignature", metadataToken, name);
                    }
                }
                catch (Exception exception)
                {
                    findings.Add(new(assemblyName, "unreadable-standalone-signature", metadataToken, exception.GetType().Name));
                }
            }

            foreach (var handle in reader.CustomAttributes)
            {
                var metadataToken = Token(handle);
                try
                {
                    foreach (var name in collector.CollectCustomAttribute(handle))
                    {
                        Report(findings, assemblyName, "CustomAttribute", metadataToken, name);
                    }
                }
                catch (Exception exception)
                {
                    findings.Add(new(assemblyName, "unreadable-custom-attribute", metadataToken, exception.GetType().Name));
                }
            }
        }
        catch (Exception exception)
        {
            findings.Add(new(assemblyName, "unreadable-assembly", "-", exception.GetType().Name));
        }

        return findings;
    }

    private static void Report(List<MetadataFinding> findings, string assembly, string table, string metadataToken, string name)
    {
        if (IsRoleMarker(name))
        {
            findings.Add(new(assembly, table, metadataToken, name));
        }
    }

    private static string Token(EntityHandle handle) => $"0x{MetadataTokens.GetToken(handle):X8}";

    private static string FullName(MetadataReader reader, StringHandle namespaceHandle, StringHandle nameHandle)
    {
        var typeNamespace = namespaceHandle.IsNil ? string.Empty : reader.GetString(namespaceHandle);
        var typeName = reader.GetString(nameHandle);
        return typeNamespace.Length == 0 ? typeName : $"{typeNamespace}.{typeName}";
    }

    /// <summary>Emri i plotë i një TypeRef me kompozim të tipeve NESTED (prind+fëmijë).</summary>
    private static string FullNameOfReference(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var name = FullName(reader, reference.Namespace, reference.Name);
        return reference.ResolutionScope.Kind == HandleKind.TypeReference
            ? $"{FullNameOfReference(reader, (TypeReferenceHandle)reference.ResolutionScope)}+{name}"
            : name;
    }

    private static string FullNameOfDefinition(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = FullName(reader, definition.Namespace, definition.Name);
        var declaring = definition.GetDeclaringType();
        return declaring.IsNil ? name : $"{FullNameOfDefinition(reader, declaring)}+{name}";
    }

    /// <summary>
    /// Provider MBLEDHËS: çdo TypeRef/TypeDef i hasur gjatë dekodimit të një
    /// firme regjistrohet me emër të plotë — matching-u bëhet mbi setin e
    /// mbledhur, jo mbi string-un e kompozuar (zero parsing i lirshëm).
    /// </summary>
    private sealed class CollectingTypeProvider(MetadataReader reader)
        : ISignatureTypeProvider<string, object?>, ICustomAttributeTypeProvider<string>
    {
        private List<string>? currentCollection;

        private List<string> Run(Action decode)
        {
            var collected = new List<string>();
            currentCollection = collected;
            try
            {
                decode();
            }
            finally
            {
                currentCollection = null;
            }

            return collected;
        }

        internal List<string> CollectTypeSpecification(TypeSpecificationHandle handle)
            => Run(() =>
            {
                var blobReader = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
                new SignatureDecoder<string, object?>(this, reader, genericContext: null).DecodeType(ref blobReader);
            });

        internal List<string> CollectParent(EntityHandle parent)
            => Run(() =>
            {
                switch (parent.Kind)
                {
                    case HandleKind.TypeReference:
                        Record(FullNameOfReference(reader, (TypeReferenceHandle)parent));
                        break;
                    case HandleKind.TypeDefinition:
                        Record(FullNameOfDefinition(reader, (TypeDefinitionHandle)parent));
                        break;
                    case HandleKind.TypeSpecification:
                        var blobReader = reader.GetBlobReader(
                            reader.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                        new SignatureDecoder<string, object?>(this, reader, genericContext: null).DecodeType(ref blobReader);
                        break;
                }
            });

        internal List<string> CollectMemberSignature(MemberReference member)
            => Run(() =>
            {
                switch (member.GetKind())
                {
                    case MemberReferenceKind.Method:
                        member.DecodeMethodSignature(this, genericContext: null);
                        break;
                    case MemberReferenceKind.Field:
                        member.DecodeFieldSignature(this, genericContext: null);
                        break;
                }
            });

        internal List<string> CollectMethodSpecification(MethodSpecification specification)
            => Run(() => specification.DecodeSignature(this, genericContext: null));

        internal List<string> CollectStandaloneSignature(StandaloneSignatureHandle handle)
            => Run(() =>
            {
                var signature = reader.GetStandaloneSignature(handle);
                if (signature.GetKind() == StandaloneSignatureKind.LocalVariables)
                {
                    signature.DecodeLocalSignature(this, genericContext: null);
                }
                else
                {
                    signature.DecodeMethodSignature(this, genericContext: null);
                }
            });

        internal List<string> CollectCustomAttribute(CustomAttributeHandle handle)
            => Run(() =>
            {
                var attribute = reader.GetCustomAttribute(handle);
                var value = attribute.DecodeValue(this);
                foreach (var argument in value.FixedArguments)
                {
                    RecordTypedArgument(argument.Value);
                }

                foreach (var named in value.NamedArguments)
                {
                    RecordTypedArgument(named.Value);
                }
            });

        private void RecordTypedArgument(object? value)
        {
            switch (value)
            {
                case string serialized:
                    // Vlerat Type serializohen si emra të plotë të kualifikuar.
                    Record(serialized);
                    break;
                case ImmutableArray<CustomAttributeTypedArgument<string>> elements:
                    foreach (var element in elements)
                    {
                        RecordTypedArgument(element.Value);
                    }

                    break;
            }
        }

        private void Record(string fullName)
        {
            currentCollection?.Add(fullName);
        }

        // ── ISignatureTypeProvider ──

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var fullName = FullNameOfDefinition(metadataReader, handle);
            Record(fullName);
            return fullName;
        }

        public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var fullName = FullNameOfReference(metadataReader, handle);
            Record(fullName);
            return fullName;
        }

        public string GetTypeFromSpecification(MetadataReader metadataReader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var blobReader = metadataReader.GetBlobReader(metadataReader.GetTypeSpecification(handle).Signature);
            return new SignatureDecoder<string, object?>(this, metadataReader, genericContext).DecodeType(ref blobReader);
        }

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

        // ── ICustomAttributeTypeProvider ──

        public string GetSystemType() => "System.Type";

        public bool IsSystemType(string type) => type == "System.Type";

        public string GetTypeFromSerializedName(string name)
        {
            Record(name);
            return name;
        }

        /// <summary>
        /// Zgjidhja REALE e tipit bazë të enum-eve në attribute blobs —
        /// enum i pazgjidhshëm = përjashtim (gjetje fail-closed), kurrë
        /// hamendësim i heshtur i gjerësisë.
        /// </summary>
        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
        {
            var resolved = ResolveRuntimeType(type)
                ?? throw new BadImageFormatException($"Enum i pazgjidhshëm në attribute blob: {type}");
            return Type.GetTypeCode(resolved.GetEnumUnderlyingType()) switch
            {
                TypeCode.SByte => PrimitiveTypeCode.SByte,
                TypeCode.Byte => PrimitiveTypeCode.Byte,
                TypeCode.Int16 => PrimitiveTypeCode.Int16,
                TypeCode.UInt16 => PrimitiveTypeCode.UInt16,
                TypeCode.Int32 => PrimitiveTypeCode.Int32,
                TypeCode.UInt32 => PrimitiveTypeCode.UInt32,
                TypeCode.Int64 => PrimitiveTypeCode.Int64,
                TypeCode.UInt64 => PrimitiveTypeCode.UInt64,
                _ => throw new BadImageFormatException($"Tip bazë enum-i i papritur: {type}"),
            };
        }

        private static Type? ResolveRuntimeType(string serializedName)
        {
            if (Type.GetType(serializedName, throwOnError: false) is { } direct)
            {
                return direct;
            }

            // Emrat pa assembly qualifier: kërko në assemblies e ngarkuara
            // (framework-u i pinuar + referencat e testeve janë të pranishme).
            var bareName = serializedName.Split(',')[0].Trim();
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(bareName, throwOnError: false))
                .FirstOrDefault(candidate => candidate is not null);
        }
    }
}
