using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.CompilerServices.SymbolWriter;

namespace SensotronicaIL;

public class CecilMethodBodyCloner
{
    private readonly ILProcessor _il;
    private readonly ModuleDefinition _targetModule;
    private readonly MethodDefinition _sourceMethod;
    private readonly MethodDefinition _targetMethod;

    private readonly Dictionary<VariableDefinition, VariableDefinition> _variableMap = new Dictionary<VariableDefinition, VariableDefinition>();
    private readonly Dictionary<Instruction, Instruction> _instructionMap = new Dictionary<Instruction, Instruction>();
    private readonly List<Instruction> _newInstructions = new List<Instruction>();

    public CecilMethodBodyCloner(MethodDefinition sourceMethod, MethodDefinition targetMethod)
    {
        _sourceMethod = sourceMethod;
        _targetMethod = targetMethod;
        _il = _targetMethod.Body.GetILProcessor();
        _targetModule = targetMethod.Module;

        Init();
    }

    private void Init()
    {
        foreach (var oldVar in _sourceMethod.Body.Variables)
        {
            TypeReference newVarType;
            if (oldVar.VariableType.IsGenericParameter)
            {
                var genericParam = oldVar.VariableType as GenericParameter;
                if(genericParam.Owner == _sourceMethod)
                {
                    newVarType = _targetMethod.GenericParameters[genericParam.Position];
                }
                else
                {
                    newVarType = _targetMethod.DeclaringType.GenericParameters[genericParam.Position];
                }
            }
            else
            {
                newVarType = _targetModule.ImportReference(oldVar.VariableType);
            }
            var newVar = new VariableDefinition(newVarType);
            _targetMethod.Body.Variables.Add(newVar);
            _variableMap[oldVar] = newVar;
        }

        foreach (var oldInstr in _sourceMethod.Body.Instructions)
        {
            var newInstr = Instruction.Create(OpCodes.Nop);
            _instructionMap[oldInstr] = newInstr;
            _newInstructions.Add(newInstr);
        }

        for (int i = 0; i < _sourceMethod.Body.Instructions.Count; i++)
        {
            var oldInstr = _sourceMethod.Body.Instructions[i];
            var newInstr = _newInstructions[i];

            newInstr.OpCode = oldInstr.OpCode;

            if (oldInstr.Operand == null)
            {
                continue;
            }

            var context = _targetMethod;

            switch (oldInstr.OpCode.OperandType)
            {
                case OperandType.ShortInlineBrTarget:
                case OperandType.InlineBrTarget:
                    newInstr.Operand = _instructionMap[oldInstr.Operand as Instruction];
                    break;

                case OperandType.InlineSwitch:
                    var oldTargets = oldInstr.Operand as Instruction[];
                    var newTargets = new Instruction[oldTargets.Length];
                    for (int j = 0; j < oldTargets.Length; j++)
                    {
                        newTargets[j] = _instructionMap[oldTargets[j]];
                    }
                    newInstr.Operand = newTargets;
                    break;

                case OperandType.ShortInlineVar:
                case OperandType.InlineVar:
                    newInstr.Operand = _variableMap[oldInstr.Operand as VariableDefinition];
                    break;

                case OperandType.ShortInlineArg:
                case OperandType.InlineArg:
                    var paramDef = oldInstr.Operand as ParameterDefinition;
                    newInstr.Operand = _targetMethod.Parameters[paramDef.Index];
                    break;

                case OperandType.InlineField:
                    var fieldRef = (FieldReference)oldInstr.Operand;
                    var resolvedFieldRef = ResolveDisplayClassMemberReference(fieldRef) as FieldReference;

                    newInstr.Operand = _targetModule.ImportReference(resolvedFieldRef);
                    break;

                case OperandType.InlineMethod:
                    var methodRef = oldInstr.Operand as MethodReference;
                    
                    if (methodRef is GenericInstanceMethod oldInstance)
                    {
                        var newElementMethod = _targetModule.ImportReference(oldInstance.ElementMethod, context);
                        var newInstance = new GenericInstanceMethod(newElementMethod);

                        foreach (var arg in oldInstance.GenericArguments)
                        {
                            TypeReference newArg;
                            if (arg.IsGenericParameter)
                            {
                                var gp = (GenericParameter)arg;

                                if (gp.Owner.Equals(_sourceMethod))
                                {
                                    newArg = context.GenericParameters[gp.Position];
                                }
                                else
                                {
                                    newArg = context.DeclaringType.GenericParameters[gp.Position];
                                }
                            }
                            else
                            {
                                newArg = _targetModule.ImportReference(arg, context);
                            }
                            newInstance.GenericArguments.Add(newArg);
                        }
                        newInstr.Operand = newInstance;
                    }
                    else
                    {
                        var resolvedMethodRef = ResolveDisplayClassMemberReference(methodRef) as MethodReference;
                        newInstr.Operand = _targetModule.ImportReference(resolvedMethodRef, context)
                                           ?? _targetModule.ImportReference(resolvedMethodRef);
                    }
                    break;
                case OperandType.InlineType:
                    var oldTypeRef = (TypeReference)oldInstr.Operand;
                    if (oldTypeRef.IsGenericParameter)
                    {
                        var gp = (GenericParameter)oldTypeRef;
                        if (gp.Owner == _sourceMethod)
                        {
                            newInstr.Operand = context.GenericParameters[gp.Position];
                        }
                        else
                        {
                            newInstr.Operand = context.DeclaringType.GenericParameters[gp.Position];
                        }
                    }
                    else
                    {
                        newInstr.Operand = _targetModule.ImportReference(oldTypeRef, context);
                    }
                    break;
                case OperandType.InlineTok:
                    if (oldInstr.Operand is TypeReference tr)
                        newInstr.Operand = _targetModule.ImportReference(tr, context);
                    else if (oldInstr.Operand is FieldReference fr)
                    {
                        if (fr.DeclaringType.IsGenericInstance) newInstr.Operand = _targetModule.ImportReference(fr);
                        else newInstr.Operand = _targetModule.ImportReference(fr, context);
                    }
                    else if (oldInstr.Operand is MethodReference mr)
                    {
                        if (mr.DeclaringType.IsGenericInstance) newInstr.Operand = _targetModule.ImportReference(mr);
                        else newInstr.Operand = _targetModule.ImportReference(mr, context);
                    }
                    else throw new InvalidOperationException("Invalid token operand.");
                    break;

                case OperandType.InlineSig:
                    var oldCallSite = oldInstr.Operand as CallSite;

                    var newCallSite = new CallSite(_targetModule.ImportReference(oldCallSite.ReturnType, context));

                    newCallSite.CallingConvention = oldCallSite.CallingConvention;
                    newCallSite.HasThis = oldCallSite.HasThis;
                    newCallSite.ExplicitThis = oldCallSite.ExplicitThis;
                    
                    foreach (var param in oldCallSite.Parameters)
                    {
                        newCallSite.Parameters.Add(
                            new ParameterDefinition(_targetModule.ImportReference(param.ParameterType, context))
                        );
                    }

                    newInstr.Operand = newCallSite;
                    break;

                case OperandType.InlineI:
                case OperandType.InlineI8:
                case OperandType.InlineR:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineR:
                case OperandType.InlineString:
                    newInstr.Operand = oldInstr.Operand;
                    break;

                default:
                    throw new NotSupportedException($"Unsupported operand type: {oldInstr.OpCode.OperandType}");
            }
        }
    }

    private MemberReference ResolveDisplayClassMemberReference(MemberReference member)
    {
        var declaringType = member.DeclaringType;
        if (declaringType is GenericInstanceType genericInstanceType && genericInstanceType.ContainsGenericParameter)
        {
            var newElementType = _targetModule.ImportReference(genericInstanceType.ElementType);
            var newDeclaringType = new GenericInstanceType(newElementType);

            foreach (var p in _sourceMethod.GenericParameters)
            {
                newDeclaringType.GenericArguments.Add(_targetMethod.GenericParameters[p.Position]);
            }

            if (member is FieldReference field)
            {
                var importedFieldType = _targetModule.ImportReference(field.FieldType, newDeclaringType);
                return new FieldReference(field.Name, importedFieldType, newDeclaringType);
            }

            if (member is MethodReference method)
            {
                var importedReturnType = _targetModule.ImportReference(method.ReturnType, newDeclaringType);
                var newMethod = new MethodReference(method.Name, importedReturnType, newDeclaringType)
                {
                    HasThis = method.HasThis,
                    ExplicitThis = method.ExplicitThis,
                    CallingConvention = method.CallingConvention
                };

                foreach (var p in method.Parameters)
                {
                    var importedParameterType = _targetModule.ImportReference(p.ParameterType, newDeclaringType);
                    newMethod.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, importedParameterType));
                }
                return newMethod;
            }

        }

        return member;
    }
    public void FinishMethod()
    {
        foreach (var instr in _newInstructions)
        {
            _il.Append(instr);
        }

        if (_sourceMethod.Body.HasExceptionHandlers)
        {
            foreach (var oldHandler in _sourceMethod.Body.ExceptionHandlers)
            {
                var newHandler = new ExceptionHandler(oldHandler.HandlerType);

                newHandler.TryStart = _instructionMap[oldHandler.TryStart];
                newHandler.TryEnd = oldHandler.TryEnd == null ? null : _instructionMap[oldHandler.TryEnd];
                newHandler.HandlerStart = _instructionMap[oldHandler.HandlerStart];
                newHandler.HandlerEnd = oldHandler.HandlerEnd == null ? null : _instructionMap[oldHandler.HandlerEnd];

                if (oldHandler.CatchType != null)
                {
                    newHandler.CatchType = _targetModule.ImportReference(oldHandler.CatchType);
                }
                if (oldHandler.FilterStart != null)
                {
                    newHandler.FilterStart = _instructionMap[oldHandler.FilterStart];
                }

                _targetMethod.Body.ExceptionHandlers.Add(newHandler);
            }
        }
    }
}
