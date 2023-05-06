namespace UnrealLib.UObject_Types
{
    public class UDefaultProperty
    {
        public UNameIndex NameIndex;
        public UNameIndex TypeIndex;
        public int ValueSize;
        public int ValueArrayIndex;

        public int ValueArraySize;

        // Possible value types. Only one will be in use
        public bool BoolValue;
        public int IntValue;
        public float FloatValue;
        public string StringValue;
        public UNameIndex EnumValue;
        public UNameIndex NameValue;
        public UObjectReference ObjectValue;

        // In-memory
        public string NameStr;
        public string TypeStr;

        private UDefaultProperty(ref UPK UPK)
        {
            NameIndex = UPK.ur.ReadNameIndex();
            if (NameIndex.NameTableIndex == UPK.NoneIndex) return;

            TypeIndex = UPK.ur.ReadNameIndex();
            ValueSize = UPK.ur.ReadInt32();
            ValueArrayIndex = UPK.ur.ReadInt32();

            NameStr = UPK.GetName(NameIndex);
            TypeStr = UPK.GetName(TypeIndex);

            if (TypeStr == "BoolProperty")
            {
                BoolValue = UPK.ur.ReadByte() == 1 ? true : false;
            }
            else if (TypeStr == "IntProperty")
            {
                IntValue = UPK.ur.ReadInt32();
            }
            else if (TypeStr == "FloatProperty")
            {
                FloatValue = UPK.ur.ReadFloat();
            }
            else if (TypeStr == "StringValue")
            {
                StringValue = new string(UPK.ur.ReadChars(ValueSize), 0, ValueSize - 1);
            }
            else if (TypeStr == "NameProperty")
            {
                NameValue = UPK.ur.ReadNameIndex();
            }
            else if (TypeStr == "ObjectProperty")
            {
                ObjectValue = UPK.ur.ReadInt32();
            }
            else if (TypeStr == "ByteProperty")
            {
                EnumValue = UPK.ur.ReadNameIndex();
                NameValue = UPK.ur.ReadNameIndex();
            }
            else if (TypeStr == "ArrayValue")
            {
                // ValueArraySize = UPK.ur.ReadInt32();
                UPK.ur.BaseStream.Position += ValueSize + 4;
            }
            else if (TypeStr == "StructProperty")
            {
                UPK.ur.BaseStream.Position += ValueSize;
            }
            else
            {
                UPK.ur.BaseStream.Position += ValueSize;
            }
        }

        public static List<UDefaultProperty> ReadProperties(ref UPK UPK)
        {
            List<UDefaultProperty> props = new();
            while (true)
            {
                UDefaultProperty currentProperty = new(ref UPK);
                if (currentProperty.NameIndex.NameTableIndex == UPK.NoneIndex) break;
                props.Add(currentProperty);
            }
            return props;
        }

        /// <summary>
        /// Serializes the DefaultProperty and returns a byte array
        /// </summary>
        /// <returns></returns>
        public byte[] Serialize()
        {
            using (var _uw = new MemoryStream())
            {
                _uw.Write(UnrealConverter.GetBytes(NameIndex));
                _uw.Write(UnrealConverter.GetBytes(TypeIndex));
                _uw.Write(UnrealConverter.GetBytes(ValueSize));
                _uw.Write(UnrealConverter.GetBytes(ValueArrayIndex));

                if (TypeStr == "BoolProperty")
                {
                    _uw.Write(UnrealConverter.GetBytes(BoolValue));
                }
                else if (TypeStr == "IntProperty")
                {
                    _uw.Write(UnrealConverter.GetBytes(ValueSize));
                }
                else if (TypeStr == "FloatProperty")
                {
                    _uw.Write(UnrealConverter.GetBytes(FloatValue));
                }
                else if (TypeStr == "StringValue")
                {
                    _uw.Write(UnrealConverter.GetBytes(StringValue));
                }
                else if (TypeStr == "NameProperty")
                {
                    _uw.Write(UnrealConverter.GetBytes(NameValue));
                }
                else if (TypeStr == "ObjectProperty")
                {
                    _uw.Write(UnrealConverter.GetBytes(ObjectValue));
                }
                else if (TypeStr == "ByteProperty")
                {
                    _uw.Write(UnrealConverter.GetBytes(EnumValue));
                    _uw.Write(UnrealConverter.GetBytes(NameValue));
                }
                return _uw.ToArray();
            }
        }
    }
}
