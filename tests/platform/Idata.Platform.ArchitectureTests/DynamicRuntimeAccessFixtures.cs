using System.Reflection;
using Idata.Platform.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Fixtures SINTETIKE për ENG-006 (§6). Asnjëra nuk ekzekutohet ndaj një
/// sistemi real: ekzistojnë vetëm që kompajleri të prodhojë IL/metadata REALE
/// mbi të cilat skaneri provohet. Zero kredencial, zero të dhënë reale.
/// </summary>
internal static class DynamicRuntimeAccessFixtures
{
    // ═════ RED 1 — PoC historik: emër i ndërtuar në runtime + Type.GetType ═════

    /// <summary>
    /// PoC-i i regjistruar i ENG-006: emri i tipit të store-it ndërtohet VËRTET
    /// në runtime nga fragmente, prandaj skanerët statikë TYPE/METADATA nuk e
    /// shohin. ENG-006 e kap te CALL-SITE-i (`Type.GetType`), jo te stringu.
    /// </summary>
    internal static object? RuntimeBuiltTypeNameFixture()
    {
        var parts = new[] { "Idata", ".", "Platform", ".", "Persistence", ".", "User", "ClaimRecord" };
        return Type.GetType(string.Concat(parts));
    }

    // ═════ RED 2 — GetType → GetProperty → GetValue mbi kontekstin ═════

    internal static object? ContextPropertyProbeFixture(PlatformDbContext context)
    {
        var type = context.GetType();
        var property = type.GetProperty("UserClaims");
        return property?.GetValue(context);
    }

    // ═════ RED 3 — Assembly.Load → GetType → Activator.CreateInstance ═════

    internal static object? AssemblyLoadCreateInstanceFixture(string assemblyName, string typeName)
    {
        var assembly = Assembly.Load(assemblyName);
        var type = assembly.GetType(typeName);
        return type is null ? null : Activator.CreateInstance(type);
    }

    // ═════ RED 4 — GetMethod → Invoke ═════

    internal static object? MethodInvokeFixture(object target, string methodName)
        => target.GetType().GetMethod(methodName)?.Invoke(target, null);

    // ═════ RED 5 — Type.InvokeMember ═════

    internal static object? InvokeMemberFixture(Type type, object target, string member)
        => type.InvokeMember(member, BindingFlags.InvokeMethod, null, target, null);

    // ═════ RED 6 — PropertyInfo.GetValue/SetValue ═════

    internal static object? PropertyGetSetFixture(PropertyInfo property, object target, object? value)
    {
        property.SetValue(target, value);
        return property.GetValue(target);
    }

    // ═════ RED 7 — FieldInfo.GetValue/SetValue ═════

    internal static object? FieldGetSetFixture(FieldInfo field, object target, object? value)
    {
        field.SetValue(target, value);
        return field.GetValue(target);
    }

    // ═════ RED 8 — MakeGenericType / MakeGenericMethod ═════

    internal static object MakeGenericTypeFixture(Type open, Type argument)
        => open.MakeGenericType(argument);

    internal static object MakeGenericMethodFixture(MethodInfo open, Type argument)
        => open.MakeGenericMethod(argument);

    // ═════ RED 9 — Delegate.DynamicInvoke / CreateDelegate ═════

    internal static object? DynamicInvokeFixture(Delegate target) => target.DynamicInvoke(null);

    internal static object CreateDelegateFixture(MethodInfo method, object target)
        => method.CreateDelegate(typeof(Action), target);

    // ═════ RED 10 — `dynamic`: emetim REAL i CallSite/Binder ═════

    internal static object? DynamicDispatchFixture(dynamic target) => target.AnyMemberName();

    // ═════ RED 11 — service location me Type-in e runtime-it ═════

    internal static object? ServiceLocatorRuntimeTypeFixture(IServiceProvider provider, Type serviceType)
        => provider.GetService(serviceType);

    internal static object ServiceLocatorRequiredRuntimeTypeFixture(IServiceProvider provider, Type serviceType)
        => provider.GetRequiredService(serviceType);

    // ═════ RED 12 — atribuim i metodës ASYNC (locator te metoda burimore) ═════

    internal static async Task<object?> AsyncReflectionFixture(object target, string memberName)
    {
        await Task.Yield();
        return target.GetType().GetProperty(memberName)?.GetValue(target);
    }

    // ═════ GREEN 15 — operacione SAFE EXACT të tipizuara ═════

    /// <summary>`typeof(...)` → `Type.GetTypeFromHandle`; emër/assembly = lexim metadata.</summary>
    internal static string SafeTypedMetadataFixture()
        => $"{typeof(PlatformDbContext).FullName}|{typeof(PlatformDbContext).Assembly.GetName().Name}";

    // ═════ GREEN 14 — homonime SINTETIKE me FullName tjetër ═════

    internal static object SyntheticReflectionHomonymsFixture()
        => new object[] { SyntheticReflection.Type.GetType("x"), SyntheticReflection.Activator.CreateInstance() };

    // ═════ GREEN 16 — API i tipizuar, i ndërmjetësuar nga owner-i ═════

    internal static IReadOnlyList<Guid> TypedMediatedApiFixture(PlatformDbContext context)
        => [.. context.Businesses.Select(business => business.Id)];
}

/// <summary>
/// GREEN 14 — homonime SINTETIKE: emri i thjeshtë përputhet me familjet e
/// monitoruara, `FullName` jo. Provojnë se autoriteti është emri I PLOTË.
/// </summary>
internal static class SyntheticReflection
{
    internal static class Type
    {
        internal static object GetType(string name) => name;
    }

    internal static class Activator
    {
        internal static object CreateInstance() => new();
    }
}

/// <summary>
/// RED 17 — escape hatch PUBLIK i owner-it: ekspozon `Type`/`MethodInfo`/
/// `IServiceProvider` te modulet e tjera. Asnjë owner production nuk e ka.
/// </summary>
public static class OwnerReflectionEscapeHatchFixtures
{
    public static Type ResolveType(string name) => typeof(object);

    public static MethodInfo? FindMethod(Type type, string name) => type.GetMethod(name);

    public static IServiceProvider Locator(IServiceProvider provider) => provider;
}

/// <summary>GREEN 16 — API publik i tipizuar, pa asnjë sipërfaqe reflection.</summary>
public static class TypedMediatedOwnerApiFixture
{
    public static string Describe(Guid businessId) => businessId.ToString("D");

    public static IReadOnlyList<Guid> Empty() => [];
}
