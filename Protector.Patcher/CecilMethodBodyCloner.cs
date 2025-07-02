using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Protector.Patcher;
public class CecilMethodBodyCloner
{
    private readonly MethodDefinition _sourceMethod;
    private readonly MethodDefinition _targetMethod;
    private readonly ModuleDefinition _targetModule;

    private readonly Dictionary<VariableDefinition, VariableDefinition> _variableMap = new Dictionary<VariableDefinition, VariableDefinition>();
    private readonly Dictionary<Instruction, Instruction> _instructionMap = new Dictionary<Instruction, Instruction>();

    public CecilMethodBodyCloner(MethodDefinition sourceMethod, MethodDefinition targetMethod)
    {
        _sourceMethod = sourceMethod;
        _targetMethod = targetMethod;
        _targetModule = targetMethod.Module;
    }

    public void Clone()
    {
        if (!_sourceMethod.HasBody) return;

        _targetMethod.Body = new MethodBody(_targetMethod);

        CloneVariables();
        CloneInstructions();
        CloneExceptionHandlers();
    }

    private void CloneVariables()
    {
        foreach(var oldVar in _sourceMethod.Body.Variables)
        {
            TypeReference newVarType = PatcherHelper.ResolveTypeReference(oldVar.VariableType, _targetModule, _sourceMethod, _targetMethod);

            var newVar = new VariableDefinition(newVarType);
            _targetMethod.Body.Variables.Add(newVar);
            _variableMap[oldVar] = newVar;
        }
    }

    private void CloneInstructions()
    {
        var il = _targetMethod.Body.GetILProcessor();
        var newInstructions = new List<Instruction>();

        foreach (var oldInstr in _sourceMethod.Body.Instructions)
        {
            var newInstr = Instruction.Create(OpCodes.Nop);
            _instructionMap[oldInstr] = newInstr;
            newInstructions.Add(newInstr);
        }

        for (int i = 0; i < newInstructions.Count; i++)
        {
            var newInstr = newInstructions[i];
            var oldInstr = _sourceMethod.Body.Instructions[i];

            ProcessInstruction(oldInstr, newInstr);
        }
        foreach (var instr in newInstructions)
        {
            il.Append(instr);
        }
    }

    private void ProcessInstruction(Instruction oldInstr, Instruction newInstr)
    {
        newInstr.OpCode = oldInstr.OpCode;

        if (oldInstr.Operand == null)
        {
            return;
        }

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
                    var elementMethod = oldInstance.ElementMethod;

                    var importedDeclaringType = PatcherHelper.ResolveTypeReference(elementMethod.DeclaringType, _targetModule, _sourceMethod, _targetMethod);

                    var newElementMethod = new MethodReference(
                        elementMethod.Name,
                        _targetModule.ImportReference(elementMethod.ReturnType, _targetMethod), 
                        importedDeclaringType) 
                    {
                        HasThis = elementMethod.HasThis,
                        ExplicitThis = elementMethod.ExplicitThis,
                        CallingConvention = elementMethod.CallingConvention
                    };

                    foreach (var gp in elementMethod.GenericParameters)
                    {
                        newElementMethod.GenericParameters.Add(new GenericParameter(gp.Name, newElementMethod));
                    }

                    foreach (var p in elementMethod.Parameters)
                    {
                        TypeReference parameterTypeToUse;
                        var baseType = p.ParameterType.IsByReference ? p.ParameterType.GetElementType() : p.ParameterType;

                        if (baseType.IsGenericParameter)
                        {
                            parameterTypeToUse = p.ParameterType;
                        }
                        else
                        {
                            parameterTypeToUse = _targetModule.ImportReference(p.ParameterType, _targetMethod);
                        }

                        var newParameter = new ParameterDefinition(p.Name, p.Attributes, parameterTypeToUse);

                        if (p.HasConstant)
                        {
                            newParameter.Constant = p.Constant;
                        }
                        foreach (var ca in p.CustomAttributes)
                        {
                            newParameter.CustomAttributes.Add(new CustomAttribute(_targetModule.ImportReference(ca.Constructor)));
                        }
                        newElementMethod.Parameters.Add(newParameter);
                    }
                    var newInstance = new GenericInstanceMethod(newElementMethod);

                    foreach (var arg in oldInstance.GenericArguments)
                    {
                        var importedType = PatcherHelper.ResolveTypeReference(arg, _targetModule, _sourceMethod, _targetMethod);
                        newInstance.GenericArguments.Add(importedType);
                    }
                    newInstr.Operand = newInstance;
                }
                else
                {
                    var resolvedMethodRef = ResolveDisplayClassMemberReference(methodRef) as MethodReference;
                    newInstr.Operand = _targetModule.ImportReference(resolvedMethodRef, _targetMethod)
                                       ?? _targetModule.ImportReference(resolvedMethodRef);
                }
                break;
            case OperandType.InlineType:
                var oldTypeRef = (TypeReference)oldInstr.Operand;
                newInstr.Operand = PatcherHelper.ResolveTypeReference(oldTypeRef, _targetModule, _sourceMethod, _targetMethod);
                break;
            case OperandType.InlineTok:
                if (oldInstr.Operand is TypeReference tr)
                    newInstr.Operand = _targetModule.ImportReference(tr, _targetMethod);
                else if (oldInstr.Operand is FieldReference fr)
                {
                    if (fr.DeclaringType.IsGenericInstance) newInstr.Operand = _targetModule.ImportReference(fr);
                    else newInstr.Operand = _targetModule.ImportReference(fr, _targetMethod);
                }
                else if (oldInstr.Operand is MethodReference mr)
                {
                    if (mr.DeclaringType.IsGenericInstance) newInstr.Operand = _targetModule.ImportReference(mr);
                    else newInstr.Operand = _targetModule.ImportReference(mr, _targetMethod);
                }
                else throw new InvalidOperationException("Invalid token operand.");
                break;

            case OperandType.InlineSig:
                var oldCallSite = oldInstr.Operand as CallSite;

                var newCallSite = new CallSite(_targetModule.ImportReference(oldCallSite.ReturnType, _targetMethod));

                newCallSite.CallingConvention = oldCallSite.CallingConvention;
                newCallSite.HasThis = oldCallSite.HasThis;
                newCallSite.ExplicitThis = oldCallSite.ExplicitThis;

                foreach (var param in oldCallSite.Parameters)
                {
                    newCallSite.Parameters.Add(
                        new ParameterDefinition(_targetModule.ImportReference(param.ParameterType, _targetMethod))
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
        return;
    }
    private MemberReference ResolveDisplayClassMemberReference(MemberReference member)
    {
        var declaringType = member.DeclaringType;
        if (declaringType is GenericInstanceType genericInstanceType && genericInstanceType.ContainsGenericParameter)
        {
            var newElementType = _targetModule.ImportReference(genericInstanceType.ElementType);
            var newDeclaringType = new GenericInstanceType(newElementType);

            foreach (var p in genericInstanceType.GenericArguments)
            {
                TypeReference importedType = PatcherHelper.ResolveTypeReference(p, _targetModule, _sourceMethod, _targetMethod);
                newDeclaringType.GenericArguments.Add(importedType);
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

    private void CloneExceptionHandlers()
    {
        if (!_sourceMethod.Body.HasExceptionHandlers) return;

        foreach (var oldHandler in _sourceMethod.Body.ExceptionHandlers)
        {
            var newHandler = new ExceptionHandler(oldHandler.HandlerType)
            {
                TryStart = _instructionMap[oldHandler.TryStart],
                TryEnd = oldHandler.TryEnd == null ? null : _instructionMap[oldHandler.TryEnd],
                HandlerStart = _instructionMap[oldHandler.HandlerStart],
                HandlerEnd = oldHandler.HandlerEnd == null ? null : _instructionMap[oldHandler.HandlerEnd],
                CatchType = oldHandler.CatchType == null ? null : _targetModule.ImportReference(oldHandler.CatchType, _targetMethod),
                FilterStart = oldHandler.FilterStart == null ? null : _instructionMap[oldHandler.FilterStart]
            };
            _targetMethod.Body.ExceptionHandlers.Add(newHandler);
        }
    }
}
