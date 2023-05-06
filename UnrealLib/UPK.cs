using System.Text;

namespace UnrealLib
{
    public class UPK : IDisposable
    {
        public FPackageFileSummary Header;

        // public string PackageName;
        public string PackagePath;
        // public long PackageSize;

        internal UPKError Error;
        public int NoneIndex;

        public UnrealReader ur;
        public UnrealWriter uw;

        public bool HasBeenModified = false;

        public enum UPKError
        {
            None = 0,
            BadPath
        }

        #region Constructors

        public UPK(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Error = UPKError.BadPath;
                return;
            }

            PackagePath = filePath;

            ur = new UnrealReader();
            uw = new UnrealWriter(new MemoryStream());
            using (var fs = File.OpenRead(PackagePath)) fs.CopyTo(ur.BaseStream);
            ur.BaseStream.Position = 0;

            DeserializeHeader();
            NoneIndex = GetNameIndex("None");
        }

        public UPK(MemoryStream ms)
        {
            ur = new UnrealReader(ms);  // Read stream and write stream will reference the SAME memorystream
            uw = new UnrealWriter(ms);
            ur.BaseStream.Position = 0;

            DeserializeHeader();
            NoneIndex = GetNameIndex("None");
        }
        #endregion
        private void DeserializeHeader()
        {
            ur.BaseStream.Position = 0;

            Header.Magic = ur.ReadInt32();
            Header.UnrealVersion = ur.ReadInt16();
            Header.LicenseeVersion = ur.ReadInt16();
            Header.HeaderSize = ur.ReadInt32();
            Header.FolderName = ur.ReadFString();
            Header.FolderFlags = ur.ReadInt32();

            Header.NameTable = new FNameEntry[ur.ReadInt32()];      // Name Count
            Header.NameOffset = ur.ReadInt32();
            Header.ExportTable = new FObjectExport[ur.ReadInt32()]; // Export Count
            Header.ExportOffset = ur.ReadInt32();
            Header.ImportTable = new FObjectImport[ur.ReadInt32()]; // Import Count
            Header.ImportOffset = ur.ReadInt32();
            Header.DependsOffset = ur.ReadInt32();
            Header.ImportExportGuidsOffset = ur.ReadInt32();
            ur.BaseStream.Position += 12;   // Skip 12 null bytes. Thumbnail table?

            Header.GUID = ur.ReadGUID();
            Header.Generations = new FGenerationInfo[ur.ReadInt32()];
            for (int i = 0; i < Header.Generations.Length; i++)
            {
                Header.Generations[i] = ur.ReadGenInfo();
            }

            Header.BuildVersion = ur.ReadInt32();
            Header.CookerVersion = ur.ReadInt32();
            Header.CompressionFlags = ur.ReadInt32();
            Header.CompressedChunkCount = ur.ReadInt32();  // Always 0 by default
            Header.Unknown2 = ur.ReadInt32();

            // PACKAGE TABLE
            Header.PackageTable = new FString[ur.ReadInt32()];
            for (int i = 0; i < Header.PackageTable.Length; i++)
            {
                Header.PackageTable[i] = ur.ReadFString();
            }

            // UNKNOWN OBJECT TABLE
            Header.UnknownObjectTable = new UnknownObject[ur.ReadInt32()];
            for (int i = 0; i < Header.UnknownObjectTable.Length; i++)
            {
                Header.UnknownObjectTable[i] = ur.ReadUnknownObject();
            }

            // NAME TABLE
            for (int i = 0; i < Header.NameTable.Length; i++)
            {
                Header.NameTable[i] = ur.ReadNameEntry();
            }

            // IMPORT TABLE
            for (int i = 0; i < Header.ImportTable.Length; i++)
            {
                Header.ImportTable[i] = ur.ReadImportEntry();
            }

            // EXPORT TABLE
            for (int i = 0; i < Header.ExportTable.Length; i++)
            {
                Header.ExportTable[i] = ur.ReadExportEntry();
                Header.ExportTable[i].TableIndex = i;
                Header.ExportTable[i].TableOffset = (int)ur.BaseStream.Position;
            }

            Header.DependsTable = ur.ReadBytes(Header.HeaderSize - Header.DependsOffset);
        }
        private void SerializeHeader()
        {
            uw.Write(Header.Magic);
            uw.Write(Header.UnrealVersion);
            uw.Write(Header.LicenseeVersion);
            uw.Write(Header.HeaderSize);

            uw.Write(Header.FolderName);  // TEST
            uw.Write(Header.FolderFlags);

            uw.Write(Header.NameTable.Length);
            uw.Write(Header.NameOffset);            // Offset: 20 + Folder string length
            uw.Write(Header.ExportTable.Length);
            uw.Write(Header.ExportOffset);
            uw.Write(Header.ImportTable.Length);
            uw.Write(Header.ImportOffset);
            uw.Write(Header.DependsOffset);
            uw.Write(Header.ImportExportGuidsOffset);
            uw.BaseStream.Position += 12;

            uw.Write(Header.GUID);
            uw.Write(Header.Generations.Length);
            for (int i = 0; i < Header.Generations.Length; i++)
            {
                uw.Write(Header.Generations[i]);
            }
            uw.Write(Header.BuildVersion);
            uw.Write(Header.CookerVersion);
            uw.Write(Header.CompressionFlags);
            uw.Write(Header.CompressedChunkCount);
            uw.Write(Header.Unknown2);

            // PACKAGE TABLE
            uw.Write(Header.PackageTable.Length);
            for (int i = 0; i < Header.PackageTable.Length; i++)
            {
                uw.Write(Header.PackageTable[i]);
            }

            // UNKNOWN OBJECT TABLE
            uw.Write(Header.UnknownObjectTable.Length);
            for (int i = 0; i < Header.UnknownObjectTable.Length; i++)
            {
                uw.Write(Header.UnknownObjectTable[i]);
            }

            // NAME TABLE
            for (int i = 0; i < Header.NameTable.Length; i++)
            {
                uw.Write(Header.NameTable[i]);
            }

            // IMPORT TABLE
            for (int i = 0; i < Header.ImportTable.Length; i++)
            {
                uw.Write(Header.ImportTable[i]);
            }

            // EXPORT TABLE
            for (int i = 0; i < Header.ExportTable.Length; i++)
            {
                uw.Write(Header.ExportTable[i]);
            }

            uw.Write(Header.DependsTable);

        }

        #region GetName
        public int GetNameTableIndex(string value)
        {
            for (int i = 0; i < Header.NameTable.Length; i++)
            {
                if (Header.NameTable[i].Name.Data == value) return i;
            }
            return -1;
        }
        public string GetName(UNameIndex nameIndex)  // Can't get full name with obj data
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Header.NameTable[nameIndex.NameTableIndex].Name.Data);
            if (nameIndex.Numeric > 0) sb.Append($"_{nameIndex.Numeric - 1}");
            return sb.ToString();
        }

        public string GetName(UObjectReference objRef)
        {
            if (objRef == 0) return string.Empty;

            if (objRef < 0) // Import
            {
                return GetName(Header.ImportTable[~objRef].NameIndex);
            }
            return GetName(Header.ExportTable[objRef - 1].NameIndex);
        }
        public string GetName(FObjectExport exportObject, bool returnFullname = false)  // TODO: redo this
        {
            StringBuilder sb = new StringBuilder();
            if (returnFullname)
            {
                UObjectReference currentRef = exportObject.OwnerRef;    // Do import objects have inheritance???
                while (currentRef != 0)
                {
                    sb.Insert(0, $"{GetName(currentRef)}.");

                    if (currentRef < 0)
                    {
                        currentRef = Header.ImportTable[~currentRef].OwnerRef;
                    }
                    else currentRef = Header.ExportTable[currentRef - 1].OwnerRef;
                }
            }
            sb.Append(GetName(exportObject.NameIndex));
            return sb.ToString();
        }

        public string GetName(FObjectImport importObject, bool returnFullname = false)
        {
            StringBuilder sb = new StringBuilder();
            if (returnFullname)
            {
                UObjectReference currentRef = importObject.OwnerRef;    // Do import objects have inheritance???
                while (currentRef != 0)
                {
                    sb.Insert(0, $"{GetName(currentRef)}.");
                }
            }
            sb.Append(GetName(importObject.NameIndex));
            return sb.ToString();
        }

        /// <summary>
        /// Searches either Name Table or Import/Export tables for an object/name entry. Returns string name
        /// </summary>
        /// <param name="index"></param>
        /// <param name="FromNameTable"></param>
        /// <returns></returns>
        public string GetName(int index, bool FromNameTable)
        {
            if (!FromNameTable)
            {
                if (index == 0) return string.Empty;
                if (index < 0) return GetName(Header.ImportTable[~index], returnFullname: true);
                return GetName(Header.ExportTable[index - 1], returnFullname: true);
            }
            return Header.NameTable[index].Name.Data;
        }

        public int GetNameIndex(string str)
        {
            for (int i = 0; i < Header.NameTable.Length; i++)
            {
                if (Header.NameTable[i].Name.Data == str) return i;
            }
            return -1;
        }
        #endregion

        /// <summary>
        /// Returns UNREAL object index! > 0 == export index, < 0 == import index, 0 == not found
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public int FindObject(string name)  // Return types: negative == import, positive == export, 0 == not found. Finds first instance
        {
            string objectName;
            bool isFullName = false;
            int objectName_NameTableIndex = -1;

            // Split passed name into firstName and the joined parent names
            int separatorIndex = name.LastIndexOf('.');
            if (separatorIndex != -1)
            {
                isFullName = true;
                objectName = name[(separatorIndex+1)..];
            }
            else
            {
                objectName = name;
            }

            // Get nametable index of primaryName
            for (int i = 0; i < Header.NameTable.Length; i++)
            {
                if (Header.NameTable[i].Name.Data == objectName)
                {
                    objectName_NameTableIndex = i;
                    break;
                }
            }
            if (objectName_NameTableIndex == -1) return 0;  // If primaryName wasn't found, object doesn't exist

            // Search export table
            for (int i = 0; i < Header.ExportTable.Length; i++)
            {
                if (Header.ExportTable[i].NameIndex.NameTableIndex == objectName_NameTableIndex)
                {
                    if (isFullName)
                    {
                        if (GetName(Header.ExportTable[i], returnFullname: true) == name) return i + 1;
                    }
                    else return i + 1;
                }
            }

            // Serch import table
            for (int i = 0; i < Header.ImportTable.Length; i++)
            {
                if (Header.ImportTable[i].NameIndex.NameTableIndex == objectName_NameTableIndex)
                {
                    if (isFullName)
                    {
                        if (GetName(Header.ImportTable[i], returnFullname: true) == name) return ~i;
                    }
                    else return ~i;
                }
            }
            return 0;  // Not found; this code should never be reached
        }

        
        public void Dispose()
        {
            if (ur.BaseStream != null)
            {
                uw.Dispose();
                uw = null;
            }
            if (ur.BaseStream != null)
            {
                ur.Dispose();
                ur = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
