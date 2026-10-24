using System.Collections.Generic;
using System.IO;

namespace Il2CppDumper
{
    public class CustomAttributeDataReader : BinaryReader
    {
        private readonly Il2CppExecutor executor;
        private readonly Metadata metadata;
        private long ctorBuffer;
        private long dataBuffer;

        public uint Count { get; set; }

        public CustomAttributeDataReader(Il2CppExecutor executor, byte[] buff) : base(new MemoryStream(buff))
        {
            this.executor = executor;
            metadata = executor.metadata;
            Count = this.ReadCompressedUInt32();
            ctorBuffer = BaseStream.Position;
            dataBuffer = BaseStream.Position + Count * 4;
        }

        public string GetStringCustomAttributeData()
        {
            BaseStream.Position = ctorBuffer;
            var ctorIndex = ReadCtorIndex();
            var methodDef = metadata.methodDefs[ctorIndex];
            var typeDef = metadata.typeDefs[methodDef.declaringType];
            ctorBuffer = BaseStream.Position;

            BaseStream.Position = dataBuffer;
            var argumentCount = this.ReadCompressedUInt32();
            var fieldCount = this.ReadCompressedUInt32();
            var propertyCount = this.ReadCompressedUInt32();

            var argList = new List<string>();

            for (var i = 0; i < argumentCount; i++)
            {
                argList.Add($"{AttributeDataToString(ReadAttributeDataValue())}");
            }
            for (var i = 0; i < fieldCount; i++)
            {
                var str = AttributeDataToString(ReadAttributeDataValue());
                (var declaring, var fieldIndex) = ReadCustomAttributeNamedArgumentClassAndIndex(typeDef);
                var fieldDef = metadata.fieldDefs[declaring.fieldStart + fieldIndex];
                argList.Add($"{metadata.GetStringFromIndex(fieldDef.nameIndex)} = {str}");
            }
            for (var i = 0; i < propertyCount; i++)
            {
                var str = AttributeDataToString(ReadAttributeDataValue());
                (var declaring, var propertyIndex) = ReadCustomAttributeNamedArgumentClassAndIndex(typeDef);
                var propertyDef = metadata.propertyDefs[declaring.propertyStart + propertyIndex];
                argList.Add($"{metadata.GetStringFromIndex(propertyDef.nameIndex)} = {str}");
            }
            dataBuffer = BaseStream.Position;


            var typeName = metadata.GetStringFromIndex(typeDef.nameIndex).Replace("Attribute", "");
            if (argList.Count > 0)
            {
                return $"[{typeName}({string.Join(", ", argList)})]";
            }
            else
            {
                return $"[{typeName}]";
            }
        }

        /// <summary>
        /// Reads one constructor entry. Up to metadata v39 this is a plain method
        /// index; from v104 (Unity 6000.5) it is an encoded metadata-usage token
        /// (usage kind in the top 3 bits, method index shifted left with the low
        /// bit set), so decode it the same way as other metadata usages.
        /// </summary>
        private int ReadCtorIndex()
        {
            var raw = ReadUInt32();
            if (Metadata.GetEncodedIndexType(raw) != 0)
            {
                return (int)metadata.GetDecodedMethodIndex(raw);
            }
            return (int)raw;
        }

        private string AttributeDataToString(BlobValue blobValue)
        {
            //TODO enum
            if (blobValue.Value == null)
            {
                return "null";
            }
            switch (blobValue.il2CppTypeEnum)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return $"\"{blobValue.Value}\"";
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    var array = (BlobValue[])blobValue.Value;
                    var list = new List<string>();
                    foreach (var item in array)
                    {
                        list.Add(AttributeDataToString(item));
                    }
                    return $"new[] {{ {string.Join(", ", list)} }}";
                case Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX:
                    var il2CppType = (Il2CppType)blobValue.Value;
                    return $"typeof({executor.GetTypeName(il2CppType, false, false)})";
                default:
                    return blobValue.Value.ToString();
            }
        }

        public CustomAttributeReaderVisitor VisitCustomAttributeData()
        {
            var visitor = new CustomAttributeReaderVisitor();

            BaseStream.Position = ctorBuffer;
            var ctorIndex = ReadCtorIndex();
            visitor.CtorIndex = ctorIndex;
            var methodDef = metadata.methodDefs[ctorIndex];
            var typeDef = metadata.typeDefs[methodDef.declaringType];
            ctorBuffer = BaseStream.Position;

            BaseStream.Position = dataBuffer;
            var argumentCount = this.ReadCompressedUInt32();
            var fieldCount = this.ReadCompressedUInt32();
            var propertyCount = this.ReadCompressedUInt32();

            visitor.Arguments = new AttributeArgument[argumentCount];
            for (var i = 0; i < argumentCount; i++)
            {
                var argument = visitor.Arguments[i] = new AttributeArgument();
                argument.Value = ReadAttributeDataValue();
                argument.Index = i;
            }
            visitor.Fields = new AttributeArgument[fieldCount];
            for (var i = 0; i < fieldCount; i++)
            {
                var field = visitor.Fields[i] = new AttributeArgument();
                field.Value = ReadAttributeDataValue();
                (var declaring, var fieldIndex) = ReadCustomAttributeNamedArgumentClassAndIndex(typeDef);
                field.Index = declaring.fieldStart + fieldIndex;
            }
            visitor.Properties = new AttributeArgument[propertyCount];
            for (var i = 0; i < propertyCount; i++)
            {
                var property = visitor.Properties[i] = new AttributeArgument();
                property.Value = ReadAttributeDataValue();
                (var declaring, var propertyIndex) = ReadCustomAttributeNamedArgumentClassAndIndex(typeDef);
                property.Index = declaring.propertyStart + propertyIndex;
            }

            dataBuffer = BaseStream.Position;
            return visitor;
        }

        private BlobValue ReadAttributeDataValue()
        {
            var type = executor.ReadEncodedTypeEnum(this, out var enumType);
            executor.GetConstantValueFromBlob(type, this, out var blobValue);
            if (enumType != null)
            {
                blobValue.EnumType = enumType;
            }
            return blobValue;
        }

        private (Il2CppTypeDefinition, int) ReadCustomAttributeNamedArgumentClassAndIndex(Il2CppTypeDefinition typeDef)
        {
            var memberIndex = this.ReadCompressedInt32();
            if (memberIndex >= 0)
            {
                return (typeDef, memberIndex);
            }
            memberIndex = -(memberIndex + 1);

            var typeIndex = this.ReadCompressedUInt32();
            // Up to metadata v39 the declaring class is a TypeDefinitionIndex; from
            // v104 (Unity 6000.5) it is a TypeIndex into the Il2CppType table.
            var declaringClass = metadata.Version >= 104
                ? executor.GetTypeDefinitionFromIl2CppType(executor.il2Cpp.types[typeIndex])
                : metadata.typeDefs[typeIndex];

            return (declaringClass, memberIndex);
        }
    }
}
