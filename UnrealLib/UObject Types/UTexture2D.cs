namespace UnrealLib.UObject_Types
{
    public struct Mip
    {
        public Int32 MipFlags;
        public Int32 UncompressedSize;
        public Int32 CompressedSize;
        public Int32 Offset;
        public Int32 Width;
        public Int32 Height;
    }

    public enum MipFlags : byte
    {
        StoredInSeparateFile = 0x1,
        StoredAsSeparateData = 0x40,
        EmptyData = 0x20,
        CompressedZLib = 0x2,
        CompressedLZO = 0x10,
        CompressedLZX = 0x80
    }

    public class UTexture2D : UObject
    {
        public byte[] Unknown;     // 3 null bytes
        public Int32 Offset;
        public Mip[] Mips;

        public UTexture2D(ref UPK UPK, ref FObjectExport obj) : base(ref UPK, ref obj)
        {
            Unknown = UPK.ur.ReadBytes(12);
            Offset = UPK.ur.ReadInt32();
            Mips = new Mip[UPK.ur.ReadInt32()];
            for (int i = 0; i < Mips.Length; i++)
            {
                Mips[i] = new Mip()
                {
                    MipFlags = UPK.ur.ReadInt32(),
                    UncompressedSize = UPK.ur.ReadInt32(),
                    CompressedSize = UPK.ur.ReadInt32(),
                    Offset = UPK.ur.ReadInt32(),
                    Width = UPK.ur.ReadInt32(),
                    Height = UPK.ur.ReadInt32()
                };
            }
        }
    }
}
