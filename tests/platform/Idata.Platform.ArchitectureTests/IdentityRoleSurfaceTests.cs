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

    [AttributeUsage(AttributeTargets.Class)]
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

        var comparableName =
            (type.IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName) ?? type.Name;
        if (ForbiddenMarkers.Any(marker => comparableName.StartsWith(marker, StringComparison.Ordinal)))
        {
            return $"{path} → {comparableName}";
        }

        // Generic arguments — GJITHMONË (edhe për tipe framework-u).
        foreach (var argument in type.GenericTypeArguments)
        {
            if (Scan(argument, rootAssembly, visited, $"{path}/generic-arg") is { } genericFinding)
            {
                return genericFinding;
            }
        }

        // Generic parameter constraints.
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

        // Anëtarët — VETËM për tipet e assembly-t të rrënjës (kufizim i qëllimshëm).
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
        }

        foreach (var property in type.GetProperties(AllDeclared))
        {
            if (Scan(property.PropertyType, rootAssembly, visited, $"{path}/property:{property.Name}") is { } propertyFinding)
            {
                return propertyFinding;
            }
        }

        foreach (var constructor in type.GetConstructors(AllDeclared))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                if (Scan(parameter.ParameterType, rootAssembly, visited, $"{path}/ctor-param:{parameter.Name}") is { } ctorFinding)
                {
                    return ctorFinding;
                }
            }
        }

        foreach (var method in type.GetMethods(AllDeclared))
        {
            if (Scan(method.ReturnType, rootAssembly, visited, $"{path}/return:{method.Name}") is { } returnFinding)
            {
                return returnFinding;
            }

            foreach (var parameter in method.GetParameters())
            {
                if (Scan(parameter.ParameterType, rootAssembly, visited, $"{path}/param:{method.Name}.{parameter.Name}") is { } parameterFinding)
                {
                    return parameterFinding;
                }
            }
        }

        foreach (var eventInfo in type.GetEvents(AllDeclared))
        {
            if (Scan(eventInfo.EventHandlerType, rootAssembly, visited, $"{path}/event:{eventInfo.Name}") is { } eventFinding)
            {
                return eventFinding;
            }
        }

        foreach (var attribute in type.GetCustomAttributesData())
        {
            if (Scan(attribute.AttributeType, rootAssembly, visited, $"{path}/attribute") is { } attributeFinding)
            {
                return attributeFinding;
            }

            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Value is Type argumentType
                    && Scan(argumentType, rootAssembly, visited, $"{path}/attribute-arg") is { } argumentFinding)
                {
                    return argumentFinding;
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
}
