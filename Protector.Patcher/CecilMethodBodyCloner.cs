using Mono.Cecil;
using Mono.Cecil.Cil;
using SensotronicaIL.Extensions;

namespace Protector.Patcher;
public class CecilMethodBodyCloner
{
    private readonly MethodDefinition _sourceMethod;
    private readonly MethodDefinition _targetMethod;
    private readonly CloningContext _context;

    private readonly Dictionary<VariableDefinition, VariableDefinition> _variableMap = new Dictionary<VariableDefinition, VariableDefinition>();
    private readonly Dictionary<Instruction, Instruction> _instructionMap = new Dictionary<Instruction, Instruction>();

    public CecilMethodBodyCloner(MethodDefinition sourceMethod, MethodDefinition targetMethod)
    {
        _sourceMethod = sourceMethod;
        _targetMethod = targetMethod;
        _context = new CloningContext(targetMethod.Module);
    }
    internal CecilMethodBodyCloner(MethodDefinition sourceMethod, MethodDefinition targetMethod, CloningContext context)
    {
        _sourceMethod = sourceMethod;
        _targetMethod = targetMethod;
        _context = context;
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
            TypeReference newVarType;
            if (oldVar.VariableType.IsGenericParameter)
            {
                var genericParam = oldVar.VariableType as GenericParameter;
                if (genericParam.Owner == _sourceMethod)
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
                if (oldVar.VariableType is GenericInstanceType genericInstanceType && genericInstanceType.ContainsGenericParameter)
                {
                    var newElementType = _context.TargetModule.ImportReference(genericInstanceType.ElementType);
                    var newDeclaringType = new GenericInstanceType(newElementType);
                    foreach (var p in _sourceMethod.GenericParameters)
                    {
                        newDeclaringType.GenericArguments.Add(_targetMethod.GenericParameters[p.Position]);
                    }
                    newVarType = newDeclaringType;
                }
                else
                    newVarType = _context.TargetModule.ImportReference(oldVar.VariableType);
            }
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

            newInstr = ProcessInstruction(oldInstr, newInstr);
        }
        foreach (var instr in newInstructions)
        {
            il.Append(instr);
        }
    }

    //TODO: some refactoring to prevent code duplication
    private Instruction ProcessInstruction(Instruction oldInstr, Instruction newInstr)
    {
        newInstr.OpCode = oldInstr.OpCode;

        if (oldInstr.Operand == null)
        {
            return newInstr;
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

                newInstr.Operand = _context.TargetModule.ImportReference(resolvedFieldRef);
                break;

            case OperandType.InlineMethod:
                var methodRef = oldInstr.Operand as MethodReference;

                if (methodRef is GenericInstanceMethod oldInstance)
                {
                    var newElementMethod = _context.TargetModule.ImportReference(oldInstance.ElementMethod, _targetMethod);
                    var newInstance = new GenericInstanceMethod(newElementMethod);

                    foreach (var arg in oldInstance.GenericArguments)
                    {
                        TypeReference newArg;
                        if (arg.IsGenericParameter)
                        {
                            var gp = (GenericParameter)arg;

                            if (gp.Owner.Equals(_sourceMethod))
                            {
                                newArg = _targetMethod.GenericParameters[gp.Position];
                            }
                            else
                            {
                                newArg = _targetMethod.DeclaringType.GenericParameters[gp.Position];
                            }
                        }
                        else if (arg.IsGenericInstance)
                        {
                            newArg = ResolveDisplayClassTypeReference(arg);
                        }
                        else
                        {
                            newArg = _context.TargetModule.ImportReference(arg, _targetMethod);
                        }
                        newInstance.GenericArguments.Add(newArg);
                    }
                    newInstr.Operand = newInstance;
                }
                else
                {
                    var resolvedMethodRef = ResolveDisplayClassMemberReference(methodRef) as MethodReference;
                    newInstr.Operand = _context.TargetModule.ImportReference(resolvedMethodRef, _targetMethod)
                                       ?? _context.TargetModule.ImportReference(resolvedMethodRef);
                }
                break;
            case OperandType.InlineType:
                var oldTypeRef = (TypeReference)oldInstr.Operand;
                if (oldTypeRef.IsGenericParameter)
                {
                    var gp = (GenericParameter)oldTypeRef;
                    if (gp.Owner == _sourceMethod)
                    {
                        newInstr.Operand = _targetMethod.GenericParameters[gp.Position];
                    }
                    else
                    {
                        newInstr.Operand = _targetMethod.DeclaringType.GenericParameters[gp.Position];
                    }
                }
                else
                {
                    newInstr.Operand = _context.TargetModule.ImportReference(oldTypeRef, _targetMethod);
                }
                break;
            case OperandType.InlineTok:
                if (oldInstr.Operand is TypeReference tr)
                    newInstr.Operand = _context.TargetModule.ImportReference(tr, _targetMethod);
                else if (oldInstr.Operand is FieldReference fr)
                {
                    if (fr.DeclaringType.IsGenericInstance) newInstr.Operand = _context.TargetModule.ImportReference(fr);
                    else newInstr.Operand = _context.TargetModule.ImportReference(fr, _targetMethod);
                }
                else if (oldInstr.Operand is MethodReference mr)
                {
                    if (mr.DeclaringType.IsGenericInstance) newInstr.Operand = _context.TargetModule.ImportReference(mr);
                    else newInstr.Operand = _context.TargetModule.ImportReference(mr, _targetMethod);
                }
                else throw new InvalidOperationException("Invalid token operand.");
                break;

            case OperandType.InlineSig:
                var oldCallSite = oldInstr.Operand as CallSite;

                var newCallSite = new CallSite(_context.TargetModule.ImportReference(oldCallSite.ReturnType, _targetMethod));

                newCallSite.CallingConvention = oldCallSite.CallingConvention;
                newCallSite.HasThis = oldCallSite.HasThis;
                newCallSite.ExplicitThis = oldCallSite.ExplicitThis;

                foreach (var param in oldCallSite.Parameters)
                {
                    newCallSite.Parameters.Add(
                        new ParameterDefinition(_context.TargetModule.ImportReference(param.ParameterType, _targetMethod))
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
        return newInstr;
    }
    private TypeReference ResolveDisplayClassTypeReference(TypeReference type)
    {
        if(type is GenericInstanceType genericInstanceType && genericInstanceType.ContainsGenericParameter)
        {
            var newElementType = _context.TargetModule.ImportReference(genericInstanceType.ElementType);
            var newDeclaringType = new GenericInstanceType(newElementType);

            foreach (var p in _sourceMethod.GenericParameters)
            {
                newDeclaringType.GenericArguments.Add(_targetMethod.GenericParameters[p.Position]);
            }
            return newDeclaringType;
        }
        return type;
    }
    private MemberReference ResolveDisplayClassMemberReference(MemberReference member)
    {
        var declaringType = member.DeclaringType;
        if (declaringType is GenericInstanceType genericInstanceType && genericInstanceType.ContainsGenericParameter)
        {
            var newElementType = _context.TargetModule.ImportReference(genericInstanceType.ElementType);
            var newDeclaringType = new GenericInstanceType(newElementType);

            foreach (var p in _sourceMethod.GenericParameters)
            {
                newDeclaringType.GenericArguments.Add(_targetMethod.GenericParameters[p.Position]);
            }

            if (member is FieldReference field)
            {
                var importedFieldType = _context.TargetModule.ImportReference(field.FieldType, newDeclaringType);
                return new FieldReference(field.Name, importedFieldType, newDeclaringType);
            }

            if (member is MethodReference method)
            {
                var importedReturnType = _context.TargetModule.ImportReference(method.ReturnType, newDeclaringType);
                var newMethod = new MethodReference(method.Name, importedReturnType, newDeclaringType)
                {
                    HasThis = method.HasThis,
                    ExplicitThis = method.ExplicitThis,
                    CallingConvention = method.CallingConvention
                };

                foreach (var p in method.Parameters)
                {
                    var importedParameterType = _context.TargetModule.ImportReference(p.ParameterType, newDeclaringType);
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
                CatchType = oldHandler.CatchType == null ? null : _context.TargetModule.ImportReference(oldHandler.CatchType, _targetMethod),
                FilterStart = oldHandler.FilterStart == null ? null : _instructionMap[oldHandler.FilterStart]
            };
            _targetMethod.Body.ExceptionHandlers.Add(newHandler);
        }
    }

    // This method is used to clone dependencies of the source method, such as types that are compiler-generated.
    // Has potential to throw exceptions if the types has generic parameters.
    // Not used as it is not needed in the current implementation as we remove the method body using Mono.Cecil (-> all compiler-generated types remain in the module).
    private void CloneDependencies()
    {
        var requiredTypes = new HashSet<TypeDefinition>();
        foreach (var instruction in _sourceMethod.Body.Instructions)
        {
            if (instruction.Operand is MemberReference memberRef)
            {
                var declaringType = memberRef.DeclaringType?.Resolve();
                if (declaringType != null && declaringType.IsCompilerGenerated())
                {
                    requiredTypes.Add(declaringType);
                }
            }
        }
        foreach (var typeToClone in requiredTypes)
        {
            TypeCloner cloner = new TypeCloner(typeToClone, _context);
            var type = cloner.Clone();
        }
    }
}
