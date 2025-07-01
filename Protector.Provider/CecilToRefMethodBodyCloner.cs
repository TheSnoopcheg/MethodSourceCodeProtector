using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Protector.Provider.Extensions;
using System.Reflection;
using System.Reflection.Emit;

using Cci = Mono.Cecil.Cil;

namespace Protector.Provider;

public class CecilToRefMethodBodyCloner
{
    private readonly ILGenerator _il;
    private readonly MethodDefinition _sourceMethod;
    private readonly Type[]? _typeGenericArgs;
    private readonly Type[]? _methodGenericArgs;
    private readonly Dictionary<Cci.Instruction, Label> _branchLabels = new Dictionary<Cci.Instruction, Label>();

    public CecilToRefMethodBodyCloner(ILGenerator il, MethodDefinition sourceMethod, Type?[]? typeGenericArgs, Type?[]? methodGenericArgs)
    {

        _il = il;
        _sourceMethod = sourceMethod;
        _typeGenericArgs = typeGenericArgs;
        _methodGenericArgs = methodGenericArgs;

        var body = sourceMethod.Body;

        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is Cci.Instruction target)
            {
                if (!_branchLabels.ContainsKey(target))
                {
                    _branchLabels[target] = _il.DefineLabel();
                }
            }
            else if (instruction.Operand is Cci.Instruction[] targets)
            {
                foreach (var t in targets)
                {
                    if (!_branchLabels.ContainsKey(t))
                    {
                        _branchLabels[t] = _il.DefineLabel();
                    }
                }
            }
        }
    }

    public void EmitBody()
    {
        foreach(var variable in _sourceMethod.Body.Variables)
        {
            IGenericParameterContext? context = BuildGenericParameterContext(variable.VariableType, null);
            var localType = ResolveType(variable.VariableType, context);
            if(localType != null)
            {
                _il.DeclareLocal(localType);
            }
        }
        foreach (var instruction in _sourceMethod.Body.Instructions)
        {
            if (_branchLabels.TryGetValue(instruction, out var label))
            {
                _il.MarkLabel(label);
            }

            EmitInstruction(instruction);
        }
    }


    private void EmitInstruction(Cci.Instruction instruction)
    {
        var opCode = CecilToRefEmitMapper.MapOpCode(instruction.OpCode);
        var operand = instruction.Operand;

        switch (instruction.OpCode.OperandType)
        {
            case Cci.OperandType.InlineNone:
                _il.Emit(opCode);
                break;

            case Cci.OperandType.InlineString:
                _il.Emit(opCode, (string)operand);
                break;

            case Cci.OperandType.InlineI:
                _il.Emit(opCode, (int)operand);
                break;

            case Cci.OperandType.InlineI8:
                _il.Emit(opCode, (long)operand);
                break;

            case Cci.OperandType.ShortInlineI:
                _il.Emit(opCode, (sbyte)operand);
                break;

            case Cci.OperandType.InlineR:
                _il.Emit(opCode, (double)operand);
                break;

            case Cci.OperandType.ShortInlineR:
                _il.Emit(opCode, (float)operand);
                break;

            case Cci.OperandType.InlineMethod:
                var methodRef = (MethodReference)operand;
                var genericParamContext = BuildGenericParameterContext(null, methodRef);
                var resolvedMethodBase = ResolveMethod(methodRef, genericParamContext);

                if (resolvedMethodBase is ConstructorInfo constructorInfo)
                {
                    _il.Emit(opCode, constructorInfo);
                }
                else if (resolvedMethodBase is MethodInfo methodInfo)
                {
                    _il.Emit(opCode, methodInfo);
                }
                else
                {
                    throw new InvalidOperationException($"Could not resolve method: {methodRef.FullName}");
                }
                break;

            case Cci.OperandType.InlineType:
                var typeRef = (TypeReference)operand;
                var typeContext = BuildGenericParameterContext(typeRef, null);
                _il.Emit(opCode, ResolveType(typeRef, typeContext));
                break;

            case Cci.OperandType.InlineField:
                var fieldRef = (FieldReference)operand;
                var fieldContext = BuildGenericParameterContext(fieldRef.DeclaringType, null);
                _il.Emit(opCode, ResolveField(fieldRef, fieldContext));
                break;

            case Cci.OperandType.ShortInlineBrTarget:
            case Cci.OperandType.InlineBrTarget:
                var targetInstruction = (Cci.Instruction)operand;
                _il.Emit(opCode, _branchLabels[targetInstruction]);
                break;

            case Cci.OperandType.InlineSwitch:
                var targetInstructions = (Cci.Instruction[])operand;
                var labels = targetInstructions.Select(t => _branchLabels[t]).ToArray();
                _il.Emit(opCode, labels);
                break;

            case Cci.OperandType.InlineVar:
            case Cci.OperandType.ShortInlineVar:
                var variableDef = (VariableDefinition)operand;
                _il.Emit(opCode, variableDef.Index);
                break;

            case Cci.OperandType.InlineArg:
            case Cci.OperandType.ShortInlineArg:
                var paramDef = (ParameterDefinition)operand;
                _il.Emit(opCode, _sourceMethod.HasThis ? paramDef.Index + 1 : paramDef.Index);
                break;
            case Cci.OperandType.InlineTok:
                if (operand is TypeReference tokenTypeRef)
                {
                    var typeContextTok = BuildGenericParameterContext(tokenTypeRef, null);
                    _il.Emit(opCode, ResolveType(tokenTypeRef, typeContextTok));
                }
                else if (operand is MethodReference tokenMethodRef)
                {
                    var genericParamContextTok = BuildGenericParameterContext(null, tokenMethodRef);
                    var resolvedMethodBaseTok = ResolveMethod(tokenMethodRef, genericParamContextTok);

                    if (resolvedMethodBaseTok is ConstructorInfo constructorInfoTok)
                    {
                        _il.Emit(opCode, constructorInfoTok);
                    }
                    else if (resolvedMethodBaseTok is MethodInfo methodInfo)
                    {
                        _il.Emit(opCode, methodInfo);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Could not resolve method token: {tokenMethodRef.FullName}");
                    }
                }
                else if (operand is FieldReference tokenFieldRef)
                {
                    var fieldContextTok = BuildGenericParameterContext(tokenFieldRef.DeclaringType, null);
                    _il.Emit(opCode, ResolveField(tokenFieldRef, fieldContextTok));
                }
                else
                {
                    throw new NotSupportedException($"Unsupported operand type for InlineTok: {operand?.GetType().FullName}");
                }
                break;

            default:
                throw new NotSupportedException($"Operand type not supported: {instruction.OpCode.OperandType}");
        }
    }

    private Type? ResolveType(TypeReference typeRef, IGenericParameterContext? context = null)
    {
        if (typeRef is GenericParameter genericParam)
        {
            if (genericParam.Owner is MethodReference)
            {
                if (genericParam.Owner == _sourceMethod && _methodGenericArgs != null && genericParam.Position < _methodGenericArgs.Length)
                {
                    return _methodGenericArgs[genericParam.Position];
                }
                else
                {
                    return ResolveType(context.MethodGenericParameters[genericParam.Position], context);
                }
            }
            else if (genericParam.Owner is TypeReference)
            {
                if (genericParam.Owner == _sourceMethod.DeclaringType && _typeGenericArgs != null && genericParam.Position < _typeGenericArgs.Length)
                {
                    return _typeGenericArgs[genericParam.Position];
                }
                else
                {
                    return ResolveType(context.TypeGenericParameters[genericParam.Position], context);
                }
            }
            throw new InvalidOperationException($"Unbound generic parameter: {genericParam.Name}");
        }
        if (typeRef is GenericInstanceType genericInstance)
        {
            var elementType = ResolveType(genericInstance.ElementType, context);
            var allGenericArgs = new List<Type>();

            if (genericInstance.IsNested && genericInstance.IsCompilerGenerated())
            {
                var declaringTypeArgs = genericInstance.DeclaringType.GenericParameters.Select(p => ResolveType(p, context));
                allGenericArgs.AddRange(declaringTypeArgs);
            }

            var currentGenericArgs = genericInstance.GenericArguments.Select(g => ResolveType(g, context));
            allGenericArgs.AddRange(currentGenericArgs);

            if(allGenericArgs.Count == 0 && typeRef.IsAsyncStateMachineType())
            {
                var genericStateMachineArgs = context.TypeGenericParameters.Select(t => ResolveType(t, context));
                allGenericArgs.AddRange(genericStateMachineArgs);
            }

            if (elementType == null || allGenericArgs.Any(a => a == null)) return null;

            return elementType.MakeGenericType(allGenericArgs.ToArray()!);
        }
        if (typeRef.IsArray)
        {
            var arrayType = (ArrayType)typeRef;
            var elementType = ResolveType(typeRef.GetElementType(), context);
            if(elementType == null) return null;
            if(arrayType.Rank == 1)
            {
                return elementType.MakeArrayType();
            }
            return elementType?.MakeArrayType((typeRef as ArrayType)!.Rank);
        }
        if (typeRef.IsByReference)
        {
            return ResolveType(typeRef.GetElementType(), context)?.MakeByRefType();
        }
        string typeFullName = typeRef.FullName.Replace('/', '+');
        try
        {
            string assemblyQualifiedName = $"{typeFullName}, {typeRef.Scope}";
            return Type.GetType(assemblyQualifiedName, true);
        }
        catch
        {
            return Type.GetType(typeFullName, false);
        }
    }

    private MethodBase? ResolveMethod(MethodReference methodRef, IGenericParameterContext? context = null)
    {
        if (methodRef is GenericInstanceMethod genericInstanceMethod)
        {
            var elementMethod = ResolveMethod(genericInstanceMethod.ElementMethod, context) as MethodInfo;
            if (elementMethod == null) throw new NullReferenceException("Cannot find generic method definition.");

            var genericArguments = genericInstanceMethod.GenericArguments
                .Select(arg => ResolveType(arg, context))
                .ToArray();
            if (genericArguments.Any(a => a == null)) throw new InvalidOperationException("Failed to resolve generic arguments for method instance.");
            return elementMethod.MakeGenericMethod(genericArguments);
        }

        var declaringType = ResolveType(methodRef.DeclaringType, context);
        if (declaringType == null) throw new NullReferenceException($"Could not resolve type: {methodRef.DeclaringType.FullName}");

        Type?[] paramTypes = [];

        paramTypes = methodRef.Parameters.Select(p => ResolveType(p.ParameterType, context)).ToArray();

        if (paramTypes.Any(p => p == null)) throw new InvalidOperationException("Failed to resolve parameter types for method.");

        var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        if (methodRef.Name == ".ctor")
        {
            return declaringType.GetConstructor(bindingFlags, null, paramTypes!, null);
        }

        if (methodRef.HasGenericParameters && !methodRef.IsGenericInstance)
        {
            return declaringType.GetMethods(bindingFlags)
                .FirstOrDefault(m => m.Name == methodRef.Name &&
                                      m.IsGenericMethodDefinition &&
                                      m.GetGenericArguments().Length == methodRef.GenericParameters.Count &&
                                      m.GetParameters().Length == methodRef.Parameters.Count);
        }
        var s = declaringType.GetMethods(bindingFlags);
        return declaringType.GetMethod(methodRef.Name, bindingFlags, null, paramTypes!, null);
    }

    private FieldInfo? ResolveField(FieldReference fieldRef, IGenericParameterContext context)
    {
        var declaringType = ResolveType(fieldRef.DeclaringType, context);
        return declaringType.GetField(fieldRef.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    }

    private IGenericParameterContext? BuildGenericParameterContext(TypeReference? type, MethodReference? method)
    {
        IGenericParameterContext? context = new GenericParameterContext();

        context.MethodGenericParameters = method is GenericInstanceMethod genericMethod ? genericMethod.GenericArguments : null;

        if (method?.DeclaringType is GenericInstanceType genericType)
        {
            context.TypeGenericParameters = genericType.GenericArguments;
        }
        if (type != null)
        {
            if (type.IsNested && type.IsCompilerGenerated())
            {
                Collection<TypeReference>? typeReferences = [];
                if (_sourceMethod.DeclaringType.HasGenericParameters)
                {
                    foreach (var param in _sourceMethod.DeclaringType.GenericParameters)
                    {
                        typeReferences.Add(param);
                    }
                }
                context.TypeGenericParameters = typeReferences;
            }
        }

        return context;
    }
}