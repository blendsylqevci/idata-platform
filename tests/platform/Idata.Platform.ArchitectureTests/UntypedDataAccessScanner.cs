using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Skaneri ENG-007 (Pre-B2 Increment 1, §4–§6): inventarizon dhe klasifikon
/// çdo sink të NJOHUR nga taksonomia e mbështetur/versionuar
/// (<see cref="RawDataSinkTaxonomy"/>) brenda një production assembly, me
/// ATRIBUIM të saktë (tip + anëtar) që manifesti i përjashtimeve të jetë i
/// verifikueshëm. NUK pretendohet mbulim universal i çdo API-je të ardhshme të
/// ekosistemit — mbulimi është ai i provuar nga fixtures dhe meta-testet.
///
/// Dy shtresa, të dyja të detyrueshme (fail-closed):
///
///  1. **Metadata (plotësia)** — çdo `MemberReference` klasifikohet kundër
///     <see cref="RawDataSinkTaxonomy"/>. Seti i sink-eve të referuara nga
///     assembly-ja është i njohur PARA se të hapet një trup metode.
///  2. **IL (atribuimi)** — çdo `MethodDefinition` me trup dekodohet me tabelë
///     TË PLOTË operand-esh ECMA-335 (jo skanim bajtësh) dhe çdo instruksion
///     që bart token (call/callvirt/newobj/ldftn/ldvirtftn/castclass/isinst/
///     ldtoken/initobj/constrained/ldfld/stfld/…) zgjidhet e klasifikohet.
///
/// Rakordimi i dy shtresave është vetë një guard: çdo sink i pranishëm në
/// metadata por i pa-atribuuar në asnjë trup = `unattributed-sink-token`
/// (INCOMPLETE → fail-closed). Kështu një sink nuk mund të "zhduket" duke e
/// bërë trupin e padekodueshëm.
///
/// Atribuimi i state machine-ve async/iterator kalon nga
/// `AsyncStateMachineAttribute`/`IteratorStateMachineAttribute`/
/// `AsyncIteratorStateMachineAttribute` (metadata REALE, jo heuristikë emri).
/// Çdo sink në kod compiler-generated që NUK provohet dot deri te metoda
/// burimore = `unattributed-compiler-generated` (fail-closed) — kurrë
/// atribuim me hamendje.
///
/// Kufij të DEKLARUAR (jo pretendim "universal"):
///  - reflection/dynamic invocation NUK mbulohet — mbetet **ENG-006 OPEN**;
///  - `calli`/function pointers janë të pazgjidhshëm statikisht → gjetje
///    `indirect-call-site`, kurrë PASS i heshtur;
///  - autoriteti është metadata+IL i binarëve production të katalogut; sjellja
///    e serverit DB (grants/roles) NUK pretendohet — ADR-026 mbetet OPEN.
/// </summary>
internal static class UntypedDataAccessScanner
{
    /// <summary>
    /// Gjetje me ATRIBUIM të plotë. `Member` është metoda burimore (pas
    /// zgjidhjes së state machine-ve) ose `field:&lt;emri&gt;` për sinjaturat e
    /// fushave.
    /// </summary>
    internal sealed record Finding(
        string Assembly, string Kind, string DeclaringType, string Member, string Detail);

    /// <summary>
    /// Gjetjet që NUK mund të lejohen KURRË nga manifesti — ato nuk përshkruajnë
    /// një sink të review-uar, por paaftësi për ta provuar gjendjen.
    /// </summary>
    internal static bool IsNeverAllowable(Finding finding)
        => finding.Kind.StartsWith("unreadable-", StringComparison.Ordinal)
           || finding.Kind.StartsWith("undecodable-", StringComparison.Ordinal)
           || finding.Kind.StartsWith("unattributed-", StringComparison.Ordinal)
           || finding.Kind.StartsWith("indirect-", StringComparison.Ordinal);

    /// <summary>
    /// Taksonomia e injektueshme — ekziston që meta-testet të provojnë se ÇDO
    /// familje sink-esh është MBAJTËSE: heqja e saj duhet ta bëjë fixture-n
    /// përkatëse të pazbuluar (guard-i s'kalon nga rrugë të tjera aksidentale).
    /// </summary>
    internal sealed record SinkTaxonomy(
        IReadOnlySet<string> RawTypes,
        IReadOnlySet<string> RawMembers,
        IReadOnlySet<(string ParentType, string Member)> QualifiedMembers)
    {
        internal static readonly SinkTaxonomy Default = new(
            RawDataSinkTaxonomy.RawCapabilityTypeFullNames,
            RawDataSinkTaxonomy.RawExecutionMemberNames,
            RawDataSinkTaxonomy.QualifiedRawExecutionMembers);

        internal bool IsRawType(string fullTypeName) => RawTypes.Contains(fullTypeName);

        internal bool IsRawMember(string memberName) => RawMembers.Contains(memberName);

        internal bool IsQualifiedRawMember(string parentTypeFullName, string memberName)
            => QualifiedMembers.Contains((parentTypeFullName, memberName));
    }

    // ───────────────────────── Skanimi kryesor ─────────────────────────

    /// <summary>
    /// C4 — RRUGA E VETME për artefaktet PRODUCTION: skanim mbi snapshot-in
    /// deep-immutable të bajteve të verifikuara. Zero path.
    /// </summary>
    internal static IReadOnlyList<Finding> Scan(
        PlatformProjectCatalog.VerifiedAssemblyImage image, SinkTaxonomy? taxonomy = null)
        => ScanCore(image.AssemblyName, image.OpenImage, taxonomy);

    /// <summary>
    /// C5 (§3) — VETËM fixtures sintetike: kapacitet OPAQUE i tipizuar, i
    /// prodhuar nga `PlatformProjectCatalog.OpenFixtureImage`, e cila refuzon
    /// fail-closed çdo artefakt production. Zero path i papërpunuar.
    /// </summary>
    internal static IReadOnlyList<Finding> ScanFixture(
        PlatformProjectCatalog.FixtureAssemblyImage fixture, SinkTaxonomy? taxonomy = null)
        => ScanCore(fixture.AssemblyName, fixture.OpenImage, taxonomy);

    private static IReadOnlyList<Finding> ScanCore(
        string assemblyName, Func<Stream> openImage, SinkTaxonomy? taxonomy)
    {
        taxonomy ??= SinkTaxonomy.Default;
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
            return [new Finding(assemblyName, "unreadable-assembly", "-", "-", exception.GetType().Name)];
        }

        using (stream)
        using (portableExecutable)
        {
            var stateMachines = BuildStateMachineMap(reader, assemblyName, findings);
            var sinkMemberReferences = BuildSinkMemberReferenceIndex(reader, assemblyName, taxonomy, findings);
            var attributedTokens = new HashSet<int>();

            ScanDeclaredSignatures(reader, assemblyName, taxonomy, stateMachines, findings);
            ScanMethodBodies(
                reader, portableExecutable, assemblyName, taxonomy, stateMachines,
                sinkMemberReferences, attributedTokens, findings);

            // Rakordim shtresash: çdo sink i referuar në metadata duhet të jetë
            // atribuuar të paktën një herë në një trup metode të dekoduar.
            foreach (var (memberToken, classification) in sinkMemberReferences)
            {
                if (attributedTokens.Contains(memberToken))
                {
                    continue;
                }

                findings.Add(new Finding(
                    assemblyName, "unattributed-sink-token", "-", "-",
                    $"{classification.Kind}:{classification.Detail}"));
            }
        }

        return findings;
    }

    // ─────────────────── State machines (atribuimi async) ───────────────────

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
                var attributeType = CustomAttributeTypeFullName(reader, attribute);
                if (attributeType is not ("System.Runtime.CompilerServices.AsyncStateMachineAttribute"
                    or "System.Runtime.CompilerServices.IteratorStateMachineAttribute"
                    or "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute"))
                {
                    continue;
                }

                string? serialized = null;
                try
                {
                    var value = attribute.DecodeValue(SerializedTypeNameProvider.Instance);
                    serialized = value.FixedArguments.Length > 0
                        ? value.FixedArguments[0].Value as string
                        : null;
                }
                catch (Exception exception)
                {
                    findings.Add(new Finding(
                        assemblyName, "unreadable-state-machine-attribute",
                        FullNameOfTypeDefinition(reader, method.GetDeclaringType()),
                        reader.GetString(method.Name), exception.GetType().Name));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(serialized))
                {
                    findings.Add(new Finding(
                        assemblyName, "unreadable-state-machine-attribute",
                        FullNameOfTypeDefinition(reader, method.GetDeclaringType()),
                        reader.GetString(method.Name), "argument bosh"));
                    continue;
                }

                map[NormalizeSerializedTypeName(serialized)] = (
                    FullNameOfTypeDefinition(reader, method.GetDeclaringType()),
                    reader.GetString(method.Name));
            }
        }

        return map;
    }

    /// <summary>Heq sufiksin assembly-qualified; emrat e nested mbeten me '+'.</summary>
    private static string NormalizeSerializedTypeName(string serialized)
    {
        var comma = serialized.IndexOf(',', StringComparison.Ordinal);
        return (comma < 0 ? serialized : serialized[..comma]).Trim();
    }

    // ─────────────────── Indeksi i sink-eve nga metadata ───────────────────

    private static Dictionary<int, (string Kind, string Detail)> BuildSinkMemberReferenceIndex(
        MetadataReader reader, string assemblyName, SinkTaxonomy taxonomy, List<Finding> findings)
    {
        var index = new Dictionary<int, (string, string)>();
        foreach (var handle in reader.MemberReferences)
        {
            (string Kind, string Detail)? classification;
            try
            {
                classification = ClassifyMemberReference(reader, handle, taxonomy);
            }
            catch (Exception exception)
            {
                findings.Add(new Finding(
                    assemblyName, "unreadable-member-reference", "-", "-", exception.GetType().Name));
                continue;
            }

            if (classification is not null)
            {
                index[MetadataTokens.GetToken(handle)] = classification.Value;
            }
        }

        return index;
    }

    private static (string Kind, string Detail)? ClassifyMemberReference(
        MetadataReader reader, MemberReferenceHandle handle, SinkTaxonomy taxonomy)
    {
        var memberReference = reader.GetMemberReference(handle);
        var memberName = reader.GetString(memberReference.Name);
        var parents = ParentTypeNames(reader, memberReference.Parent);

        // (C) Çifti i KUALIFIKUAR vlerësohet i pari: lidh tipin prind me anëtarin
        // ekzakt, prandaj është më specifik se të dyja rregullat globale.
        var qualifiedParent = parents.FirstOrDefault(
            parent => taxonomy.IsQualifiedRawMember(parent, memberName));
        if (qualifiedParent is not null)
        {
            return ("qualified-raw-member", $"{qualifiedParent}.{memberName}");
        }

        var rawParent = parents.FirstOrDefault(taxonomy.IsRawType);
        if (rawParent is not null)
        {
            // §4.2 — anëtar I NJOHUR OSE I PANJOHUR mbi një tip raw = gjetje.
            return ("raw-type-member", $"{rawParent}.{memberName}");
        }

        if (taxonomy.IsRawMember(memberName))
        {
            return ("raw-execution-member", $"{(parents.Count > 0 ? parents[0] : "?")}.{memberName}");
        }

        return null;
    }

    private static IReadOnlyList<string> ParentTypeNames(MetadataReader reader, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                return [FullNameOfTypeReference(reader, (TypeReferenceHandle)parent)];
            case HandleKind.TypeDefinition:
                return [FullNameOfTypeDefinition(reader, (TypeDefinitionHandle)parent)];
            case HandleKind.TypeSpecification:
                {
                    var collected = new HashSet<string>(StringComparer.Ordinal);
                    DecodeTypeSpecification(reader, (TypeSpecificationHandle)parent, collected);
                    return [.. collected];
                }

            case HandleKind.MethodDefinition:
                {
                    var method = reader.GetMethodDefinition((MethodDefinitionHandle)parent);
                    return [FullNameOfTypeDefinition(reader, method.GetDeclaringType())];
                }

            case HandleKind.ModuleReference:
                return ["<module>"];
            default:
                return [];
        }
    }

    // ─────────────────── Sinjaturat e deklaruara (fusha/metoda) ───────────────────

    private static void ScanDeclaredSignatures(
        MetadataReader reader, string assemblyName, SinkTaxonomy taxonomy,
        Dictionary<string, (string Type, string Member)> stateMachines, List<Finding> findings)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var typeName = FullNameOfTypeDefinition(reader, typeHandle);

            foreach (var fieldHandle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                var collected = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    field.DecodeSignature(new CollectingTypeProvider(collected), genericContext: null);
                }
                catch (Exception exception)
                {
                    findings.Add(new Finding(
                        assemblyName, "unreadable-field-signature", typeName,
                        reader.GetString(field.Name), exception.GetType().Name));
                    continue;
                }

                EmitRawTypeSignatureFindings(
                    assemblyName, typeName, $"field:{reader.GetString(field.Name)}",
                    "raw-type-in-field", collected, taxonomy, stateMachines, findings);
            }

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var collected = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    var signature = method.DecodeSignature(new CollectingTypeProvider(collected), genericContext: null);
                    _ = signature.ParameterTypes.Length;
                }
                catch (Exception exception)
                {
                    findings.Add(new Finding(
                        assemblyName, "unreadable-method-signature", typeName,
                        reader.GetString(method.Name), exception.GetType().Name));
                    continue;
                }

                EmitRawTypeSignatureFindings(
                    assemblyName, typeName, reader.GetString(method.Name),
                    "raw-type-in-signature", collected, taxonomy, stateMachines, findings);
            }
        }
    }

    private static void EmitRawTypeSignatureFindings(
        string assemblyName, string typeName, string memberName, string kind,
        HashSet<string> collected, SinkTaxonomy taxonomy,
        Dictionary<string, (string Type, string Member)> stateMachines, List<Finding> findings)
    {
        foreach (var rawType in collected.Where(taxonomy.IsRawType).Order(StringComparer.Ordinal))
        {
            findings.Add(Attribute(assemblyName, typeName, memberName, kind, rawType, stateMachines));
        }
    }

    // ─────────────────── Trupat IL (atribuimi i saktë) ───────────────────

    private static void ScanMethodBodies(
        MetadataReader reader, PEReader portableExecutable, string assemblyName, SinkTaxonomy taxonomy,
        Dictionary<string, (string Type, string Member)> stateMachines,
        Dictionary<int, (string Kind, string Detail)> sinkMemberReferences,
        HashSet<int> attributedTokens, List<Finding> findings)
    {
        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue; // abstract/extern/interface — pa trup IL
            }

            var typeName = FullNameOfTypeDefinition(reader, method.GetDeclaringType());
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
                    assemblyName, "unreadable-method-body", typeName, memberName, exception.GetType().Name));
                continue;
            }

            var instructions = IlOperandTable.Walk(il, out var decodeError);
            foreach (var instruction in instructions)
            {
                if (instruction.IsIndirectCall)
                {
                    findings.Add(new Finding(
                        assemblyName, "indirect-call-site", typeName, memberName,
                        $"calli @IL_{instruction.Offset:X4}"));
                    continue;
                }

                if (instruction.MetadataToken == 0)
                {
                    continue;
                }

                var handle = MetadataTokens.EntityHandle(instruction.MetadataToken);
                if (handle.IsNil)
                {
                    findings.Add(new Finding(
                        assemblyName, "undecodable-il-token", typeName, memberName,
                        $"0x{instruction.MetadataToken:X8} @IL_{instruction.Offset:X4}"));
                    continue;
                }

                (string Kind, string Detail)? classification;
                try
                {
                    classification = ClassifyInstructionToken(
                        reader, handle, taxonomy, sinkMemberReferences, attributedTokens);
                }
                catch (Exception exception)
                {
                    findings.Add(new Finding(
                        assemblyName, "undecodable-il-token", typeName, memberName,
                        $"{exception.GetType().Name} @IL_{instruction.Offset:X4}"));
                    continue;
                }

                if (classification is not null)
                {
                    findings.Add(Attribute(
                        assemblyName, typeName, memberName,
                        classification.Value.Kind, classification.Value.Detail, stateMachines));
                }
            }

            if (decodeError is not null)
            {
                findings.Add(new Finding(
                    assemblyName, "undecodable-il", typeName, memberName, decodeError));
            }
        }
    }

    private static (string Kind, string Detail)? ClassifyInstructionToken(
        MetadataReader reader, EntityHandle handle, SinkTaxonomy taxonomy,
        Dictionary<int, (string Kind, string Detail)> sinkMemberReferences,
        HashSet<int> attributedTokens)
    {
        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
                {
                    var memberToken = MetadataTokens.GetToken(handle);
                    if (!sinkMemberReferences.TryGetValue(memberToken, out var classification))
                    {
                        return null;
                    }

                    attributedTokens.Add(memberToken);
                    return classification;
                }

            case HandleKind.MethodSpecification:
                {
                    var specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return ClassifyInstructionToken(
                        reader, specification.Method, taxonomy, sinkMemberReferences, attributedTokens);
                }

            case HandleKind.TypeReference:
                {
                    var name = FullNameOfTypeReference(reader, (TypeReferenceHandle)handle);
                    return taxonomy.IsRawType(name) ? ("raw-type-reference", name) : null;
                }

            case HandleKind.TypeSpecification:
                {
                    var collected = new HashSet<string>(StringComparer.Ordinal);
                    DecodeTypeSpecification(reader, (TypeSpecificationHandle)handle, collected);
                    var raw = collected.Where(taxonomy.IsRawType)
                        .Order(StringComparer.Ordinal).FirstOrDefault();
                    return raw is null ? null : ("raw-type-reference", raw);
                }

            case HandleKind.TypeDefinition:
            case HandleKind.MethodDefinition:
            case HandleKind.FieldDefinition:
            case HandleKind.UserString:
                // Në-assembly ose literal string: sink-u (nëse ka) kapet aty ku
                // ndodh vërtet — §4.3 ndalon mbështetjen te literali `identity.`.
                return null;
            default:
                return null;
        }
    }

    // ─────────────────── Atribuimi (state machine → burim) ───────────────────

    private static Finding Attribute(
        string assemblyName, string typeName, string memberName, string kind, string detail,
        Dictionary<string, (string Type, string Member)> stateMachines)
    {
        if (stateMachines.TryGetValue(typeName, out var source))
        {
            return new Finding(assemblyName, kind, source.Type, source.Member, detail);
        }

        if (IsCompilerGeneratedName(typeName) || IsCompilerGeneratedName(memberName))
        {
            // §5 — atribuimi NUK provohet dot: fail-closed, kurrë hamendje.
            return new Finding(
                assemblyName, "unattributed-compiler-generated", typeName, memberName,
                $"{kind}:{detail}");
        }

        return new Finding(assemblyName, kind, typeName, memberName, detail);
    }

    private static bool IsCompilerGeneratedName(string name)
        => name.Contains('<', StringComparison.Ordinal) || name.Contains('>', StringComparison.Ordinal);

    // ─────────────────── §6 — Owner public-surface guard ───────────────────

    /// <summary>
    /// §6: sipërfaqja PUBLIKE e DEKLARUAR e një owner assembly-je nuk lejohet të
    /// PRANOJË (parametra) as të DOREZOJË (kthim/fushë publike) kapacitet raw:
    /// `DbConnection`/`DbCommand`/Npgsql*, delegate që i bart ato, ose parametra
    /// me emra SQL/schema/tabelë.
    ///
    /// Kufij të DEKLARUAR: (a) mbulohen anëtarët e DEKLARUAR në atë assembly —
    /// sipërfaqja e trashëguar nga framework-u (p.sh. `DbContext.Database`)
    /// është sipërfaqe framework-u dhe qeveriset nga skaneri i sink-eve në ÇDO
    /// call-site; (b) dorëzimi i kontekstit TË TIPIZUAR `PlatformDbContext`
    /// NUK trajtohet si kapacitet raw — dalja raw prej tij (`GetDbConnection`)
    /// është vetë sink i skanuar dhe i manifestuar.
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
            return [new Finding(assemblyName, "unreadable-assembly", "-", "-", exception.GetType().Name)];
        }

        using (stream)
        using (portableExecutable)
        {
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                if (!IsPubliclyVisible(reader, typeHandle))
                {
                    continue;
                }

                var type = reader.GetTypeDefinition(typeHandle);
                var typeName = FullNameOfTypeDefinition(reader, typeHandle);

                foreach (var fieldHandle in type.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    var visibility = field.Attributes & FieldAttributes.FieldAccessMask;
                    if (visibility is not (FieldAttributes.Public or FieldAttributes.Family
                        or FieldAttributes.FamORAssem))
                    {
                        continue;
                    }

                    string fieldType;
                    try
                    {
                        fieldType = field.DecodeSignature(
                            new CollectingTypeProvider(new HashSet<string>(StringComparer.Ordinal)),
                            genericContext: null);
                    }
                    catch (Exception exception)
                    {
                        findings.Add(new Finding(
                            assemblyName, "unreadable-field-signature", typeName,
                            reader.GetString(field.Name), exception.GetType().Name));
                        continue;
                    }

                    foreach (var raw in ExtractTypeComponents(fieldType)
                        .Where(RawDataSinkTaxonomy.IsRawCapabilityType).Order(StringComparer.Ordinal))
                    {
                        findings.Add(new Finding(
                            assemblyName, "owner-public-raw-field", typeName,
                            reader.GetString(field.Name), raw));
                    }
                }

                foreach (var methodHandle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    var visibility = method.Attributes & MethodAttributes.MemberAccessMask;
                    if (visibility is not (MethodAttributes.Public or MethodAttributes.Family
                        or MethodAttributes.FamORAssem))
                    {
                        continue;
                    }

                    var memberName = reader.GetString(method.Name);
                    MethodSignature<string> signature;
                    try
                    {
                        signature = method.DecodeSignature(
                            new CollectingTypeProvider(new HashSet<string>(StringComparer.Ordinal)),
                            genericContext: null);
                    }
                    catch (Exception exception)
                    {
                        findings.Add(new Finding(
                            assemblyName, "unreadable-method-signature", typeName,
                            memberName, exception.GetType().Name));
                        continue;
                    }

                    // Komponentët nxirren me ndarje EKZAKTE mbi kufijtë e
                    // sinjaturës (`<>,[]&*`) — jo krahasim substring, që
                    // `NpgsqlConnection` të mos e kapë `NpgsqlConnectionStringBuilder`.
                    foreach (var raw in signature.ParameterTypes.SelectMany(ExtractTypeComponents)
                        .Where(RawDataSinkTaxonomy.IsRawCapabilityType).Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal))
                    {
                        findings.Add(new Finding(
                            assemblyName, "owner-public-raw-parameter", typeName, memberName, raw));
                    }

                    foreach (var raw in ExtractTypeComponents(signature.ReturnType)
                        .Where(RawDataSinkTaxonomy.IsRawCapabilityType).Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal))
                    {
                        findings.Add(new Finding(
                            assemblyName, "owner-public-raw-return", typeName, memberName, raw));
                    }

                    foreach (var parameterHandle in method.GetParameters())
                    {
                        var parameter = reader.GetParameter(parameterHandle);
                        if (parameter.SequenceNumber == 0 || parameter.Name.IsNil)
                        {
                            continue;
                        }

                        var parameterName = reader.GetString(parameter.Name);
                        if (RawDataSinkTaxonomy.RawSqlParameterNames.Contains(parameterName))
                        {
                            findings.Add(new Finding(
                                assemblyName, "owner-public-sql-parameter", typeName, memberName, parameterName));
                        }
                    }
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// Ndan një tip të dekoduar (p.sh. `System.Func&lt;Npgsql.NpgsqlConnection,Int32&gt;[]&amp;`)
    /// në KOMPONENTËT e tij të plotë, që krahasimi me taksonominë të jetë barazi
    /// EKZAKTE dhe jo `Contains` — kufi pike i detyrueshëm.
    /// </summary>
    internal static IEnumerable<string> ExtractTypeComponents(string decodedType)
        => decodedType.Split(['<', '>', ',', '[', ']', '&', '*'], StringSplitOptions.RemoveEmptyEntries)
            .Select(component => component.Trim())
            .Where(component => component.Length > 0);

    private static bool IsPubliclyVisible(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        if (visibility == TypeAttributes.Public)
        {
            return true;
        }

        if (visibility is not (TypeAttributes.NestedPublic or TypeAttributes.NestedFamily
            or TypeAttributes.NestedFamORAssem))
        {
            return false;
        }

        var declaring = type.GetDeclaringType();
        return !declaring.IsNil && IsPubliclyVisible(reader, declaring);
    }

    // ─────────────────── Ndihmësit e emrave/sinjaturave ───────────────────

    internal static string FullNameOfTypeReference(MetadataReader reader, TypeReferenceHandle handle)
    {
        var typeReference = reader.GetTypeReference(handle);
        var name = reader.GetString(typeReference.Name);
        if (typeReference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return FullNameOfTypeReference(reader, (TypeReferenceHandle)typeReference.ResolutionScope) + "+" + name;
        }

        var @namespace = typeReference.Namespace.IsNil ? string.Empty : reader.GetString(typeReference.Namespace);
        return @namespace.Length == 0 ? name : $"{@namespace}.{name}";
    }

    internal static string FullNameOfTypeDefinition(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return FullNameOfTypeDefinition(reader, declaring) + "+" + name;
        }

        var @namespace = type.Namespace.IsNil ? string.Empty : reader.GetString(type.Namespace);
        return @namespace.Length == 0 ? name : $"{@namespace}.{name}";
    }

    private static string CustomAttributeTypeFullName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                {
                    var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                    return member.Parent.Kind == HandleKind.TypeReference
                        ? FullNameOfTypeReference(reader, (TypeReferenceHandle)member.Parent)
                        : string.Empty;
                }

            case HandleKind.MethodDefinition:
                {
                    var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                    return FullNameOfTypeDefinition(reader, method.GetDeclaringType());
                }

            default:
                return string.Empty;
        }
    }

    private static string DecodeTypeSpecification(
        MetadataReader reader, TypeSpecificationHandle handle, HashSet<string> collected)
    {
        var blobReader = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
        return new SignatureDecoder<string, object?>(
            new CollectingTypeProvider(collected), reader, genericContext: null).DecodeType(ref blobReader);
    }

    /// <summary>
    /// Provider që MBLEDH çdo emër tipi të hasur në sinjaturë (barazi EKZAKTE
    /// më vonë) — jo krahasim substring mbi tekstin e sinjaturës.
    /// </summary>
    private sealed class CollectingTypeProvider(HashSet<string> collected)
        : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var name = FullNameOfTypeDefinition(reader, handle);
            collected.Add(name);
            return name;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var name = FullNameOfTypeReference(reader, handle);
            collected.Add(name);
            return name;
        }

        public string GetTypeFromSpecification(
            MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => DecodeTypeSpecification(reader, handle, collected);

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

    /// <summary>Provider minimal për të lexuar `typeof(...)` nga blob-i i një attribute-i.</summary>
    private sealed class SerializedTypeNameProvider : ICustomAttributeTypeProvider<string>
    {
        internal static readonly SerializedTypeNameProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => "System.Type";

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => FullNameOfTypeDefinition(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => FullNameOfTypeReference(reader, handle);

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => type == "System.Type";
    }
}
