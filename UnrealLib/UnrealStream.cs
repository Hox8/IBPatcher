using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace UnrealLib
{
    public enum UEncoding
    {
        ASCII = 0,
        Unicode = 1
    }

    public class UnrealReader : IDisposable
    {

        public virtual MemoryStream BaseStream => _stream;
        private readonly MemoryStream _stream;
        private readonly UEncoding codec;

        public UnrealReader(MemoryStream existingStream, UEncoding codec = UEncoding.ASCII)
        {
            _stream = existingStream;
            this.codec = codec;
        }

        public UnrealReader(byte[] bytes, UEncoding codec = UEncoding.ASCII)
        {
            _stream = new MemoryStream(bytes);
            this.codec = codec;
        }

        public UnrealReader(UEncoding codec = UEncoding.ASCII)
        {
            _stream = new MemoryStream();
            this.codec = codec;
        }

        public byte[] ReadBytes(int amount)
        {
            byte[] buffer = new byte[amount];
            BaseStream.Read(buffer, 0, amount);
            return buffer;
        }

        public byte ReadByte()
        {
            return ReadBytes(1)[0];
        }

        public char[] ReadChars(int amount, UEncoding? overrideEncoding = null)
        {
            UEncoding encoding = overrideEncoding ?? codec;

            Span<byte> buffer = stackalloc byte[amount * (encoding == UEncoding.Unicode ? 2 : 1)];
            _stream.Read(buffer);

            if (encoding == UEncoding.Unicode) return MemoryMarshal.Cast<byte, char>(buffer).ToArray(); // Unicode

            // ASCII cannot benefit from above casting since they're one-byte chars
            char[] chars = new char[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
            {
                chars[i] = (char)buffer[i];
            }
            return chars;
        }

        public char ReadChar(UEncoding? overrideEncoding = null)
        {
            return ReadChars(1, overrideEncoding)[0];
        }

        public Int64 ReadInt64()
        {
            Span<byte> buf = stackalloc byte[8];
            _stream.Read(buf);
            return
                buf[0] + (buf[1] << 8) + (buf[2] << 16) + (buf[3] << 24) +
                (buf[4] << 32) + (buf[5] << 40) + (buf[6] << 48) + (buf[7] << 56);
        }

        public Int32 ReadInt32()
        {
            Span<byte> buf = stackalloc byte[4];
            _stream.Read(buf);
            return buf[0] + (buf[1] << 8) + (buf[2] << 16) + (buf[3] << 24);
        }

        public Int16 ReadInt16()
        {
            Span<byte> buf = stackalloc byte[2];
            _stream.Read(buf);
            return (Int16)(buf[0] + (buf[1] << 8));
        }

        public float ReadFloat()
        {
            ReadOnlySpan<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.ReadSingleLittleEndian(buffer);
            // return BitConverter.ToSingle(buffer);
            return Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(buffer));
        }

        public string ReadCString()
        {
            StringBuilder sb = new();
            char currentChar;

            while (true)
            {
                // if (BaseStream.Position == BaseStream.Length)
                // throw new EndOfStreamException("Cannot read CString; reached end of stream!");

                if ((currentChar = ReadChar()) == '\0') return sb.ToString();
                sb.Append(currentChar);
            }
        }

        public FString ReadFString()
        {
            FString fStr = new() { Length = ReadInt32() };
            char[] buffer;

            if (fStr.Length == 0) return fStr;
            if (fStr.Length > 0) buffer = ReadChars(fStr.Length, UEncoding.ASCII);
            else buffer = ReadChars(fStr.Length * -1, UEncoding.Unicode);

            fStr.Data = new string(buffer.AsSpan(0, buffer.Length - 1));  // Trims off null terminator
            return fStr;
        }

        public FGUID ReadGUID()
        {
            FGUID GUID = new()
            {
                A = ReadInt32(),
                B = ReadInt32(),
                C = ReadInt32(),
                D = ReadInt32()
            };
            return GUID;
        }

        public FGenerationInfo ReadGenInfo()
        {
            FGenerationInfo Info = new()
            {
                ExportCount = ReadInt32(),
                NameCount = ReadInt32(),
                NetObjectCount = ReadInt32()
            };
            return Info;
        }

        public UnknownObject ReadUnknownObject()
        {
            UnknownObject UnknownObj = new()
            {
                A = ReadInt32(),
                B = ReadInt32(),
                C = ReadInt32(),
                D = ReadInt32(),
                E = ReadInt32(),
                ObjectIndexes = new int[ReadInt32()]
            };

            for (int i = 0; i < UnknownObj.ObjectIndexes.Length; i++)
            {
                UnknownObj.ObjectIndexes[i] = ReadInt32();
            }
            return UnknownObj;
        }

        public FNameEntry ReadNameEntry()
        {
            FNameEntry NameEntry = new()
            {
                Name = ReadFString(),
                FlagsL = ReadInt32(),
                FlagsH = ReadInt32(),
            };
            return NameEntry;
        }

        public FObjectImport ReadImportEntry()
        {
            FObjectImport importEntry = new()
            {
                PackageNameIndex = new UNameIndex()
                {
                    NameTableIndex = ReadInt32(),
                    Numeric = ReadInt32()
                },

                TypeNameIndex = new UNameIndex()
                {
                    NameTableIndex = ReadInt32(),
                    Numeric = ReadInt32()
                },

                OwnerRef = ReadInt32(),

                NameIndex = new UNameIndex()
                {
                    NameTableIndex = ReadInt32(),
                    Numeric = ReadInt32()
                }
            };
            return importEntry;
        }

        public FObjectExport ReadExportEntry()
        {
            FObjectExport exportEntry = new()
            {
                TypeRef = ReadInt32(),
                ParentClassRef = ReadInt32(),
                OwnerRef = ReadInt32(),

                NameIndex = new UNameIndex()
                {
                    NameTableIndex = ReadInt32(),
                    Numeric = ReadInt32()
                },

                ArchetypeRef = ReadInt32(),
                ObjectFlags = ReadInt64(),

                SerialSize = ReadInt32(),
                SerialOffset = ReadInt32(),
                ExportFlags = ReadInt32(),
                NetObjectCount = ReadInt32(),

                GUID = ReadGUID(),
                Unknown = ReadInt32()
            };

            exportEntry.Unknown2 = new int[exportEntry.NetObjectCount];
            for (int i = 0; i < exportEntry.NetObjectCount; i++)
            {
                exportEntry.Unknown2[i] = ReadInt32();
            }

            return exportEntry;
        }

        public UNameIndex ReadNameIndex()
        {
            UNameIndex nameIndex = new()
            {
                NameTableIndex = ReadInt32(),
                Numeric = ReadInt32()
            };
            return nameIndex;
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
    public class UnrealWriter : BinaryWriter
    {
        public UnrealWriter(MemoryStream output) : base(output)
        {

        }

        public void WriteCString(string data, UEncoding codec =  UEncoding.ASCII)
        {
            if (codec == UEncoding.Unicode)
            {
                Write(MemoryMarshal.Cast<char, byte>(data));
                Write(new byte[] { 0, 0 });
            }
            else
            {
                Write(UnrealConverter.GetBytes(data));
                Write((byte)0);
            }
        }

        public void Write(FString fStr)  // ASSUMES LENGTH IS CORRECT!
        {
            Write(fStr.Length);
            if (fStr.Length == 0) return;
            if (fStr.Length > 0)    // ASCII
            {
                Write(UnrealConverter.GetBytes(fStr.Data));
                Write(0);
            }
            else  // UNICODE
            {
                Write(MemoryMarshal.Cast<char, byte>(fStr.Data));
                Write(new byte[] { 0, 0 });
            }
        }

        public void Write(FGUID guid)
        {
            Write(guid.A);
            Write(guid.B);
            Write(guid.C);
            Write(guid.D);
        }

        public void Write(FGenerationInfo genInfo)
        {
            Write(genInfo.ExportCount);
            Write(genInfo.NameCount);
            Write(genInfo.NetObjectCount);
        }

        public void Write(UnknownObject unknownObj)
        {
            Write(unknownObj.A);
            Write(unknownObj.B);
            Write(unknownObj.C);
            Write(unknownObj.D);
            Write(unknownObj.E);
            for (int i = 0; i < unknownObj.ObjectIndexes.Length; i++)
            {
                Write(unknownObj.ObjectIndexes[i]);
            }
        }

        public void Write(UNameIndex nameIndex)
        {
            Write(nameIndex.NameTableIndex);
            Write(nameIndex.Numeric);
        }

        public void Write(FNameEntry nameEntry)
        {
            Write(nameEntry.Name);
            Write(nameEntry.FlagsL);
            Write(nameEntry.FlagsH);
        }

        public void Write(FObjectImport importEntry)
        {
            Write(importEntry.PackageNameIndex);
            Write(importEntry.TypeNameIndex);
            Write(importEntry.OwnerRef);
            Write(importEntry.NameIndex);
        }

        public void Write(FObjectExport exportEntry)
        {
            Write(exportEntry.TypeRef);
            Write(exportEntry.ParentClassRef);
            Write(exportEntry.OwnerRef);
            Write(exportEntry.NameIndex);
            Write(exportEntry.ArchetypeRef);
            Write(exportEntry.ObjectFlags);
            Write(exportEntry.SerialSize);
            Write(exportEntry.SerialOffset);
            Write(exportEntry.ExportFlags);
            Write(exportEntry.NetObjectCount);
            Write(exportEntry.GUID);
            Write(exportEntry.Unknown);
            for (int i = 0; i < exportEntry.NetObjectCount; i++)
            {
                Write(exportEntry.Unknown2[i]);
            }
        }
    }
}
