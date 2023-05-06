namespace UnrealLib.UObject_Types
{
    public class UClass : UState
    {
        public Int32 ClassFlags;
        public UObjectReference Within;
        public UNameIndex ConfigName;

        public Int32 ComponentsMapCount;
        public Dictionary<UNameIndex, Int32> ComponentsMap;

        public Int32 InterfacesCount;
        public Dictionary<UObjectReference, Int32> Interfaces;

        public UNameIndex DLLBindName;
        public UObjectReference Default;

        public UClass(ref UPK UPK, ref FObjectExport obj) : base(ref UPK, ref obj)
        {
            ClassFlags = UPK.ur.ReadInt32();
            Within = UPK.ur.ReadInt32();
            ConfigName = UPK.ur.ReadNameIndex();

            ComponentsMapCount = UPK.ur.ReadInt32();
            ComponentsMap = new(ComponentsMapCount);
            for (int i = 0; i < ComponentsMapCount; i++)
            {
                ComponentsMap.Add(UPK.ur.ReadNameIndex(), UPK.ur.ReadInt32());
            }

            InterfacesCount = UPK.ur.ReadInt32();
            Interfaces = new(InterfacesCount);
            for (int i = 0; i < InterfacesCount; i++)
            {
                Interfaces.Add(UPK.ur.ReadInt32(), UPK.ur.ReadInt32());
            }

            DLLBindName = UPK.ur.ReadNameIndex();
            Default = UPK.ur.ReadInt32();
        }
    }
}
