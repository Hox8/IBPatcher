namespace UnrealLib.UObject_Types
{
    // UStates don't contain any UnrealScript themselves -
    // They point to functions in their FunctionMaps
    public class UState : UStruct
    {
        public Int32 ProbeMask;
        public Int16 LabelTableOffset;
        public Int32 StateFlags;

        public Int32 FunctionMapCount;
        public Dictionary<UNameIndex, UObjectReference> FunctionMap;

        public UState(ref UPK UPK, ref FObjectExport obj) : base(ref UPK, ref obj)
        {
            ProbeMask = UPK.ur.ReadInt32();
            LabelTableOffset = UPK.ur.ReadInt16();
            StateFlags = UPK.ur.ReadInt32();

            FunctionMapCount = UPK.ur.ReadInt32();
            FunctionMap = new(FunctionMapCount);
            for (int i = 0; i < FunctionMapCount; i++)
            {
                FunctionMap.Add(UPK.ur.ReadNameIndex(), UPK.ur.ReadInt32());
            }
        }
    }
}
