namespace UnrealLib.UObject_Types
{
    public class UStruct : UField
    {
        public UObjectReference FirstChildRef;
        public int ScriptMemorySize;
        public int ScriptSerialSize;
        public byte[] ScriptData;

        public UStruct(ref UPK UPK, ref FObjectExport obj) : base(ref UPK, ref obj)
        {
            FirstChildRef = UPK.ur.ReadInt32();
            ScriptMemorySize = UPK.ur.ReadInt32();
            ScriptSerialSize = UPK.ur.ReadInt32();
            ScriptData = UPK.ur.ReadBytes(ScriptSerialSize);
        }
    }
}
