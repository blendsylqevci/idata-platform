using System.CodeDom.Compiler;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
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

    // ═════ C1/A — format e kodit TONË që kompajleri i shënon vetë ═════
    // Të gjitha duhet të mbeten BLOCKING (unattributed ose kind i thjeshtë) —
    // asnjëra nuk fiton trust "generated" nga [CompilerGenerated] i kompajlerit.

    /// <summary>Non-capturing lambda → klasa `+&lt;&gt;c` e kompajlerit.</summary>
    internal static Func<object?> NonCapturingLambdaFixture()
        => static () => Type.GetType("System.String");

    /// <summary>Capturing lambda → `+&lt;&gt;c__DisplayClass…` e kompajlerit.</summary>
    internal static Func<object?> CapturingLambdaFixture(string capturedName)
        => () => Type.GetType(capturedName);

    /// <summary>Local function → metodë `&lt;M&gt;g__…|…` mbi vetë tipin.</summary>
    internal static object? LocalFunctionFixture(string typeName)
    {
        return Resolve();

        object? Resolve() => Type.GetType(typeName);
    }

    /// <summary>Lambda brenda LINQ (capturing) → `+&lt;&gt;c__DisplayClass…`.</summary>
    internal static object?[] LinqLambdaFixture(string prefix, IEnumerable<string> suffixes)
        => [.. suffixes.Select(suffix => (object?)Type.GetType(prefix + suffix))];

    // ═════════════ GREEN 15 — operacione SAFE EXACT të tipizuara ═════════════

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

/// <summary>
/// C1/A RED — klasë MANUALE me `[CompilerGenerated]`: atributi i vetëm nuk jep
/// KURRË trust; gjetja duhet të mbetet kind i thjeshtë blocking, jo `generated-*`.
/// </summary>
[CompilerGenerated]
internal static class ManualCompilerGeneratedFixture
{
    internal static object? Probe() => Type.GetType("System.String");
}

/// <summary>
/// C1/A RED — klasë manuale me `[GeneratedCode]` dhe tool TË PANJOHUR: merr
/// klasifikim `generated-*` me tool-in e dekoduar, POR ledger-i s'e njeh dhe
/// kind-i dangerous është gjithmonë blocking.
/// </summary>
[GeneratedCode("UnknownTool", "9.9")]
internal static class ManualUnknownToolFixture
{
    internal static object? Probe() => Type.GetType("System.String");
}

/// <summary>
/// C1/A RED — klasë manuale me tool/version REAL të generatorit të pin-uar:
/// as ky nuk vetë-autorizon një tip arbitrar — dangerous/service-location janë
/// gjithmonë blocking dhe unknown-member-i s'ka hyrje ledger-i.
/// </summary>
[GeneratedCode("Mediator.SourceGenerator", "3.0.0.0")]
internal static class ManualRealToolFixture
{
    internal static object? ProbeDangerous() => Type.GetType("System.String");

    internal static object? ProbeServiceLocation(IServiceProvider provider, Type serviceType)
        => provider.GetService(serviceType);

    internal static Type? ProbeUnknown(Type type) => type.GetElementType();
}

/// <summary>
/// C1/A RED — nested type I PASHËNUAR nën parent me `[GeneratedCode]`: nuk
/// trashëgon ASNJË trust nga parent-i (barazi ekzakte, zero ecje '+').
/// </summary>
[GeneratedCode("Mediator.SourceGenerator", "3.0.0.0")]
internal static class GeneratedParentFixture
{
    internal static class UnmarkedChild
    {
        internal static object? Probe() => Type.GetType("System.String");
    }
}

/// <summary>
/// C2 (Blocker B) — PE SINTETIKE e emetuar me `MetadataBuilder`/Ecma335, që
/// arrin EKZAKTËSISHT degët fail-closed të metadata-ve të palexueshme, të cilat
/// s'prodhohen dot nga C# valid: (a) blob i keqformuar i `[GeneratedCode]`,
/// (b) sinjaturë fushe e keqformuar, (c) sinjaturë metode e keqformuar.
/// Varianti VALID (i njëjti skelet me blobs të vlefshëm) provon zero
/// false-positive. Zero prekje e ndonjë output-i real.
/// </summary>
internal static class SyntheticMetadataPe
{
    internal const string HostTypeFullName = "Synthetic.MalformedHost";

    /// <summary>Emeton PE-në (në OS-temp, ose në directory-n e dhënë) dhe kthen path-in.</summary>
    internal static string Write(bool malformed, string? directory = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("SynMalformed.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("SynMalformed"), new Version(1, 0, 0, 0),
            default, default, 0, AssemblyHashAlgorithm.None);

        var corlib = metadata.AddAssemblyReference(metadata.GetOrAddString("System.Runtime"),
            new Version(10, 0, 0, 0), default, default, 0, default);
        var objectRef = metadata.AddTypeReference(
            corlib, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        var attributeRef = metadata.AddTypeReference(
            corlib, metadata.GetOrAddString("System.CodeDom.Compiler"),
            metadata.GetOrAddString("GeneratedCodeAttribute"));

        // .ctor(string, string) i atributit — sinjaturë E VLEFSHME metode.
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(isInstanceMethod: true)
            .Parameters(2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().String();
                    parameters.AddParameter().Type().String();
                });
        var attributeConstructor = metadata.AddMemberReference(
            attributeRef, metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        // Fusha: sinjaturë e vlefshme (string) OSE e cunguar (vetëm prolog-u FIELD).
        var validFieldSignature = new BlobBuilder();
        new BlobEncoder(validFieldSignature).Field().Type().String();
        var fieldDefinition = metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static,
            metadata.GetOrAddString("ProbeField"),
            malformed ? metadata.GetOrAddBlob(new byte[] { 0x06 })
                      : metadata.GetOrAddBlob(validFieldSignature));

        // Metoda: sinjaturë e vlefshme `static void()` OSE bosh e pavlefshme.
        var validMethodSignature = new BlobBuilder();
        new BlobEncoder(validMethodSignature).MethodSignature()
            .Parameters(0, returnType => returnType.Void(), parameters => { });
        var methodDefinition = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.Abstract,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ProbeMethod"),
            malformed ? metadata.GetOrAddBlob(new byte[] { 0x00 })
                      : metadata.GetOrAddBlob(validMethodSignature),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        var hostType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Synthetic"), metadata.GetOrAddString("MalformedHost"),
            objectRef, fieldDefinition, methodDefinition);

        // [GeneratedCode(...)] mbi tipin: blob i VLEFSHËM (prolog + 2 SerString +
        // zero named) OSE një bajt i vetëm i pavlefshëm.
        var validAttributeBlob = new BlobBuilder();
        validAttributeBlob.WriteUInt16(0x0001);
        validAttributeBlob.WriteSerializedString("SynTool");
        validAttributeBlob.WriteSerializedString("1.0");
        validAttributeBlob.WriteUInt16(0x0000);
        metadata.AddCustomAttribute(hostType, attributeConstructor,
            malformed ? metadata.GetOrAddBlob(new byte[] { 0xFF })
                      : metadata.GetOrAddBlob(validAttributeBlob));

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            ilStream: new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var peBlob = new BlobBuilder();
        peBuilder.Serialize(peBlob);

        var path = Path.Combine(
            directory ?? Path.GetTempPath(),
            $"eng006-syn-{(malformed ? "malformed" : "valid")}-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, peBlob.ToArray());
        return path;
    }
}

/// <summary>
/// C1/B RED — FUSHA publike (statike/instance, readonly/të mutueshme, edhe
/// nested në generic/array) që dorëzojnë tipe nga seti autoritativ
/// <see cref="DynamicRuntimeAccessTaxonomy.OwnerForbiddenSurfaceTypes"/>.
/// Fushat Safe* janë inverse GREEN dhe s'duhet të raportohen kurrë.
/// </summary>
public sealed class OwnerReflectionFieldEscapeFixtures
{
    public static readonly Type LeakedType = typeof(object);

    public IServiceProvider? LeakedProvider;

    public readonly IReadOnlyList<Delegate>? LeakedDelegates;

    public static FieldInfo[]? LeakedFields;

    public int SafeCount;

    public static readonly string SafeLabel = "ok";

    public Guid SafeId;
}
