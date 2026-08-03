namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Taksonomia ENG-006 (Pre-B2 Increment 2, §5) e AKSESIT DINAMIK
/// reflection/runtime — DENY-BY-DEFAULT me TRI rezultate:
///
///  1. **DANGEROUS** — anëtar ekzekutues që zgjidh ose thërret në runtime →
///     gjetje.
///  2. **SAFE EXACT** — operacion i EMËRTUAR, i review-uar dhe i provuar nga
///     call-site-t aktuale (p.sh. `typeof(...)` → `Type.GetTypeFromHandle`) →
///     lejohet.
///  3. **UNKNOWN mbi një familje të monitoruar** → gjetje fail-closed.
///
/// Autoriteti është **familja e tipit prind** plus emri EKZAKT i anëtarit —
/// asnjë `StartsWith`/`Contains`. `System.Object.GetType()` NUK ndalohet
/// globalisht (do të prodhonte false-positive masive): zinxhiri kapet te hapi i
/// dytë real (`Type.GetProperty`, `Type.GetMethod`, …), i cili është DANGEROUS.
///
/// Safe-lista është EKSPLICITE dhe e derivuar nga call-site-t aktuale; nuk ka
/// auto-allow për namespace ose familje.
/// </summary>
internal static class DynamicRuntimeAccessTaxonomy
{
    /// <summary>
    /// Familjet e MONITORUARA: çdo anëtar ekzekutues mbi këto tipe klasifikohet.
    /// Anëtar i panjohur mbi to = gjetje fail-closed.
    /// </summary>
    internal static readonly IReadOnlySet<string> MonitoredTypeFullNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // ── Reflection core ──
            "System.Type",
            "System.Reflection.Assembly",
            "System.Reflection.Module",
            "System.Reflection.MemberInfo",
            "System.Reflection.MethodBase",
            "System.Reflection.MethodInfo",
            "System.Reflection.ConstructorInfo",
            "System.Reflection.PropertyInfo",
            "System.Reflection.FieldInfo",
            "System.Activator",
            "System.Delegate",
            "System.Runtime.Loader.AssemblyLoadContext",

            // ── DLR (`dynamic`) ──
            "System.Runtime.CompilerServices.CallSite",
            "System.Runtime.CompilerServices.CallSite`1",
            "Microsoft.CSharp.RuntimeBinder.Binder",

            // ── Service location me Type-in e runtime-it ──
            "System.IServiceProvider",
            "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions",
        };

    /// <summary>
    /// DANGEROUS — zgjidhje/thirrje dinamike. Vlerësohen VETËM mbi familjet e
    /// monitoruara, prandaj homonimet në tipe të tjera nuk preken.
    /// </summary>
    internal static readonly IReadOnlySet<string> DangerousMemberNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // Type — zgjidhje dhe zbulim anëtarësh
            "GetType", "GetMethod", "GetMethods", "GetProperty", "GetProperties",
            "GetField", "GetFields", "GetConstructor", "GetConstructors",
            "GetMember", "GetMembers", "GetEvent", "GetEvents",
            "GetNestedType", "GetNestedTypes", "GetInterface",
            "InvokeMember", "MakeGenericType", "MakeArrayType", "MakePointerType",

            // Assembly / Module / ALC — ngarkim dhe zgjidhje
            "Load", "LoadFrom", "LoadFile", "LoadWithPartialName", "LoadFrom",
            "UnsafeLoadFrom", "ReflectionOnlyLoad", "LoadFromAssemblyPath",
            "LoadFromAssemblyName", "LoadFromStream", "CreateInstance",

            // Invocation
            "Invoke", "DynamicInvoke", "CreateDelegate", "MakeGenericMethod",
            "GetValue", "SetValue", "SetValueDirect", "GetValueDirect",

            // DLR
            "Create", "InvokeMember", "GetMember", "SetMember", "BinaryOperation",

            // Service location me Type-in e runtime-it (diskriminohet me sinjaturë)
            "GetService", "GetRequiredService", "GetServices",
        };

    /// <summary>
    /// SAFE EXACT — çifte (tip i monitoruar, anëtar) të lejuara SHPREHIMISHT.
    /// Këto janë operacione të tipizuara/diagnostike pa asnjë zgjidhje dinamike:
    /// `typeof(...)` kompilohet në `GetTypeFromHandle`, ndërsa emri/assembly-ja
    /// janë vetëm lexim metadata. Lista rritet VETËM me review.
    /// </summary>
    internal static readonly IReadOnlySet<(string ParentType, string Member)> SafeExactMembers =
        new HashSet<(string, string)>
        {
            ("System.Type", "GetTypeFromHandle"),
            ("System.Type", "get_Assembly"),
            ("System.Type", "get_FullName"),
            ("System.Type", "get_Name"),
            ("System.Type", "get_Namespace"),
            ("System.Type", "get_IsAbstract"),
            ("System.Type", "get_IsInterface"),
            ("System.Type", "op_Equality"),
            ("System.Type", "op_Inequality"),
            ("System.Reflection.Assembly", "get_FullName"),
            ("System.Reflection.Assembly", "GetName"),
            ("System.Reflection.MemberInfo", "get_Name"),
            ("System.Reflection.MemberInfo", "get_DeclaringType"),

            // Materializim i një TOKENI TË KOMPILUAR në handle — ekuivalenti i
            // `typeof(...)` për metoda/fusha. Kompajleri i emeton për lambda-t
            // dhe expression tree-t (p.sh. `HasKey(x => x.Id)`); nuk ka asnjë
            // zgjidhje dinamike: identiteti është i ngulitur në compile-time.
            ("System.Reflection.MethodBase", "GetMethodFromHandle"),
            ("System.Reflection.FieldInfo", "GetFieldFromHandle"),
        };

    /// <summary>
    /// Anëtarët e service-location që janë DANGEROUS vetëm në overload-in që
    /// pranon `System.Type` në runtime; varianti gjenerik `&lt;T&gt;` është i
    /// tipizuar dhe mbetet i lejuar.
    /// </summary>
    internal static readonly IReadOnlySet<string> RuntimeTypeOverloadMembers =
        new HashSet<string>(StringComparer.Ordinal) { "GetService", "GetRequiredService", "GetServices" };

    /// <summary>
    /// §5 — sipërfaqja që owner-at NUK duhet ta eksportojnë publikisht: dorëzimi
    /// i këtyre tipeve është escape hatch i drejtpërdrejtë drejt reflection.
    /// </summary>
    internal static readonly IReadOnlySet<string> OwnerForbiddenSurfaceTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Type",
            "System.Reflection.Assembly",
            "System.Reflection.Module",
            "System.Reflection.MemberInfo",
            "System.Reflection.MethodBase",
            "System.Reflection.MethodInfo",
            "System.Reflection.ConstructorInfo",
            "System.Reflection.PropertyInfo",
            "System.Reflection.FieldInfo",
            "System.Delegate",
            "System.IServiceProvider",
            "System.Linq.Expressions.Expression",
            "System.Runtime.Loader.AssemblyLoadContext",
        };

    internal static bool IsMonitoredType(string fullTypeName)
        => MonitoredTypeFullNames.Contains(fullTypeName);

    internal static bool IsSafeExact(string parentTypeFullName, string memberName)
        => SafeExactMembers.Contains((parentTypeFullName, memberName));

    internal static bool IsDangerousMember(string memberName)
        => DangerousMemberNames.Contains(memberName);
}
