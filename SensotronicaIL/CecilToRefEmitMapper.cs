using Mono.Cecil.Cil;

using NETOpcodes = System.Reflection.Emit.OpCodes;

namespace SensotronicaIL;
public static class CecilToRefEmitMapper
{
    private static readonly Dictionary<Code, System.Reflection.Emit.OpCode> _opCodeMap =
        new Dictionary<Code, System.Reflection.Emit.OpCode>();

    static CecilToRefEmitMapper()
    {
        foreach (var field in typeof(System.Reflection.Emit.OpCodes).GetFields())
        {
            if (field.IsStatic)
            {
                var emitOpCode = (System.Reflection.Emit.OpCode)field.GetValue(null);
                if (System.Enum.TryParse<Code>(field.Name, true, out var cecilCode))
                {
                    _opCodeMap[cecilCode] = emitOpCode;
                }
            }
        }

        _opCodeMap[Code.Readonly] = NETOpcodes.Readonly;
    }

    public static System.Reflection.Emit.OpCode MapOpCode(OpCode cecilOpCode)
    {
        if (!_opCodeMap.TryGetValue(cecilOpCode.Code, out var emitOpCode))
        {
            throw new NotSupportedException($"OpCode not supported: {cecilOpCode.Name}");
        }
        return emitOpCode;
    }
}
