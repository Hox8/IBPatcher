namespace UnrealLib.UObject_Types
{
    public class UObject
    {
        public Int32 NetIndex;
        public List<UDefaultProperty> Properties;

        // In-memory
        FObjectExport exportEntry;

        public UObject(ref UPK UPK, ref FObjectExport obj)
        {
            UPK.ur.BaseStream.Position = obj.SerialOffset;

            exportEntry = obj;
            NetIndex = UPK.ur.ReadInt32();
            Properties = new List<UDefaultProperty>();

            // Only for select classes? Need to figure out a system
            if (true)  // Handle default properties
            {
                Properties = UDefaultProperty.ReadProperties(ref UPK);
            }
        }
    }
}
