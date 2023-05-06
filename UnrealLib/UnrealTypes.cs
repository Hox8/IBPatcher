namespace UnrealLib
{
    public struct FString
    {
        public Int32 Length;    // 0 == null, Positive == ASCII, Negative == UNICODE
        public string? Data;    // ASCII/Unicode string data

        public FString(string data, UEncoding codec)
        {
            if (string.IsNullOrEmpty(data))
            {
                Length = 0;
                Data = null;
            }
            else
            {
                Length = codec == UEncoding.Unicode ? -data.Length - 1 : data.Length + 1;
                Data = data;
            }
        }
    }

    public struct FGUID
    {
        public Int32 A, B, C, D;
    }

    public struct FGenerationInfo
    {
        public int ExportCount;
        public int NameCount;
        public int NetObjectCount;
    }

    public struct FNameEntry
    {
        public FString Name;
        public int FlagsL, FlagsH;
    }

    public struct UObjectReference
    {
        public Int32 Value;

        public UObjectReference(Int32 value)
        {
            Value = value;
        }

        public static implicit operator UObjectReference(Int32 value)
        {
            return new UObjectReference(value);
        }

        public static implicit operator Int32(UObjectReference objRef)
        {
            return objRef.Value;
        }
    }

    public struct UNameIndex
    {
        public int NameTableIndex;
        public int Numeric;
    }
    public struct FObjectImport
    {
        public UNameIndex PackageNameIndex;
        public UNameIndex TypeNameIndex;
        public UObjectReference OwnerRef;
        public UNameIndex NameIndex;
    }
    public class FObjectExport
    {
        public UObjectReference TypeRef;
        public UObjectReference ParentClassRef;
        public UObjectReference OwnerRef;
        public UNameIndex NameIndex;
        public UObjectReference ArchetypeRef;
        public long ObjectFlags;
        public int SerialSize;
        public int SerialOffset;
        public int ExportFlags;
        public int NetObjectCount;
        public FGUID GUID;
        public int Unknown;
        public int[] Unknown2;

        // Memory
        public int TableIndex;
        public long TableOffset;

        public void Print()
        {
            Console.WriteLine($"{TypeRef.Value}\n{ParentClassRef.Value}\n{OwnerRef.Value}\n{NameIndex.NameTableIndex}\n{ArchetypeRef.Value}\n{TableIndex}");
        }
    }

    public struct UnknownObject
    {
        public int A, B, C, D, E;   // Placeholder names
        public int[] ObjectIndexes;
    }

    public struct FPackageFileSummary
    {
        public int Magic;
        public short UnrealVersion;     // Main engine version
        public short LicenseeVersion;   // Licensee version
        public int HeaderSize;
        public FString FolderName;      // Leftover editor data
        public int FolderFlags;

        // public int NameCount;    <-- Initialize table immediately
        public int NameOffset;
        // public int ExportCount;  <-- Initialize table immediately
        public int ExportOffset;
        // public int ImportCount;  <-- Initialize table immediately

        public int ImportOffset;
        public int DependsOffset;
        public int ImportExportGuidsOffset;     // For IB, same as HeaderSize
        public int ImportGuidsCount;            // For IB, always 0
        public int ExportGuidsCount;            // ^
        public int ThumbnailTableOffset;        // ^

        public FGUID GUID;
        public FGenerationInfo[] Generations;
        // public List<FGenerationInfo> Generations;
        public int BuildVersion;
        public int CookerVersion;

        public int CompressionFlags;
        public int CompressedChunkCount;
        // public List<FCompressedChunk> CompressedChunks;

        public int Unknown2;
        public FString[] PackageTable;
        // public List<FString> PackageTable;

        public UnknownObject[] UnknownObjectTable;
        // public List<UnknownObject> UnknownObjectTable;

        public FNameEntry[] NameTable;
        public FObjectImport[] ImportTable;
        public FObjectExport[] ExportTable;
        public byte[] DependsTable;

        // public List<FNameEntry> NameTable;
        // public List<FObjectImport> ImportTable;
        // public List<FObjectExport> ExportTable;
        // public List<byte> DependsTable;            // Need to learn about this instead of using 'byte'.
    }
}
