using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Reflection;
using Xunit;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// ZERO Identity roles — imponim STRUKTUROR i PLOTË (correction pass PR #28,
/// §9): traversal REKURSIV, cycle-safe, mbi TË GJITHA tipet e platformës
/// (publike DHE jo-publike) — base types · interfaces · fields · properties ·
/// constructors · method params/returns · events · generic arguments ·
/// generic parameter constraints · attributes (tipi + argumentet Type) ·
/// nested types. Fixtures diskriminuese në KËTË test assembly provojnë që
/// skaneri kap SECILIN lloj ekspozimi — heqja e një dege të traversal-it e
/// bën meta-testin përkatës RED.
/// </summary>
public sealed class IdentityRoleSurfaceTests
{
    private static readonly Assembly[] AllPlatformAssemblies =
        [Assembly.Load("Idata.Platform.Modules.PlatformStatus.Domain"),
         Assembly.Load("Idata.Platform.Modules.PlatformStatus.Application"),
         Assembly.Load("Idata.Platform.Modules.PlatformStatus.Infrastructure"),
         Assembly.Load("Idata.Platform.Modules.PlatformStatus.Presentation"),
         Assembly.Load("Idata.Platform.Modules.AccessControl.Domain"),
         Assembly.Load("Idata.Platform.Modules.AccessControl.Application"),
         Assembly.Load("Idata.Platform.Modules.AccessControl.Infrastructure"),
         Assembly.Load("Idata.Platform.Modules.AccessControl.Presentation"),
         Assembly.Load("Idata.Platform.Modules.Identity.Infrastructure"),
         Assembly.Load("Idata.Platform.Persistence"),
         Assembly.Load("Idata.Platform.DbMigrator"),
         Assembly.Load("Idata.Platform.Api")];

    [Fact]
    public void Zero_Identity_roles_ne_asnje_tip_te_platformes_traversal_rekursiv()
    {
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
    /// Fixtures diskriminuese — SECILA degë kryesore e traversal-it provohet
    /// veçmas; nëse dega hiqet nga skaneri, rasti përkatës bëhet RED.
    /// </summary>
    [Theory]
    [InlineData(typeof(FixtureBaseExposure), "base type")]
    [InlineData(typeof(IFixtureInterfaceExposure), "interface")]
    [InlineData(typeof(FixtureFieldExposure), "field")]
    [InlineData(typeof(FixturePropertyExposure), "property")]
    [InlineData(typeof(FixtureConstructorExposure), "constructor parameter")]
    [InlineData(typeof(FixtureMethodParameterExposure), "method parameter")]
    [InlineData(typeof(FixtureMethodReturnExposure), "method return")]
    [InlineData(typeof(FixtureEventExposure), "event")]
    [InlineData(typeof(FixtureGenericArgumentExposure), "generic argument")]
    [InlineData(typeof(FixtureGenericConstraintExposure<>), "generic constraint")]
    [InlineData(typeof(FixtureAttributeExposure), "attribute Type argument")]
    [InlineData(typeof(FixtureNestedExposure), "nested type")]
    [InlineData(typeof(FixtureMethodGenericConstraintExposure), "METHOD generic constraint")]
    [InlineData(typeof(FixtureMethodAttributeExposure), "attribute mbi method")]
    [InlineData(typeof(FixturePropertyAttributeExposure), "attribute mbi property")]
    [InlineData(typeof(FixtureFieldAttributeExposure), "attribute mbi field")]
    [InlineData(typeof(FixtureEventAttributeExposure), "attribute mbi event")]
    [InlineData(typeof(FixtureParameterAttributeExposure), "attribute mbi parameter")]
    [InlineData(typeof(FixtureReturnAttributeExposure), "attribute mbi return value")]
    [InlineData(typeof(FixtureGenericParameterAttributeExposure<>), "attribute mbi generic parameter")]
    [InlineData(typeof(FixtureNamedAttributeArgumentExposure), "named attribute argument")]
    [InlineData(typeof(FixtureArrayAttributeArgumentExposure), "array attribute argument")]
    [InlineData(typeof(FixtureByRefExposure), "by-ref parameter")]
    [InlineData(typeof(FixtureArrayFieldExposure), "array element type")]
    [InlineData(typeof(FixtureDeepGenericExposure), "deep generic nesting")]
    public void Traversal_i_kap_te_gjitha_llojet_e_ekspozimit_fixtures(Type fixture, string branch)
    {
        var finding = RoleSurfaceScanner.FindForbiddenSurface(fixture);
        Assert.False(finding is null, $"Dega '{branch}' NUK u kap nga traversal-i.");
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
        public RoleStore<IdentityRole<Guid>, Microsoft.EntityFrameworkCore.DbContext, Guid>? Store { get; set; }
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
        public event Action<IdentityRole<Guid>>? Changed;

        public void Raise() => Changed?.Invoke(null!);
    }

    internal sealed class FixtureGenericArgumentExposure
    {
        public List<IdentityRole<Guid>> Items { get; } = [];
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
        public event Action? Changed;

        public void Raise() => Changed?.Invoke();
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
        public List<Dictionary<string, IdentityRole<Guid>[]>>? Deep { get; set; }
    }
}

/// <summary>
/// Skaneri rekursiv cycle-safe i sipërfaqes së tipeve për markerët e ndaluar
/// të Identity roles. Anëtarët (fields/properties/ctors/methods/events/
/// attributes/nested) zgjerohen VETËM për tipet e deklaruara në assembly-n e
/// rrënjës (tipet e framework-ut skanohen në nivel emri/generic args/base/
/// interfaces — mjafton për detektim dhe mban traversal-in të kufizuar).
/// </summary>
internal static class RoleSurfaceScanner
{
    private static readonly string[] ForbiddenMarkers =
        ["Microsoft.AspNetCore.Identity.IdentityRole",
         "Microsoft.AspNetCore.Identity.IdentityUserRole",
         "Microsoft.AspNetCore.Identity.IdentityRoleClaim",
         "Microsoft.AspNetCore.Identity.RoleManager",
         "Microsoft.AspNetCore.Identity.EntityFrameworkCore.RoleStore",
         "Microsoft.AspNetCore.Identity.IRoleStore",
         "Microsoft.AspNetCore.Identity.IRoleClaimStore",
         "Microsoft.AspNetCore.Identity.IQueryableRoleStore",
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
