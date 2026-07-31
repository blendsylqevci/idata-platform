using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Reflection;
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
    /// <summary>§3-R3: assemblies të DERIVUARA nga katalogu kanonik.</summary>
    private static readonly Assembly[] AllPlatformAssemblies =
        [.. PlatformProjectCatalog.DiscoverProduction()
            .Select(entry => Assembly.Load(entry.AssemblyName))];

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
