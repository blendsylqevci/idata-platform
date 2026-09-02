using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Skaneri ENG-006 (Pre-B2 Increment 2, §5) — kufiri DENY-BY-DEFAULT i aksesit
/// dinamik reflection/runtime. **I NDARË plotësisht nga
/// <see cref="IdentityStoreIlScanner"/>**: ai mbron universin e tipeve të
/// store-it, ky mbron rrugën e ZGJIDHJES DINAMIKE që skanerët statikë të tipeve
/// nuk e shohin (root cause i regjistruar i ENG-006).
///
/// Autoriteti: metadata + IL i binarëve REALË, me atribuim deri te metoda
/// burimore (state machines async zgjidhen nga `AsyncStateMachineAttribute`).
/// Çdo gjë e palexueshme ose e pa-atribuueshme = gjetje fail-closed.
///
/// Kufij të DEKLARUAR: mbulohen platform production assemblies të
/// inventarizuara dhe call-site-t ekzekutuese + DLR-patterns e provuara;
/// brendësia e paketave jashtë platform assemblies NUK pretendohet e skanuar;
/// një literal string pa call-site NUK është gjetje. Zero pretendim
/// "universal", "bypass-free" ose "production-ready".
/// </summary>
internal static class DynamicRuntimeAccessScanner
{
    internal sealed record Finding(
        string Assembly, string Kind, string DeclaringType, string Member, string Detail, string Reason,
        string GeneratedTool = "", string GeneratedToolVersion = "");

    internal static bool IsNeverAllowable(Finding finding)
        => finding.Kind.StartsWith("unreadable-", StringComparison.Ordinal)
           || finding.Kind.StartsWith("undecodable-", StringComparison.Ordinal)
           || finding.Kind.StartsWith("unattributed-", StringComparison.Ordinal);

    /// <summary>Taksonomi e injektueshme — meta-testet provojnë se çdo degë është MBAJTËSE.</summary>
    internal sealed record AccessTaxonomy(
        IReadOnlySet<string> MonitoredTypes,
        IReadOnlySet<string> DangerousMembers,
        IReadOnlySet<(string ParentType, string Member)> SafeExact)
    {
        internal static readonly AccessTaxonomy Default = new(
            DynamicRuntimeAccessTaxonomy.MonitoredTypeFullNames,
            DynamicRuntimeAccessTaxonomy.DangerousMemberNames,
            DynamicRuntimeAccessTaxonomy.SafeExactMembers);
    }

    // ───────────────────────────── Skanimi ─────────────────────────────

    /// <summary>
    /// C4 — RRUGA E VETME për artefaktet PRODUCTION: skanim mbi snapshot-in
    /// deep-immutable të bajteve të verifikuara. Zero path.
    /// </summary>
    internal static IReadOnlyList<Finding> Scan(
        PlatformProjectCatalog.VerifiedAssemblyImage image, AccessTaxonomy? taxonomy = null)
        => ScanCore(image.AssemblyName, image.OpenImage, taxonomy);

    /// <summary>
    /// C5 (§3) — VETËM fixtures sintetike: kapacitet OPAQUE i tipizuar, i
    /// prodhuar nga `PlatformProjectCatalog.OpenFixtureImage`, e cila refuzon
    /// fail-closed çdo artefakt production. Zero path i papërpunuar.
    /// </summary>
    internal static IReadOnlyList<Finding> ScanFixture(
        PlatformProjectCatalog.FixtureAssemblyImage fixture, AccessTaxonomy? taxonomy = null)
        => ScanCore(fixture.AssemblyName, fixture.OpenImage, taxonomy);

    private static IReadOnlyList<Finding> ScanCore(
        string assemblyName, Func<Stream> openImage, AccessTaxonomy? taxonomy)
    {
        taxonomy ??= AccessTaxonomy.Default;
        var findings = new List<Finding>();

        Stream stream;
        PEReader portableExecutable;
        MetadataReader reader;
        try
        {
            stream = openImage();
            portableExecutable = new PEReader(stream);
            reader = portableExecutable.GetMetadataReader();
        }
        catch (Exception exception)
        {
            return [new Finding(
                assemblyName, "unreadable-assembly", "-", "-", exception.GetType().Name,
                "Assembly i palexueshëm — gjendja nuk provohet dot.")];
        }

        using (stream)
        using (portableExecutable)
        {
            var stateMachines = BuildStateMachineMap(reader, assemblyName, findings);
            var generatedTypes = BuildGeneratedTypeMap(reader, assemblyName, findings);
            var classified = new Dictionary<int, (string Kind, string Detail, string Reason)>();
            foreach (var handle in reader.MemberReferences)
            {
                try
                {
                    var classification = Classify(reader, handle, taxonomy);
                    if (classification is not null)
                    {
                        classified[MetadataTokens.GetToken(handle)] = classification.Value;
                    }
                }
                catch (Exception exception)
                {
                    findings.Add(new Finding(
                        assemblyName, "unreadable-member-reference", "-", "-", exception.GetType().Name,
                        "Referencë anëtari e padekodueshme — fail-closed."));
                }
            }

            ScanMethodBodies(
                reader, portableExecutable, assemblyName, stateMachines, generatedTypes, classified, findings);
        }

        return findings;
    }

    private static (string Kind, string Detail, string Reason)? Classify(
        MetadataReader reader, MemberReferenceHandle handle, AccessTaxonomy taxonomy)
    {
        var memberReference = reader.GetMemberReference(handle);
        var memberName = reader.GetString(memberReference.Name);
        var parents = ParentTypeNames(reader, memberReference.Parent);
        var monitored = parents.FirstOrDefault(taxonomy.MonitoredTypes.Contains);
        if (monitored is null)
        {
            return null;
        }

        if (taxonomy.SafeExact.Contains((monitored, memberName)))
        {
            return null; // SAFE EXACT — operacion i emërtuar dhe i review-uar.
        }

        // Service location: DANGEROUS vetëm në overload-in me `System.Type`.
        if (DynamicRuntimeAccessTaxonomy.RuntimeTypeOverloadMembers.Contains(memberName))
        {
            return AcceptsRuntimeType(reader, memberReference)
                ? ("dynamic-service-location", $"{monitored}.{memberName}(System.Type)",
                    "Service locator me Type-in e runtime-it — zgjidhje dinamike, jo e tipizuar.")
                : null;
        }

        if (taxonomy.DangerousMembers.Contains(memberName))
        {
            return ("dynamic-dangerous", $"{monitored}.{memberName}",
                "Zgjidhje ose thirrje dinamike në runtime.");
        }

        // §5 — anëtar i PANJOHUR mbi familje të monitoruar = fail-closed.
        return ("dynamic-unknown-member", $"{monitored}.{memberName}",
            "Anëtar i paklasifikuar mbi një familje të monitoruar — kërkon klasifikim eksplicit.");
    }

    /// <summary>A pranon kjo referencë anëtari një `System.Type` si parametër?</summary>
    private static bool AcceptsRuntimeType(MetadataReader reader, MemberReference memberReference)
    {
        try
        {
            var collected = new HashSet<string>(StringComparer.Ordinal);
            var signature = memberReference.DecodeMethodSignature(
                new CollectingTypeProvider(collected), genericContext: null);
            return signature.ParameterTypes
                .SelectMany(UntypedDataAccessScanner.ExtractTypeComponents)
                .Contains("System.Type", StringComparer.Ordinal);
        }
        catch (Exception)
        {
            return true; // Sinjaturë e padekodueshme → fail-closed (trajtohet si e rrezikshme).
        }
    }

    private static IReadOnlyList<string> ParentTypeNames(MetadataReader reader, EntityHandle parent)
        => parent.Kind switch
        {
            HandleKind.TypeReference =>
                [UntypedDataAccessScanner.FullNameOfTypeReference(reader, (TypeReferenceHandle)parent)],
            HandleKind.TypeDefinition =>
                [UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, (TypeDefinitionHandle)parent)],
            HandleKind.TypeSpecification => DecodeSpecification(reader, (TypeSpecificationHandle)parent),
            HandleKind.MethodDefinition =>
                [UntypedDataAccessScanner.FullNameOfTypeDefinition(
                    reader, reader.GetMethodDefinition((MethodDefinitionHandle)parent).GetDeclaringType())],
            _ => [],
        };

    /// <summary>
    /// Prindi i një `TypeSpecification` është tipi DEKLARUES i jashtëm, JO
    /// argumentet e tij gjenerike. Pa këtë dallim, `EqualityComparer&lt;Type&gt;`
    /// (që kompajleri e emeton për `record.GetHashCode`) do të lexohej gabimisht
    /// si "anëtar mbi System.Type" — false-positive i dëshmuar empirikisht.
    /// </summary>
    private static string[] DecodeSpecification(MetadataReader reader, TypeSpecificationHandle handle)
    {
        var collected = new HashSet<string>(StringComparer.Ordinal);
        var blobReader = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
        var decoded = new SignatureDecoder<string, object?>(
            new CollectingTypeProvider(collected), reader, genericContext: null).DecodeType(ref blobReader);
        var generic = decoded.IndexOf('<', StringComparison.Ordinal);
        return [generic < 0 ? decoded : decoded[..generic]];
    }

    // ─────────────────── Atribuimi (IL + state machines) ───────────────────

    private static void ScanMethodBodies(
        MetadataReader reader, PEReader portableExecutable, string assemblyName,
        Dictionary<string, (string Type, string Member)> stateMachines,
        IReadOnlyDictionary<string, (string Tool, string Version)> generatedTypes,
        Dictionary<int, (string Kind, string Detail, string Reason)> classified,
        List<Finding> findings)
    {
        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var typeName = UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, method.GetDeclaringType());
            var memberName = reader.GetString(method.Name);

            byte[] il;
            try
            {
                il = portableExecutable.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()
                     ?? throw new InvalidOperationException("IL bosh");
            }
            catch (Exception exception)
            {
                findings.Add(new Finding(
                    assemblyName, "unreadable-method-body", typeName, memberName, exception.GetType().Name,
                    "Trup metode i palexueshëm — fail-closed."));
                continue;
            }

            var instructions = IlOperandTable.Walk(il, out var decodeError);
            foreach (var instruction in instructions)
            {
                if (instruction.MetadataToken == 0)
                {
                    continue;
                }

                var handle = MetadataTokens.EntityHandle(instruction.MetadataToken);
                var memberToken = instruction.MetadataToken;
                if (handle.Kind == HandleKind.MethodSpecification)
                {
                    var specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    if (specification.Method.Kind != HandleKind.MemberReference)
                    {
                        continue;
                    }

                    memberToken = MetadataTokens.GetToken(specification.Method);
                }

                if (classified.TryGetValue(memberToken, out var classification))
                {
                    findings.Add(Attribute(
                        assemblyName, typeName, memberName, classification.Kind, classification.Detail,
                        classification.Reason, stateMachines, generatedTypes));
                }
            }

            if (decodeError is not null)
            {
                findings.Add(new Finding(
                    assemblyName, "undecodable-il", typeName, memberName, decodeError,
                    "IL i padekodueshëm — fail-closed."));
            }
        }
    }

    private static Finding Attribute(
        string assemblyName, string typeName, string memberName,
        string kind, string detail, string reason,
        Dictionary<string, (string Type, string Member)> stateMachines,
        IReadOnlyDictionary<string, (string Tool, string Version)> generatedTypes)
    {
        if (stateMachines.TryGetValue(typeName, out var source))
        {
            typeName = source.Type;
            memberName = source.Member;
        }

        // Kod me `[GeneratedCode(tool, version)]` TË DEKODUAR nga metadata:
        // klasifikohet `generated-*` me tool/version të bartur në gjetje, POR
        // (correction C1) KJO NUK ËSHTË PËRJASHTIM — çdo gjetje e tillë duhet të
        // përputhet EKZAKT me ledger-in e provenance-ës në
        // <see cref="DynamicRuntimeAccessManifest"/>, përndryshe është blocking.
        // Rregullat strukturore: `[CompilerGenerated]` i vetëm nuk jep KURRË
        // trust; tipi duhet të mbajë VETË atributin — nested type nuk trashëgon
        // asgjë nga parent-i (barazi EKZAKTE e emrit të plotë, zero ecje '+').
        if (generatedTypes.TryGetValue(typeName, out var provenance))
        {
            return new Finding(
                assemblyName, $"generated-{kind}", typeName, memberName, detail,
                "Kod me [GeneratedCode] në metadata — kërkon përputhje EKZAKTE me provenance ledger-in.",
                provenance.Tool, provenance.Version);
        }

        if (typeName.Contains('<', StringComparison.Ordinal) || memberName.Contains('<', StringComparison.Ordinal))
        {
            return new Finding(
                assemblyName, "unattributed-compiler-generated", typeName, memberName, $"{kind}:{detail}",
                "Atribuimi te metoda burimore nuk provohet dot — fail-closed.");
        }

        return new Finding(assemblyName, kind, typeName, memberName, detail, reason);
    }

    /// <summary>
    /// Harta e tipeve me `[GeneratedCode]`, me TOOL dhe VERSION të dekoduar
    /// REALISHT nga blob-i i atributit në metadata — pa asnjë listë
    /// namespace-esh. `[CompilerGenerated]` NUK hyn këtu: i vetëm nuk jep trust
    /// (correction C1 — mbyllja e self-exemption). Blob i palexueshëm =
    /// gjetje fail-closed, kurrë trust i heshtur.
    /// </summary>
    private static Dictionary<string, (string Tool, string Version)> BuildGeneratedTypeMap(
        MetadataReader reader, string assemblyName, List<Finding> findings)
    {
        var generated = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var attributeHandle in type.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(attributeHandle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference)
                {
                    continue;
                }

                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (member.Parent.Kind != HandleKind.TypeReference)
                {
                    continue;
                }

                var name = UntypedDataAccessScanner.FullNameOfTypeReference(
                    reader, (TypeReferenceHandle)member.Parent);
                if (name != "System.CodeDom.Compiler.GeneratedCodeAttribute")
                {
                    continue;
                }

                var typeName = UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, typeHandle);
                try
                {
                    var blob = reader.GetBlobReader(attribute.Value);
                    if (blob.ReadUInt16() != 0x0001)
                    {
                        throw new InvalidOperationException("prolog i pavlefshëm");
                    }

                    var tool = blob.ReadSerializedString();
                    var version = blob.ReadSerializedString();
                    if (string.IsNullOrEmpty(tool) || string.IsNullOrEmpty(version))
                    {
                        throw new InvalidOperationException("tool/version bosh");
                    }

                    generated[typeName] = (tool, version);
                }
                catch (Exception exception)
                {
                    findings.Add(new Finding(
                        assemblyName, "unreadable-generated-code-attribute", typeName, "-",
                        exception.GetType().Name,
                        "Blob i padekodueshëm i [GeneratedCode] — fail-closed, zero trust."));
                }

                break;
            }
        }

        return generated;
    }

    private static Dictionary<string, (string Type, string Member)> BuildStateMachineMap(
        MetadataReader reader, string assemblyName, List<Finding> findings)
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            foreach (var attributeHandle in method.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(attributeHandle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference)
                {
                    continue;
                }

                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (member.Parent.Kind != HandleKind.TypeReference)
                {
                    continue;
                }

                var attributeType = UntypedDataAccessScanner.FullNameOfTypeReference(
                    reader, (TypeReferenceHandle)member.Parent);
                if (attributeType is not ("System.Runtime.CompilerServices.AsyncStateMachineAttribute"
                    or "System.Runtime.CompilerServices.IteratorStateMachineAttribute"
                    or "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute"))
                {
                    continue;
                }

                try
                {
                    var value = attribute.DecodeValue(SerializedTypeNameProvider.Instance);
                    if (value.FixedArguments.Length > 0 && value.FixedArguments[0].Value is string serialized
                        && serialized.Length > 0)
                    {
                        var comma = serialized.IndexOf(',', StringComparison.Ordinal);
                        map[(comma < 0 ? serialized : serialized[..comma]).Trim()] = (
                            UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, method.GetDeclaringType()),
                            reader.GetString(method.Name));
                    }
                }
                catch (Exception exception)
                {
                    findings.Add(new Finding(
                        assemblyName, "unreadable-state-machine-attribute",
                        UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, method.GetDeclaringType()),
                        reader.GetString(method.Name), exception.GetType().Name,
                        "Atribut state-machine i padekodueshëm — fail-closed."));
                }
            }
        }

        return map;
    }

    // ─────────────────── §5 — Owner public-surface guard ───────────────────

    /// <summary>
    /// §5 — guard-i i sipërfaqes publike të owner-ave: asnjë anëtar publik/
    /// `protected` (metodë, property accessor OSE FUSHË — correction C1) nuk
    /// lejohet të pranojë a dorëzojë tipe nga seti autoritativ I VETËM
    /// <see cref="DynamicRuntimeAccessTaxonomy.OwnerForbiddenSurfaceTypes"/>.
    /// Ky docstring NUK mban listë tipesh — autoriteti është vetëm seti në kod.
    ///
    /// Kufi i deklaruar: mbulohen anëtarët e DEKLARUAR në atë assembly;
    /// sinjaturë e padekodueshme = gjetje fail-closed.
    /// </summary>
    /// <summary>C4 — owner surface mbi snapshot-in e verifikuar (production).</summary>
    internal static IReadOnlyList<Finding> ScanOwnerPublicSurface(PlatformProjectCatalog.VerifiedAssemblyImage image)
        => ScanOwnerPublicSurfaceCore(image.AssemblyName, image.OpenImage);

    /// <summary>
    /// C5 (§3) — VETËM fixtures sintetike: kapacitet OPAQUE i tipizuar, i
    /// prodhuar nga `PlatformProjectCatalog.OpenFixtureImage`, e cila refuzon
    /// fail-closed çdo artefakt production. Zero path i papërpunuar.
    /// </summary>
    internal static IReadOnlyList<Finding> ScanOwnerPublicSurfaceFixture(
        PlatformProjectCatalog.FixtureAssemblyImage fixture)
        => ScanOwnerPublicSurfaceCore(fixture.AssemblyName, fixture.OpenImage);

    private static IReadOnlyList<Finding> ScanOwnerPublicSurfaceCore(string assemblyName, Func<Stream> openImage)
    {
        var findings = new List<Finding>();

        Stream stream;
        PEReader portableExecutable;
        MetadataReader reader;
        try
        {
            stream = openImage();
            portableExecutable = new PEReader(stream);
            reader = portableExecutable.GetMetadataReader();
        }
        catch (Exception exception)
        {
            return [new Finding(
                assemblyName, "unreadable-assembly", "-", "-", exception.GetType().Name,
                "Assembly i palexueshëm — fail-closed.")];
        }

        using (stream)
        using (portableExecutable)
        {
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                if ((type.Attributes & TypeAttributes.VisibilityMask) is not
                    (TypeAttributes.Public or TypeAttributes.NestedPublic))
                {
                    continue;
                }

                var typeName = UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, typeHandle);
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if ((method.Attributes & MethodAttributes.MemberAccessMask) is not
                        (MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem))
                    {
                        continue;
                    }

                    var collected = new HashSet<string>(StringComparer.Ordinal);
                    try
                    {
                        var signature = method.DecodeSignature(
                            new CollectingTypeProvider(collected), genericContext: null);
                        collected.UnionWith(
                            signature.ParameterTypes.SelectMany(UntypedDataAccessScanner.ExtractTypeComponents));
                        collected.UnionWith(
                            UntypedDataAccessScanner.ExtractTypeComponents(signature.ReturnType));
                    }
                    catch (Exception exception)
                    {
                        findings.Add(new Finding(
                            assemblyName, "unreadable-method-signature", typeName,
                            reader.GetString(method.Name), exception.GetType().Name,
                            "Sinjaturë e padekodueshme — fail-closed."));
                        continue;
                    }

                    foreach (var forbidden in collected
                        .Where(DynamicRuntimeAccessTaxonomy.OwnerForbiddenSurfaceTypes.Contains)
                        .Order(StringComparer.Ordinal))
                    {
                        findings.Add(new Finding(
                            assemblyName, "owner-public-reflection-surface", typeName,
                            reader.GetString(method.Name), forbidden,
                            "Sipërfaqe publike që dorëzon kapacitet reflection te modulet e tjera."));
                    }
                }

                // Correction C1 (Blocker B): edhe FUSHAT publike/`protected` —
                // instance ose statike, readonly ose të mutueshme — skanohen me
                // të njëjtin set autoritativ dhe me dekodim fail-closed.
                foreach (var fieldHandle in type.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    if ((field.Attributes & FieldAttributes.FieldAccessMask) is not
                        (FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem))
                    {
                        continue;
                    }

                    var fieldTypes = new HashSet<string>(StringComparer.Ordinal);
                    try
                    {
                        var decoded = field.DecodeSignature(
                            new CollectingTypeProvider(fieldTypes), genericContext: null);
                        fieldTypes.UnionWith(UntypedDataAccessScanner.ExtractTypeComponents(decoded));
                    }
                    catch (Exception exception)
                    {
                        findings.Add(new Finding(
                            assemblyName, "unreadable-field-signature", typeName,
                            reader.GetString(field.Name), exception.GetType().Name,
                            "Sinjaturë fushe e padekodueshme — fail-closed."));
                        continue;
                    }

                    foreach (var forbidden in fieldTypes
                        .Where(DynamicRuntimeAccessTaxonomy.OwnerForbiddenSurfaceTypes.Contains)
                        .Order(StringComparer.Ordinal))
                    {
                        findings.Add(new Finding(
                            assemblyName, "owner-public-reflection-surface", typeName,
                            reader.GetString(field.Name), forbidden,
                            "Fushë publike që dorëzon kapacitet reflection te modulet e tjera."));
                    }
                }
            }
        }

        return findings;
    }

    // ─────────────────── Providers ───────────────────

    private sealed class CollectingTypeProvider(HashSet<string> collected)
        : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => $"System.{typeCode}";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var name = UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, handle);
            collected.Add(name);
            return name;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var name = UntypedDataAccessScanner.FullNameOfTypeReference(reader, handle);
            collected.Add(name);
            return name;
        }

        public string GetTypeFromSpecification(
            MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var blobReader = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
            return new SignatureDecoder<string, object?>(this, reader, genericContext).DecodeType(ref blobReader);
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
    }

    private sealed class SerializedTypeNameProvider : ICustomAttributeTypeProvider<string>
    {
        internal static readonly SerializedTypeNameProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => "System.Type";

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => UntypedDataAccessScanner.FullNameOfTypeReference(reader, handle);

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => type == "System.Type";
    }
}
