using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppDumper
{
    public class DummyAssemblyGenerator
    {
        public List<AssemblyDefinition> Assemblies = new();

        private readonly Il2CppExecutor executor;
        private readonly Metadata metadata;
        private readonly Il2Cpp il2Cpp;
        private readonly Dictionary<Il2CppTypeDefinition, TypeDefinition> typeDefinitionDic = new();
        private readonly Dictionary<Il2CppTypeDefinition, ModuleDefinition> typeModuleDic = new();
        private readonly Dictionary<ModuleDefinition, Dictionary<MemberReference, MemberReference>> importCache = new();
        private readonly Dictionary<Il2CppGenericParameter, GenericParameter> genericParameterDic = new();
        private readonly MethodDefinition attributeAttribute;
        private readonly TypeReference stringType;
        private readonly TypeReference systemEnumType;
        private readonly TypeReference systemTypeType;
        private readonly TypeSystem typeSystem;
        private readonly Dictionary<int, FieldDefinition> fieldDefinitionDic = new();
        private readonly Dictionary<int, PropertyDefinition> propertyDefinitionDic = new();
        private readonly Dictionary<int, MethodDefinition> methodDefinitionDic = new();

        private int attributeRestoreErrorFirst = -1;
        private int attributeRestoreErrorLast = -1;
        public DummyAssemblyGenerator(Il2CppExecutor il2CppExecutor, bool addToken)
        {
            executor = il2CppExecutor;
            metadata = il2CppExecutor.metadata;
            il2Cpp = il2CppExecutor.il2Cpp;

            //Il2CppDummyDll
            var il2CppDummyDll = AssemblyDefinition.ReadAssembly(new MemoryStream(Resource1.Il2CppDummyDll));
            Assemblies.Add(il2CppDummyDll);
            var dummyMD = il2CppDummyDll.MainModule;
            var addressAttribute = dummyMD.Types.First(x => x.Name == "AddressAttribute").Methods[0];
            var fieldOffsetAttribute = dummyMD.Types.First(x => x.Name == "FieldOffsetAttribute").Methods[0];
            attributeAttribute = dummyMD.Types.First(x => x.Name == "AttributeAttribute").Methods[0];
            var metadataOffsetAttribute = dummyMD.Types.First(x => x.Name == "MetadataOffsetAttribute").Methods[0];
            var tokenAttribute = dummyMD.Types.First(x => x.Name == "TokenAttribute").Methods[0];
            stringType = dummyMD.TypeSystem.String;
            typeSystem = dummyMD.TypeSystem;
            // System.Enum and System.Type are not part of Cecil's TypeSystem. Build
            // them against the same core library the primitives use, so the dummy
            // DLLs never reference the core library of the runtime running the dumper.
            systemEnumType = new TypeReference("System", "Enum", dummyMD, typeSystem.CoreLibrary);
            systemTypeType = new TypeReference("System", "Type", dummyMD, typeSystem.CoreLibrary);

            var resolver = new MyAssemblyResolver();
            var moduleParameters = new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver
            };
            resolver.Register(il2CppDummyDll);

            var parameterDefinitionDic = new Dictionary<int, ParameterDefinition>();
            var eventDefinitionDic = new Dictionary<int, EventDefinition>();

            //创建程序集，同时创建所有类
            foreach (var imageDef in metadata.imageDefs)
            {
                var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                if (string.IsNullOrWhiteSpace(imageName))
                {
                    imageName = $"Image_{Assemblies.Count}.dll";
                }
                var aname = imageDef.assemblyIndex >= 0 && imageDef.assemblyIndex < metadata.assemblyDefs.Length
                    ? metadata.assemblyDefs[imageDef.assemblyIndex].aname
                    : new Il2CppAssemblyNameDefinition();
                var metadataAssemblyName = metadata.GetStringFromIndex(aname.nameIndex);
                var imageAssemblyName = Path.GetFileNameWithoutExtension(imageName);
                var assemblyName = !string.IsNullOrWhiteSpace(imageAssemblyName) ? imageAssemblyName : metadataAssemblyName;
                if (string.IsNullOrWhiteSpace(assemblyName))
                {
                    assemblyName = $"Assembly_{Assemblies.Count}";
                }
                var useMetadataAssemblyInfo = string.Equals(metadataAssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase);
                Version vers;
                if (useMetadataAssemblyInfo && aname.major >= 0 && aname.minor >= 0 && aname.build >= 0 && aname.revision >= 0)
                {
                    vers = new Version(aname.major, aname.minor, aname.build, aname.revision);
                }
                else
                {
                    vers = new Version(0, 0, 0, 0);
                }
                var assemblyNameDef = new AssemblyNameDefinition(assemblyName, vers);
                /*assemblyNameDef.Culture = metadata.GetStringFromIndex(aname.cultureIndex);
                assemblyNameDef.PublicKey = Encoding.UTF8.GetBytes(metadata.GetStringFromIndex(aname.publicKeyIndex));
                assemblyNameDef.HashAlgorithm = (AssemblyHashAlgorithm)aname.hash_alg;
                assemblyNameDef.Attributes = (AssemblyAttributes)aname.flags;
                assemblyNameDef.PublicKeyToken = aname.public_key_token;*/
                var assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyNameDef, imageName, moduleParameters);
                resolver.Register(assemblyDefinition);
                Assemblies.Add(assemblyDefinition);
                var moduleDefinition = assemblyDefinition.MainModule;
                moduleDefinition.Types.Clear();//清除自动创建的<Module>类
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var namespaceName = metadata.GetStringFromIndex(typeDef.namespaceIndex);
                    var typeName = metadata.GetStringFromIndex(typeDef.nameIndex);
                    var typeDefinition = new TypeDefinition(namespaceName, typeName, (TypeAttributes)typeDef.flags);
                    typeDefinitionDic.Add(typeDef, typeDefinition);
                    typeModuleDic[typeDef] = moduleDefinition;
                    if (typeDef.declaringTypeIndex == -1)
                    {
                        moduleDefinition.Types.Add(typeDefinition);
                    }
                }
            }
            foreach (var imageDef in metadata.imageDefs)
            {
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[typeDef];

                    //nestedtype
                    for (int i = 0; i < typeDef.nested_type_count; i++)
                    {
                        var nestedTypeIndex = typeDef.nestedTypesStart + i;
                        if (nestedTypeIndex < 0 || nestedTypeIndex >= metadata.nestedTypeIndices.Length)
                        {
                            continue;
                        }
                        var nestedIndex = metadata.nestedTypeIndices[nestedTypeIndex];
                        if (nestedIndex < 0 || nestedIndex >= metadata.typeDefs.Length)
                        {
                            continue;
                        }
                        var nestedTypeDef = metadata.typeDefs[nestedIndex];
                        // A nested type always lives in the image of its declaring
                        // type; a mismatch means a bad index, and attaching it would
                        // make one type definition reachable from two modules.
                        if (!typeModuleDic.TryGetValue(nestedTypeDef, out var nestedModule) || nestedModule != typeDefinition.Module)
                        {
                            continue;
                        }
                        var nestedTypeDefinition = typeDefinitionDic[nestedTypeDef];
                        AttachNestedType(typeDefinition, nestedTypeDefinition);
                    }
                }
            }
            foreach (var pair in typeDefinitionDic)
            {
                if (pair.Value.Module == null && typeModuleDic.TryGetValue(pair.Key, out var moduleDefinition))
                {
                    moduleDefinition.Types.Add(pair.Value);
                }
            }
            //提前处理
            foreach (var imageDef in metadata.imageDefs)
            {
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[typeDef];

                    if (addToken)
                    {
                        var customTokenAttribute = new CustomAttribute(Import(typeDefinition.Module, tokenAttribute));
                        customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{typeDef.token:X}")));
                        typeDefinition.CustomAttributes.Add(customTokenAttribute);
                    }

                    //genericParameter
                    if (typeDef.genericContainerIndex >= 0)
                    {
                        var genericContainer = metadata.genericContainers[typeDef.genericContainerIndex];
                        for (int i = 0; i < genericContainer.type_argc; i++)
                        {
                            var genericParameterIndex = genericContainer.genericParameterStart + i;
                            var param = metadata.genericParameters[genericParameterIndex];
                            var genericParameter = CreateGenericParameter(param, typeDefinition);
                            typeDefinition.GenericParameters.Add(genericParameter);
                        }
                    }

                    //parent
                    if (typeDef.parentIndex >= 0 && !typeDef.IsEnum)
                    {
                        var parentType = il2Cpp.types[typeDef.parentIndex];
                        var parentTypeRef = GetTypeReference(typeDefinition, parentType);
                        typeDefinition.BaseType = parentTypeRef;
                    }

                    // Cecil identifies an enum by System.Enum as its BaseType.
                    // Do this AFTER the normal parent assignment so an IL2CPP
                    // underlying primitive such as int cannot overwrite it.
                    if (typeDef.IsEnum)
                    {
                        typeDefinition.BaseType =
                            Import(typeDefinition.Module, systemEnumType);
                    }

                    //interfaces
                    for (int i = 0; i < typeDef.interfaces_count; i++)
                    {
                        var interfaceType = il2Cpp.types[metadata.interfaceIndices[typeDef.interfacesStart + i]];
                        var interfaceTypeRef = GetTypeReference(typeDefinition, interfaceType);
                        typeDefinition.Interfaces.Add(new InterfaceImplementation(interfaceTypeRef));
                    }
                }
            }
            //处理field, method, property等等
            foreach (var imageDef in metadata.imageDefs)
            {
                var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (int index = imageDef.typeStart; index < typeEnd; index++)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[typeDef];

                    //field
                    var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                    for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                    {
                        var fieldDef = metadata.fieldDefs[i];
                        var fieldType = il2Cpp.types[fieldDef.typeIndex];
                        var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);

                        TypeReference fieldTypeRef;
                        FieldAttributes fieldAttributes;

                        if (typeDef.IsEnum)
                        {
                            if (fieldName == "value__")
                            {
                                // value__ must use the primitive underlying type.
                                fieldTypeRef = GetTypeReference(typeDefinition, fieldType);
                                fieldAttributes =
                                    FieldAttributes.Private |
                                    FieldAttributes.SpecialName |
                                    FieldAttributes.RTSpecialName;
                            }
                            else
                            {
                                // IMPORTANT:
                                // Cecil enum literal fields are stored with the
                                // UNDERLYING primitive field type (int, byte, etc.),
                                // not the enum TypeDefinition itself.
                                fieldTypeRef = GetEnumUnderlyingType(typeDefinition, typeDef);

                                fieldAttributes =
                                    FieldAttributes.Public |
                                    FieldAttributes.Static |
                                    FieldAttributes.Literal |
                                    FieldAttributes.HasDefault;
                            }
                        }
                        else
                        {
                            fieldTypeRef = GetTypeReference(typeDefinition, fieldType);
                            fieldAttributes = (FieldAttributes)fieldType.attrs;
                        }

                        var fieldDefinition =
                            new FieldDefinition(fieldName, fieldAttributes, fieldTypeRef);

                        typeDefinition.Fields.Add(fieldDefinition);
                        fieldDefinitionDic.Add(i, fieldDefinition);

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(Import(typeDefinition.Module, tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{fieldDef.token:X}")));
                            fieldDefinition.CustomAttributes.Add(customTokenAttribute);
                        }

                        //fieldDefault
                        if (metadata.GetFieldDefaultValueFromIndex(i, out var fieldDefault) && fieldDefault.dataIndex != -1)
                        {
                            if (executor.TryGetDefaultValue(fieldDefault.typeIndex, fieldDefault.dataIndex, out var value))
                            {
                                if (typeDef.IsEnum && fieldName != "value__")
                                {
                                    // The enum literal FieldType is the underlying
                                    // primitive, so Cecil can serialize the constant.
                                    fieldDefinition.Constant = value;
                                }
                                else if (!TrySetConstant(fieldDefinition, fieldDefinition.FieldType, value))
                                {
                                    AddMetadataOffsetAttribute(
                                        typeDefinition.Module,
                                        metadataOffsetAttribute,
                                        fieldDefinition.CustomAttributes,
                                        metadata.GetDefaultValueFromIndex(fieldDefault.dataIndex));
                                }
                            }
                            else
                            {
                                AddMetadataOffsetAttribute(
                                    typeDefinition.Module,
                                    metadataOffsetAttribute,
                                    fieldDefinition.CustomAttributes,
                                    metadata.GetDefaultValueFromIndex(fieldDefault.dataIndex));
                            }
                        }
                        //fieldOffset
                        if (!fieldDefinition.IsLiteral)
                        {
                            var fieldOffset = il2Cpp.GetFieldOffsetFromIndex(index, i - typeDef.fieldStart, i, typeDefinition.IsValueType, fieldDefinition.IsStatic);
                            if (fieldOffset >= 0)
                            {
                                var customAttribute = new CustomAttribute(Import(typeDefinition.Module, fieldOffsetAttribute));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{fieldOffset:X}"));
                                customAttribute.Fields.Add(offset);
                                fieldDefinition.CustomAttributes.Add(customAttribute);
                            }
                        }
                    }
                    //method
                    for (var methodIndexInType = 0; methodIndexInType < typeDef.method_count; methodIndexInType++)
                    {
                        var i = metadata.GetMethodIndexFromTypeDefinition(index, methodIndexInType);
                        var methodDef = metadata.methodDefs[i];
                        var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
                        var methodDefinition = new MethodDefinition(methodName, (MethodAttributes)methodDef.flags, Import(typeDefinition.Module, typeSystem.Void))
                        {
                            ImplAttributes = (MethodImplAttributes)methodDef.iflags
                        };
                        typeDefinition.Methods.Add(methodDefinition);
                        //genericParameter
                        if (methodDef.genericContainerIndex >= 0)
                        {
                            var genericContainer = metadata.genericContainers[methodDef.genericContainerIndex];
                            for (int j = 0; j < genericContainer.type_argc; j++)
                            {
                                var genericParameterIndex = genericContainer.genericParameterStart + j;
                                var param = metadata.genericParameters[genericParameterIndex];
                                var genericParameter = CreateGenericParameter(param, methodDefinition);
                                methodDefinition.GenericParameters.Add(genericParameter);
                            }
                        }
                        var methodReturnType = il2Cpp.types[methodDef.returnType];
                        var returnType = GetTypeReferenceWithByRef(methodDefinition, methodReturnType);
                        methodDefinition.ReturnType = returnType;

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(Import(typeDefinition.Module, tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{methodDef.token:X}")));
                            methodDefinition.CustomAttributes.Add(customTokenAttribute);
                        }

                        if (methodDefinition.HasBody && typeDefinition.BaseType?.FullName != "System.MulticastDelegate")
                        {
                            var ilprocessor = methodDefinition.Body.GetILProcessor();
                            if (returnType.FullName == "System.Void")
                            {
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                            }
                            else if (returnType.IsValueType)
                            {
                                var variable = new VariableDefinition(returnType);
                                methodDefinition.Body.Variables.Add(variable);
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloca_S, variable));
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Initobj, returnType));
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                            }
                            else
                            {
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ldnull));
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                            }
                        }
                        methodDefinitionDic.Add(i, methodDefinition);
                        //method parameter
                        for (var j = 0; j < methodDef.parameterCount; ++j)
                        {
                            var parameterDef = metadata.parameterDefs[methodDef.parameterStart + j];
                            var parameterName = metadata.GetStringFromIndex(parameterDef.nameIndex);
                            var parameterType = il2Cpp.types[parameterDef.typeIndex];
                            var parameterTypeRef = GetTypeReferenceWithByRef(methodDefinition, parameterType);
                            var parameterDefinition = new ParameterDefinition(parameterName, (ParameterAttributes)parameterType.attrs, parameterTypeRef);
                            methodDefinition.Parameters.Add(parameterDefinition);
                            parameterDefinitionDic.Add(methodDef.parameterStart + j, parameterDefinition);
                            //ParameterDefault
                            if (metadata.GetParameterDefaultValueFromIndex(methodDef.parameterStart + j, out var parameterDefault) && parameterDefault.dataIndex != -1)
                            {
                                if (executor.TryGetDefaultValue(parameterDefault.typeIndex, parameterDefault.dataIndex, out var value))
                                {
                                    if (!TrySetConstant(parameterDefinition, parameterDefinition.ParameterType, value))
                                    {
                                        AddMetadataOffsetAttribute(typeDefinition.Module, metadataOffsetAttribute, parameterDefinition.CustomAttributes, metadata.GetDefaultValueFromIndex(parameterDefault.dataIndex));
                                    }
                                }
                                else
                                {
                                    AddMetadataOffsetAttribute(typeDefinition.Module, metadataOffsetAttribute, parameterDefinition.CustomAttributes, (uint)value);
                                }
                            }
                        }
                        //methodAddress
                        if (!methodDefinition.IsAbstract)
                        {
                            var methodPointer = il2Cpp.GetMethodPointer(imageName, methodDef);
                            if (methodPointer > 0)
                            {
                                var customAttribute = new CustomAttribute(Import(typeDefinition.Module, addressAttribute));
                                var fixedMethodPointer = il2Cpp.GetRVA(methodPointer);
                                var rva = new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{fixedMethodPointer:X}"));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{il2Cpp.MapVATR(methodPointer):X}"));
                                var va = new CustomAttributeNamedArgument("VA", new CustomAttributeArgument(stringType, $"0x{methodPointer:X}"));
                                customAttribute.Fields.Add(rva);
                                customAttribute.Fields.Add(offset);
                                customAttribute.Fields.Add(va);
                                if (methodDef.slot != ushort.MaxValue)
                                {
                                    var slot = new CustomAttributeNamedArgument("Slot", new CustomAttributeArgument(stringType, methodDef.slot.ToString()));
                                    customAttribute.Fields.Add(slot);
                                }
                                methodDefinition.CustomAttributes.Add(customAttribute);
                            }
                        }
                    }
                    //property
                    var propertyEnd = typeDef.propertyStart + typeDef.property_count;
                    for (var i = typeDef.propertyStart; i < propertyEnd; ++i)
                    {
                        var propertyDef = metadata.propertyDefs[i];
                        var propertyName = metadata.GetStringFromIndex(propertyDef.nameIndex);
                        TypeReference propertyType = null;
                        MethodDefinition GetMethod = null;
                        MethodDefinition SetMethod = null;
                        if (propertyDef.get >= 0)
                        {
                            GetMethod = methodDefinitionDic[metadata.GetMethodIndexFromTypeDefinition(index, propertyDef.get)];
                            propertyType = GetMethod.ReturnType;
                        }
                        if (propertyDef.set >= 0)
                        {
                            SetMethod = methodDefinitionDic[metadata.GetMethodIndexFromTypeDefinition(index, propertyDef.set)];
                            propertyType ??= SetMethod.Parameters[0].ParameterType;
                        }
                        var propertyDefinition = new PropertyDefinition(propertyName, (PropertyAttributes)propertyDef.attrs, propertyType)
                        {
                            GetMethod = GetMethod,
                            SetMethod = SetMethod
                        };
                        typeDefinition.Properties.Add(propertyDefinition);
                        propertyDefinitionDic.Add(i, propertyDefinition);

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(Import(typeDefinition.Module, tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{propertyDef.token:X}")));
                            propertyDefinition.CustomAttributes.Add(customTokenAttribute);
                        }
                    }
                    //event
                    var eventEnd = typeDef.eventStart + typeDef.event_count;
                    for (var i = typeDef.eventStart; i < eventEnd; ++i)
                    {
                        var eventDef = metadata.eventDefs[i];
                        var eventName = metadata.GetStringFromIndex(eventDef.nameIndex);
                        var eventType = il2Cpp.types[eventDef.typeIndex];
                        var eventTypeRef = GetTypeReference(typeDefinition, eventType);
                        var eventDefinition = new EventDefinition(eventName, (EventAttributes)eventType.attrs, eventTypeRef);
                        if (eventDef.add >= 0)
                            eventDefinition.AddMethod = methodDefinitionDic[metadata.GetMethodIndexFromTypeDefinition(index, eventDef.add)];
                        if (eventDef.remove >= 0)
                            eventDefinition.RemoveMethod = methodDefinitionDic[metadata.GetMethodIndexFromTypeDefinition(index, eventDef.remove)];
                        if (eventDef.raise >= 0)
                            eventDefinition.InvokeMethod = methodDefinitionDic[metadata.GetMethodIndexFromTypeDefinition(index, eventDef.raise)];
                        typeDefinition.Events.Add(eventDefinition);
                        eventDefinitionDic.Add(i, eventDefinition);

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(Import(typeDefinition.Module, tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{eventDef.token:X}")));
                            eventDefinition.CustomAttributes.Add(customTokenAttribute);
                        }
                    }
                }
            }
            //第三遍，添加CustomAttribute
            if (il2Cpp.Version > 20)
            {
                foreach (var imageDef in metadata.imageDefs)
                {
                    var typeEnd = imageDef.typeStart + imageDef.typeCount;
                    for (int index = imageDef.typeStart; index < typeEnd; index++)
                    {
                        var typeDef = metadata.typeDefs[index];
                        var typeDefinition = typeDefinitionDic[typeDef];
                        //typeAttribute
                        CreateCustomAttribute(imageDef, typeDef.customAttributeIndex, typeDef.token, typeDefinition.Module, typeDefinition.CustomAttributes);

                        //field
                        var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                        for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                        {
                            var fieldDef = metadata.fieldDefs[i];
                            var fieldDefinition = fieldDefinitionDic[i];
                            //fieldAttribute
                            CreateCustomAttribute(imageDef, fieldDef.customAttributeIndex, fieldDef.token, typeDefinition.Module, fieldDefinition.CustomAttributes);
                        }

                        //method
                        for (var methodIndexInType = 0; methodIndexInType < typeDef.method_count; methodIndexInType++)
                        {
                            var i = metadata.GetMethodIndexFromTypeDefinition(index, methodIndexInType);
                            var methodDef = metadata.methodDefs[i];
                            var methodDefinition = methodDefinitionDic[i];
                            //methodAttribute
                            CreateCustomAttribute(imageDef, methodDef.customAttributeIndex, methodDef.token, typeDefinition.Module, methodDefinition.CustomAttributes);

                            //method parameter
                            for (var j = 0; j < methodDef.parameterCount; ++j)
                            {
                                var parameterDef = metadata.parameterDefs[methodDef.parameterStart + j];
                                var parameterDefinition = parameterDefinitionDic[methodDef.parameterStart + j];
                                //parameterAttribute
                                CreateCustomAttribute(imageDef, parameterDef.customAttributeIndex, parameterDef.token, typeDefinition.Module, parameterDefinition.CustomAttributes);
                            }
                        }

                        //property
                        var propertyEnd = typeDef.propertyStart + typeDef.property_count;
                        for (var i = typeDef.propertyStart; i < propertyEnd; ++i)
                        {
                            var propertyDef = metadata.propertyDefs[i];
                            var propertyDefinition = propertyDefinitionDic[i];
                            //propertyAttribute
                            CreateCustomAttribute(imageDef, propertyDef.customAttributeIndex, propertyDef.token, typeDefinition.Module, propertyDefinition.CustomAttributes);
                        }

                        //event
                        var eventEnd = typeDef.eventStart + typeDef.event_count;
                        for (var i = typeDef.eventStart; i < eventEnd; ++i)
                        {
                            var eventDef = metadata.eventDefs[i];
                            var eventDefinition = eventDefinitionDic[i];
                            //eventAttribute
                            CreateCustomAttribute(imageDef, eventDef.customAttributeIndex, eventDef.token, typeDefinition.Module, eventDefinition.CustomAttributes);
                        }
                    }
                }
            }
            PrintAttributeRestoreErrors();
        }

        private void PrintAttributeRestoreErrors()
        {
            if (attributeRestoreErrorFirst >= 0)
            {
                Logger.Log($"WARNING: Failed to restore custom attributes with attributeIndex {attributeRestoreErrorFirst} - {attributeRestoreErrorLast}");
            }
        }

        private static void AttachNestedType(TypeDefinition declaringType, TypeDefinition nestedType)
        {
            if (declaringType == null || nestedType == null || ReferenceEquals(declaringType, nestedType))
            {
                return;
            }
            if (nestedType.DeclaringType == declaringType || declaringType.NestedTypes.Contains(nestedType))
            {
                return;
            }
            if (nestedType.DeclaringType != null)
            {
                return;
            }
            nestedType.Module?.Types.Remove(nestedType);
            declaringType.NestedTypes.Add(nestedType);
        }

        private void AddMetadataOffsetAttribute(ModuleDefinition moduleDefinition, MethodReference metadataOffsetAttribute, Collection<CustomAttribute> customAttributes, uint metadataOffset)
        {
            var customAttribute = new CustomAttribute(Import(moduleDefinition, metadataOffsetAttribute));
            var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{metadataOffset:X}"));
            customAttribute.Fields.Add(offset);
            customAttributes.Add(customAttribute);
        }

        private static bool TrySetConstant(FieldDefinition fieldDefinition, TypeReference typeReference, object value)
        {
            if (!CanWriteConstant(typeReference))
            {
                return false;
            }
            fieldDefinition.Constant = value;
            return true;
        }

        private static bool TrySetConstant(ParameterDefinition parameterDefinition, TypeReference typeReference, object value)
        {
            if (!CanWriteConstant(typeReference))
            {
                return false;
            }
            parameterDefinition.Constant = value;
            return true;
        }

        private static bool CanWriteConstant(TypeReference typeReference)
        {
            if (typeReference == null)
            {
                return false;
            }
            if (IsPrimitiveConstantType(typeReference.FullName))
            {
                return true;
            }
            return false;
        }

        private static bool IsPrimitiveConstantType(string fullName)
        {
            return fullName is "System.Boolean"
                or "System.Char"
                or "System.SByte"
                or "System.Byte"
                or "System.Int16"
                or "System.UInt16"
                or "System.Int32"
                or "System.UInt32"
                or "System.Int64"
                or "System.UInt64"
                or "System.Single"
                or "System.Double"
                or "System.String";
        }

        private TypeReference GetEnumUnderlyingType(TypeDefinition enumTypeDefinition, Il2CppTypeDefinition typeDef)
        {
            var fieldEnd = typeDef.fieldStart + typeDef.field_count;

            for (var i = typeDef.fieldStart; i < fieldEnd; i++)
            {
                var fieldDef = metadata.fieldDefs[i];
                var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);

                if (fieldName == "value__")
                {
                    // The value__ field is the authoritative underlying enum type.
                    return GetTypeReference(
                        enumTypeDefinition,
                        il2Cpp.types[fieldDef.typeIndex]);
                }
            }

            // IL2CPP normally uses Int32 when no explicit value__ field can be found.
            return enumTypeDefinition.Module.TypeSystem.Int32;
        }

        // Cecil builds a fresh reference (and scans the module's assembly references)
        // on every ImportReference call. The same few definitions are imported for
        // hundreds of thousands of members, so keep one imported reference per module.
        private TypeReference Import(ModuleDefinition module, TypeReference type)
        {
            var cache = GetImportCache(module);
            if (!cache.TryGetValue(type, out var imported))
            {
                imported = module.ImportReference(type);
                cache.Add(type, imported);
            }
            return (TypeReference)imported;
        }

        private MethodReference Import(ModuleDefinition module, MethodReference method)
        {
            var cache = GetImportCache(module);
            if (!cache.TryGetValue(method, out var imported))
            {
                imported = module.ImportReference(method);
                cache.Add(method, imported);
            }
            return (MethodReference)imported;
        }

        private Dictionary<MemberReference, MemberReference> GetImportCache(ModuleDefinition module)
        {
            if (!importCache.TryGetValue(module, out var cache))
            {
                cache = new Dictionary<MemberReference, MemberReference>();
                importCache.Add(module, cache);
            }
            return cache;
        }

        private TypeReference GetTypeReferenceWithByRef(MemberReference memberReference, Il2CppType il2CppType)
        {
            var typeReference = GetTypeReference(memberReference, il2CppType);
            if (il2CppType.byref == 1)
            {
                return new ByReferenceType(typeReference);
            }
            else
            {
                return typeReference;
            }
        }

        private TypeReference GetTypeReference(MemberReference memberReference, Il2CppType il2CppType)
        {
            var moduleDefinition = memberReference.Module;
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return Import(moduleDefinition, typeSystem.Object);
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                    return Import(moduleDefinition, typeSystem.Void);
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return Import(moduleDefinition, typeSystem.Boolean);
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return Import(moduleDefinition, typeSystem.Char);
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return Import(moduleDefinition, typeSystem.SByte);
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return Import(moduleDefinition, typeSystem.Byte);
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return Import(moduleDefinition, typeSystem.Int16);
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return Import(moduleDefinition, typeSystem.UInt16);
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return Import(moduleDefinition, typeSystem.Int32);
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return Import(moduleDefinition, typeSystem.UInt32);
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                    return Import(moduleDefinition, typeSystem.IntPtr);
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                    return Import(moduleDefinition, typeSystem.UIntPtr);
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return Import(moduleDefinition, typeSystem.Int64);
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return Import(moduleDefinition, typeSystem.UInt64);
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return Import(moduleDefinition, typeSystem.Single);
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return Import(moduleDefinition, typeSystem.Double);
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return Import(moduleDefinition, typeSystem.String);
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    return Import(moduleDefinition, typeSystem.TypedReference);
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var typeDef = executor.GetTypeDefinitionFromIl2CppType(il2CppType);
                        // Protected/obfuscated binaries can yield type references that
                        // don't resolve to a known type definition; fall back to
                        // System.Object instead of throwing.
                        if (typeDef == null || !typeDefinitionDic.TryGetValue(typeDef, out var typeDefinition))
                        {
                            return Import(moduleDefinition, typeSystem.Object);
                        }
                        return Import(moduleDefinition, typeDefinition);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2Cpp.MapVATR<Il2CppArrayType>(il2CppType.data.array);
                        var oriType = il2Cpp.GetIl2CppType(arrayType.etype);
                        return new ArrayType(GetTypeReference(memberReference, oriType), arrayType.rank);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(il2CppType.data.generic_class);
                        var typeDef = executor.GetGenericClassTypeDefinition(genericClass);
                        if (typeDef == null || !typeDefinitionDic.TryGetValue(typeDef, out var typeDefinition))
                        {
                            return Import(moduleDefinition, typeSystem.Object);
                        }
                        var genericInstanceType = new GenericInstanceType(Import(moduleDefinition, typeDefinition));
                        var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(genericClass.context.class_inst);
                        var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
                        foreach (var pointer in pointers)
                        {
                            var oriType = il2Cpp.GetIl2CppType(pointer);
                            genericInstanceType.GenericArguments.Add(GetTypeReference(memberReference, oriType));
                        }
                        return genericInstanceType;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return new ArrayType(GetTypeReference(memberReference, oriType));
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (memberReference is MethodDefinition methodDefinition)
                        {
                            return CreateGenericParameter(executor.GetGenericParameteFromIl2CppType(il2CppType), methodDefinition.DeclaringType);
                        }
                        var typeDefinition = (TypeDefinition)memberReference;
                        return CreateGenericParameter(executor.GetGenericParameteFromIl2CppType(il2CppType), typeDefinition);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        var methodDefinition = (MethodDefinition)memberReference;
                        return CreateGenericParameter(executor.GetGenericParameteFromIl2CppType(il2CppType), methodDefinition);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return new PointerType(GetTypeReference(memberReference, oriType));
                    }
                default:
                    throw new NotSupportedException();
            }
        }

        private void CreateCustomAttribute(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token, ModuleDefinition moduleDefinition, Collection<CustomAttribute> customAttributes)
        {
            var attributeIndex = metadata.GetCustomAttributeIndex(imageDef, customAttributeIndex, token);
            if (attributeIndex >= 0)
            {
                try
                {
                    if (il2Cpp.Version < 29)
                    {
                        var attributeTypeRange = metadata.attributeTypeRanges[attributeIndex];
                        for (int i = 0; i < attributeTypeRange.count; i++)
                        {
                            var attributeTypeIndex = metadata.attributeTypes[attributeTypeRange.start + i];
                            var attributeType = il2Cpp.types[attributeTypeIndex];
                            var typeDef = executor.GetTypeDefinitionFromIl2CppType(attributeType);
                            var typeDefinition = typeDefinitionDic[typeDef];
                            if (!TryRestoreCustomAttribute(typeDefinition, moduleDefinition, customAttributes))
                            {
                                var methodPointer = executor.customAttributeGenerators[attributeIndex];
                                var fixedMethodPointer = il2Cpp.GetRVA(methodPointer);
                                var customAttribute = new CustomAttribute(Import(moduleDefinition, attributeAttribute));
                                var name = new CustomAttributeNamedArgument("Name", new CustomAttributeArgument(stringType, typeDefinition.Name));
                                var rva = new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{fixedMethodPointer:X}"));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{il2Cpp.MapVATR(methodPointer):X}"));
                                customAttribute.Fields.Add(name);
                                customAttribute.Fields.Add(rva);
                                customAttribute.Fields.Add(offset);
                                customAttributes.Add(customAttribute);
                            }
                        }
                    }
                    else
                    {
                        var startRange = metadata.attributeDataRanges[attributeIndex];
                        var endRange = metadata.attributeDataRanges[attributeIndex + 1];
                        metadata.Position = (ulong)(metadata.Version >= 38
                            ? metadata.header.attributeData.offset + startRange.startOffset
                            : metadata.header.attributeDataOffset + startRange.startOffset);
                        var buff = metadata.ReadBytes((int)(endRange.startOffset - startRange.startOffset));
                        var reader = new CustomAttributeDataReader(executor, buff);
                        if (reader.Count != 0)
                        {
                            for (var i = 0; i < reader.Count; i++)
                            {
                                var visitor = reader.VisitCustomAttributeData();
                                var methodDefinition = methodDefinitionDic[visitor.CtorIndex];
                                var customAttribute = new CustomAttribute(Import(moduleDefinition, methodDefinition));
                                foreach (var argument in visitor.Arguments)
                                {
                                    var parameterDefinition = methodDefinition.Parameters[argument.Index];
                                    var customAttributeArgument = CreateCustomAttributeArgument(parameterDefinition.ParameterType, argument.Value, methodDefinition);
                                    customAttribute.ConstructorArguments.Add(customAttributeArgument);
                                }
                                foreach (var field in visitor.Fields)
                                {
                                    var fieldDefinition = fieldDefinitionDic[field.Index];
                                    var customAttributeArgument = CreateCustomAttributeArgument(fieldDefinition.FieldType, field.Value, fieldDefinition);
                                    var customAttributeNamedArgument = new CustomAttributeNamedArgument(fieldDefinition.Name, customAttributeArgument);
                                    customAttribute.Fields.Add(customAttributeNamedArgument);
                                }
                                foreach (var property in visitor.Properties)
                                {
                                    var propertyDefinition = propertyDefinitionDic[property.Index];
                                    var customAttributeArgument = CreateCustomAttributeArgument(propertyDefinition.PropertyType, property.Value, propertyDefinition);
                                    var customAttributeNamedArgument = new CustomAttributeNamedArgument(propertyDefinition.Name, customAttributeArgument);
                                    customAttribute.Properties.Add(customAttributeNamedArgument);
                                }
                                // Validate now so a malformed argument (e.g. a non-enum
                                // type in an enum position, seen in obfuscated metadata)
                                // is skipped here by the catch below, instead of aborting
                                // the entire module when Cecil serializes it.
                                if (IsCustomAttributeSerializable(customAttribute))
                                {
                                    customAttributes.Add(customAttribute);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    if (attributeRestoreErrorFirst < 0 ||
                        attributeIndex < attributeRestoreErrorFirst)
                    {
                        attributeRestoreErrorFirst = attributeIndex;
                    }

                    if (attributeRestoreErrorLast < 0 ||
                        attributeIndex > attributeRestoreErrorLast)
                    {
                        attributeRestoreErrorLast = attributeIndex;
                    }
                }
            }
        }

        private bool TryRestoreCustomAttribute(TypeDefinition attributeType, ModuleDefinition moduleDefinition, Collection<CustomAttribute> customAttributes)
        {
            if (attributeType.Methods.Count == 1 && attributeType.Name != "CompilerGeneratedAttribute")
            {
                var methodDefinition = attributeType.Methods[0];
                if (methodDefinition.Name == ".ctor" && methodDefinition.Parameters.Count == 0)
                {
                    var customAttribute = new CustomAttribute(Import(moduleDefinition, methodDefinition));
                    customAttributes.Add(customAttribute);
                    return true;
                }
            }
            return false;
        }

        private GenericParameter CreateGenericParameter(Il2CppGenericParameter param, IGenericParameterProvider iGenericParameterProvider)
        {
            if (!genericParameterDic.TryGetValue(param, out var genericParameter))
            {
                var genericName = metadata.GetStringFromIndex(param.nameIndex);
                genericParameter = new GenericParameter(genericName, iGenericParameterProvider)
                {
                    Attributes = (GenericParameterAttributes)param.flags
                };
                genericParameterDic.Add(param, genericParameter);
                for (int i = 0; i < param.constraintsCount; ++i)
                {
                    var constraintTypeIndex = metadata.constraintIndices[param.constraintsStart + i];
                    if (constraintTypeIndex < 0 || constraintTypeIndex >= il2Cpp.types.Length)
                    {
                        continue;
                    }
                    var il2CppType = il2Cpp.types[constraintTypeIndex];
                    genericParameter.Constraints.Add(new GenericParameterConstraint(GetTypeReference((MemberReference)iGenericParameterProvider, il2CppType)));
                }
            }
            return genericParameter;
        }

        private CustomAttributeArgument CreateCustomAttributeArgument(TypeReference typeReference, BlobValue blobValue, MemberReference memberReference)
        {
            var val = blobValue.Value;
            if (typeReference.FullName == "System.Object")
            {
                if (blobValue.il2CppTypeEnum == Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX)
                {
                    val = new CustomAttributeArgument(Import(memberReference.Module, systemTypeType), GetTypeReference(memberReference, (Il2CppType)val));
                }
                else
                {
                    val = new CustomAttributeArgument(GetBlobValueTypeReference(blobValue, memberReference), val);
                }
            }
            else if (val == null)
            {
                return new CustomAttributeArgument(typeReference, val);
            }
            else if (typeReference is ArrayType arrayType)
            {
                var arrayVal = (BlobValue[])val;
                var array = new CustomAttributeArgument[arrayVal.Length];
                var elementType = arrayType.ElementType;
                for (int i = 0; i < arrayVal.Length; i++)
                {
                    array[i] = CreateCustomAttributeArgument(elementType, arrayVal[i], memberReference);
                }
                val = array;
            }
            else if (typeReference.FullName == "System.Type")
            {
                val = GetTypeReference(memberReference, (Il2CppType)val);
            }
            return new CustomAttributeArgument(typeReference, val);
        }

        // Mono.Cecil serializes any custom-attribute argument whose type is not a
        // primitive/string/Type/Object as an enum, and throws if that type does not
        // resolve to an actual enum. Obfuscated/protected metadata can produce such
        // mismatches; detect them up front so the offending attribute is dropped
        // rather than failing the whole assembly write.
        private static readonly HashSet<string> primitiveAttributeArgTypes = new(StringComparer.Ordinal)
        {
            "System.Boolean", "System.Char", "System.SByte", "System.Byte",
            "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
            "System.Int64", "System.UInt64", "System.Single", "System.Double",
            "System.String", "System.Object", "System.Type",
        };

        private static bool IsCustomAttributeSerializable(CustomAttribute customAttribute)
        {
            foreach (var arg in customAttribute.ConstructorArguments)
            {
                if (!IsAttributeArgumentSerializable(arg)) return false;
            }
            foreach (var arg in customAttribute.Fields)
            {
                if (!IsAttributeArgumentSerializable(arg.Argument)) return false;
            }
            foreach (var arg in customAttribute.Properties)
            {
                if (!IsAttributeArgumentSerializable(arg.Argument)) return false;
            }
            return true;
        }

        private static bool IsAttributeArgumentSerializable(CustomAttributeArgument argument)
        {
            var type = argument.Type;
            if (type == null) return false;
            if (type is ArrayType arrayType)
            {
                if (argument.Value is CustomAttributeArgument[] elements)
                {
                    foreach (var element in elements)
                    {
                        if (!IsAttributeArgumentSerializable(element)) return false;
                    }
                }
                return true;
            }
            if (argument.Value is CustomAttributeArgument boxed)
            {
                // boxed object argument: validate the inner (real) argument
                return IsAttributeArgumentSerializable(boxed);
            }
            if (primitiveAttributeArgTypes.Contains(type.FullName)) return true;
            // Anything else is written by Cecil as an enum; require it to resolve to one.
            try
            {
                var resolved = type.Resolve();
                if (resolved == null) return true; // unknown -> preserve prior behaviour
                return resolved.IsEnum;
            }
            catch
            {
                return true;
            }
        }

        private TypeReference GetBlobValueTypeReference(BlobValue blobValue, MemberReference memberReference)
        {
            if (blobValue.EnumType != null)
            {
                return GetTypeReference(memberReference, blobValue.EnumType);
            }
            var il2CppType = new Il2CppType
            {
                type = blobValue.il2CppTypeEnum
            };
            return GetTypeReference(memberReference, il2CppType);
        }
    }
}
