namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Dekodues instruksionesh IL me tabelë TË PLOTË operand-esh ECMA-335 (Partition
/// III). Ekziston sepse skanimi "i lirë" i bajtëve për token-a do të prodhonte
/// false-positive/false-negative: p.sh. operandi i `ldc.i8` ose një `switch`
/// përmban bajte që duken si token. Për një guard fail-closed kjo do të ishte e
/// papranueshme, prandaj çdo instruksion konsumohet me gjatësinë e vet reale.
///
/// Çdo opcode i pavlefshëm/i rezervuar ose operand i cunguar NDALON dekodimin
/// dhe raportohet përmes <c>decodeError</c> — kurrë "vazhdo në heshtje".
/// </summary>
internal static class IlOperandTable
{
    /// <summary>Instruksion me operand metadata (ose thirrje indirekte `calli`).</summary>
    internal readonly record struct Instruction(int Offset, int MetadataToken, bool IsIndirectCall);

    private const byte Invalid = 0xFF;
    private const byte Switch = 0xFE;
    private const byte Prefix = 0xFD;

    private static readonly byte[] OneByte = BuildOneByte();
    private static readonly byte[] TwoByte = BuildTwoByte();

    /// <summary>Opcode-t një-bajtëshe që bartin token metadata (operand 4-bajtësh).</summary>
    private static readonly bool[] OneByteCarriesToken = BuildOneByteTokenFlags();

    /// <summary>Opcode-t dy-bajtëshe (0xFE xx) që bartin token metadata.</summary>
    private static readonly bool[] TwoByteCarriesToken = BuildTwoByteTokenFlags();

    /// <summary>
    /// Correction C5 (§4) — instruksion i PLOTË, me opcode dhe operandin e
    /// dekoduar. Nevojitet nga analiza dataflow-aware e `ReadVerifiedImage`:
    /// aty duhen edhe `ldloc`/`stloc`, jo vetëm instruksionet me token.
    /// `Opcode` = bajti i vetëm, ose `0xFE00 | second` për format dy-bajtëshe.
    /// </summary>
    internal readonly record struct FullInstruction(int Offset, int Opcode, int Operand);

    /// <summary>
    /// Dekodim i PLOTË i një trupi IL me të njëjtat tabela ECMA-335 si
    /// <see cref="Walk"/> — pra pa asnjë tabelë të dytë që mund të divergjojë.
    /// Çdo opcode i pavlefshëm ose operand i cunguar NDALON dekodimin dhe
    /// raportohet me <c>decodeError</c>; kurrë "vazhdo në heshtje".
    /// </summary>
    internal static IReadOnlyList<FullInstruction> WalkAll(byte[] il, out string? decodeError)
    {
        var instructions = new List<FullInstruction>();
        decodeError = null;
        var offset = 0;

        while (offset < il.Length)
        {
            var start = offset;
            var opcode = il[offset++];

            if (opcode == 0xFE)
            {
                if (offset >= il.Length)
                {
                    decodeError = $"prefiks 0xFE i cunguar @IL_{start:X4}";
                    return instructions;
                }

                var second = il[offset++];
                if (second >= TwoByte.Length || TwoByte[second] == Invalid)
                {
                    decodeError = $"opcode i pavlefshëm 0xFE{second:X2} @IL_{start:X4}";
                    return instructions;
                }

                var twoByteSize = TwoByte[second];
                if (offset + twoByteSize > il.Length)
                {
                    decodeError = $"operand i cunguar për 0xFE{second:X2} @IL_{start:X4}";
                    return instructions;
                }

                instructions.Add(new FullInstruction(
                    start, 0xFE00 | second, ReadOperand(il, offset, twoByteSize)));
                offset += twoByteSize;
                continue;
            }

            var size = OneByte[opcode];
            if (size is Invalid or Prefix)
            {
                decodeError = $"opcode i papranueshëm 0x{opcode:X2} @IL_{start:X4}";
                return instructions;
            }

            if (size == Switch)
            {
                if (offset + 4 > il.Length)
                {
                    decodeError = $"switch i cunguar @IL_{start:X4}";
                    return instructions;
                }

                var count = BitConverter.ToUInt32(il, offset);
                offset += 4;
                var payload = (long)count * 4;
                if (offset + payload > il.Length)
                {
                    decodeError = $"switch me {count} degë jashtë kufijve @IL_{start:X4}";
                    return instructions;
                }

                instructions.Add(new FullInstruction(start, opcode, (int)count));
                offset += (int)payload;
                continue;
            }

            if (offset + size > il.Length)
            {
                decodeError = $"operand i cunguar për 0x{opcode:X2} @IL_{start:X4}";
                return instructions;
            }

            instructions.Add(new FullInstruction(start, opcode, ReadOperand(il, offset, size)));
            offset += size;
        }

        return instructions;
    }

    private static int ReadOperand(byte[] il, int offset, int size) => size switch
    {
        0 => 0,
        1 => il[offset],
        4 => BitConverter.ToInt32(il, offset),
        8 => 0,
        2 => BitConverter.ToUInt16(il, offset),
        _ => 0,
    };

    internal static IReadOnlyList<Instruction> Walk(byte[] il, out string? decodeError)
    {
        var instructions = new List<Instruction>();
        decodeError = null;
        var offset = 0;

        while (offset < il.Length)
        {
            var start = offset;
            var opcode = il[offset++];

            if (opcode == 0xFE)
            {
                if (offset >= il.Length)
                {
                    decodeError = $"prefiks 0xFE i cunguar @IL_{start:X4}";
                    return instructions;
                }

                var second = il[offset++];
                if (second >= TwoByte.Length || TwoByte[second] == Invalid)
                {
                    decodeError = $"opcode i pavlefshëm 0xFE{second:X2} @IL_{start:X4}";
                    return instructions;
                }

                var operandSize = TwoByte[second];
                if (offset + operandSize > il.Length)
                {
                    decodeError = $"operand i cunguar për 0xFE{second:X2} @IL_{start:X4}";
                    return instructions;
                }

                if (TwoByteCarriesToken[second])
                {
                    instructions.Add(new Instruction(start, BitConverter.ToInt32(il, offset), IsIndirectCall: false));
                }

                offset += operandSize;
                continue;
            }

            var size = OneByte[opcode];
            if (size == Invalid)
            {
                decodeError = $"opcode i pavlefshëm 0x{opcode:X2} @IL_{start:X4}";
                return instructions;
            }

            if (size == Prefix)
            {
                decodeError = $"prefiks i papritur 0x{opcode:X2} @IL_{start:X4}";
                return instructions;
            }

            if (size == Switch)
            {
                if (offset + 4 > il.Length)
                {
                    decodeError = $"switch i cunguar @IL_{start:X4}";
                    return instructions;
                }

                var count = BitConverter.ToUInt32(il, offset);
                offset += 4;
                var payload = (long)count * 4;
                if (offset + payload > il.Length)
                {
                    decodeError = $"switch me {count} degë jashtë kufijve @IL_{start:X4}";
                    return instructions;
                }

                offset += (int)payload;
                continue;
            }

            if (offset + size > il.Length)
            {
                decodeError = $"operand i cunguar për 0x{opcode:X2} @IL_{start:X4}";
                return instructions;
            }

            if (opcode == 0x29) // calli — target i pazgjidhshëm statikisht
            {
                instructions.Add(new Instruction(start, 0, IsIndirectCall: true));
            }
            else if (OneByteCarriesToken[opcode])
            {
                instructions.Add(new Instruction(start, BitConverter.ToInt32(il, offset), IsIndirectCall: false));
            }

            offset += size;
        }

        return instructions;
    }

    private static byte[] BuildOneByte()
    {
        var table = new byte[256];
        Array.Fill(table, Invalid);

        void Set(int from, int to, byte value)
        {
            for (var index = from; index <= to; index++)
            {
                table[index] = value;
            }
        }

        // Pa operand.
        Set(0x00, 0x0D, 0);   // nop, break, ldarg.0-3, ldloc.0-3, stloc.0-3
        Set(0x14, 0x1E, 0);   // ldnull, ldc.i4.m1..8
        Set(0x25, 0x26, 0);   // dup, pop
        table[0x2A] = 0;      // ret
        Set(0x46, 0x6E, 0);   // ldind.*/stind.*/aritmetika/conv.*
        table[0x76] = 0;      // conv.r.un
        table[0x7A] = 0;      // throw
        Set(0x82, 0x8B, 0);   // conv.ovf.*.un
        table[0x8E] = 0;      // ldlen
        Set(0x90, 0xA2, 0);   // ldelem.*/stelem.*
        Set(0xB3, 0xBA, 0);   // conv.ovf.*
        table[0xC3] = 0;      // ckfinite
        Set(0xD1, 0xDC, 0);   // conv.u2..sub.ovf.un, endfinally
        table[0xDF] = 0;      // stind.i
        table[0xE0] = 0;      // conv.u

        // Operand 1-bajtësh.
        Set(0x0E, 0x13, 1);   // ldarg.s..stloc.s
        table[0x1F] = 1;      // ldc.i4.s
        Set(0x2B, 0x37, 1);   // br.s..blt.un.s
        table[0xDE] = 1;      // leave.s

        // Operand 4-bajtësh.
        table[0x20] = 4;      // ldc.i4
        table[0x22] = 4;      // ldc.r4
        Set(0x27, 0x29, 4);   // jmp, call, calli
        Set(0x38, 0x44, 4);   // br..blt.un
        Set(0x6F, 0x75, 4);   // callvirt, cpobj, ldobj, ldstr, newobj, castclass, isinst
        table[0x79] = 4;      // unbox
        Set(0x7B, 0x81, 4);   // ldfld..stobj
        Set(0x8C, 0x8D, 4);   // box, newarr
        table[0x8F] = 4;      // ldelema
        Set(0xA3, 0xA5, 4);   // ldelem, stelem, unbox.any
        table[0xC2] = 4;      // refanyval
        table[0xC6] = 4;      // mkrefany
        table[0xD0] = 4;      // ldtoken
        table[0xDD] = 4;      // leave

        // Operand 8-bajtësh.
        table[0x21] = 8;      // ldc.i8
        table[0x23] = 8;      // ldc.r8

        table[0x45] = Switch; // switch
        table[0xFE] = Prefix; // prefiks dy-bajtësh (trajtohet veçmas)

        return table;
    }

    private static byte[] BuildTwoByte()
    {
        var table = new byte[32];
        Array.Fill(table, Invalid);

        void Set(int from, int to, byte value)
        {
            for (var index = from; index <= to; index++)
            {
                table[index] = value;
            }
        }

        Set(0x00, 0x05, 0);   // arglist, ceq, cgt, cgt.un, clt, clt.un
        Set(0x06, 0x07, 4);   // ldftn, ldvirtftn
        Set(0x09, 0x0E, 2);   // ldarg..stloc
        table[0x0F] = 0;      // localloc
        table[0x11] = 0;      // endfilter
        table[0x12] = 1;      // unaligned.
        table[0x13] = 0;      // volatile.
        table[0x14] = 0;      // tail.
        table[0x15] = 4;      // initobj
        table[0x16] = 4;      // constrained.
        table[0x17] = 0;      // cpblk
        table[0x18] = 0;      // initblk
        table[0x19] = 1;      // no.
        table[0x1A] = 0;      // rethrow
        table[0x1C] = 4;      // sizeof
        table[0x1D] = 0;      // refanytype
        table[0x1E] = 0;      // readonly.

        return table;
    }

    private static bool[] BuildOneByteTokenFlags()
    {
        var flags = new bool[256];
        foreach (var opcode in (int[])
        [
            0x27, // jmp        (method)
            0x28, // call       (method)
            0x6F, // callvirt   (method)
            0x70, // cpobj      (type)
            0x71, // ldobj      (type)
            0x73, // newobj     (method)
            0x74, // castclass  (type)
            0x75, // isinst     (type)
            0x79, // unbox      (type)
            0x7B, // ldfld      (field)
            0x7C, // ldflda     (field)
            0x7D, // stfld      (field)
            0x7E, // ldsfld     (field)
            0x7F, // ldsflda    (field)
            0x80, // stsfld     (field)
            0x81, // stobj      (type)
            0x8C, // box        (type)
            0x8D, // newarr     (type)
            0x8F, // ldelema    (type)
            0xA3, // ldelem     (type)
            0xA4, // stelem     (type)
            0xA5, // unbox.any  (type)
            0xC2, // refanyval  (type)
            0xC6, // mkrefany   (type)
            0xD0, // ldtoken    (type/method/field)
        ])
        {
            flags[opcode] = true;
        }

        return flags;
    }

    private static bool[] BuildTwoByteTokenFlags()
    {
        var flags = new bool[32];
        flags[0x06] = true; // ldftn
        flags[0x07] = true; // ldvirtftn
        flags[0x15] = true; // initobj
        flags[0x16] = true; // constrained.
        flags[0x1C] = true; // sizeof
        return flags;
    }
}
