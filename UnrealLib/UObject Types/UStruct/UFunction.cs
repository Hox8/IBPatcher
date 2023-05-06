namespace UnrealLib.UObject_Types
{
    public class UFunction : UStruct
    {
        public Int16 NativeToken;
        public byte OperPrecedence;
        public Int32 FunctionFlags;

        public UFunction(ref UPK UPK, ref FObjectExport obj) : base(ref UPK, ref obj)
        {
            NativeToken = UPK.ur.ReadInt16();
            OperPrecedence = UPK.ur.ReadByte();
            FunctionFlags = UPK.ur.ReadInt32();
        }
    }
}
