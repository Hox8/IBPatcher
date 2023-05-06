namespace UnrealLib.UObject_Types
{
    public class UField : UObject
    {
        UObjectReference NextRef;
        UObjectReference ParentRef;

        public UField(ref UPK UPK, ref FObjectExport obj) : base(ref UPK, ref obj)
        {
            NextRef = UPK.ur.ReadInt32();
            ParentRef = UPK.ur.ReadInt32();
        }
    }
}
