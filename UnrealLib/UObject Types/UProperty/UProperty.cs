namespace UnrealLib.UObject_Types.UProperty
{
    public class UProperty : UField
    {
        Int16 ArrayDim;
        Int16 ElementSize;
        Int64 PropertyFlags;

        public UProperty(ref UPK UPK, ref FObjectExport obj) : base(ref UPK, ref obj)
        {
            ArrayDim = UPK.ur.ReadInt16();
            ElementSize = UPK.ur.ReadInt16();
            PropertyFlags = UPK.ur.ReadInt64();
        }
    }
}
