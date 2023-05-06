namespace UnrealLib.UObject_Types
{
    public class UScriptStruct : UStruct
    {
        public int StructFlags;
        public List<UDefaultProperty> ScriptProperties;

        public UScriptStruct(ref UPK UPK, ref FObjectExport obj) : base(ref UPK, ref obj)
        {
            StructFlags = UPK.ur.ReadInt32();
            ScriptProperties = UDefaultProperty.ReadProperties(ref UPK);
        }
    }
}
