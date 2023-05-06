using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace UnrealLib
{
    public class UnrealConverter
    {
        public static byte[] GetBytes(bool value)
        {
            return new byte[] { (byte)(value ? 1 : 0) };
        }
        public static byte[] GetBytes(Int64 value)
        {
            byte[] buffer = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
            return buffer;
        }
        public static byte[] GetBytes(Int32 value)
        {
            byte[] buffer = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            return buffer;
        }

        public static byte[] GetBytes(Int16 value)
        {
            byte[] buffer = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
            return buffer;
        }

        public static byte[] GetBytes(float value)
        {
            byte[] buffer = new byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
            return buffer;
        }

        public static byte[] GetBytes(string value, UEncoding codec = UEncoding.ASCII)
        {
            if (codec == UEncoding.Unicode) return MemoryMarshal.Cast<char, byte>(value).ToArray();
            byte[] chars = new byte[value.Length];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = (byte)value[i];
            }
            return chars;
        }

        public static byte[] GetBytes(FString fStr)
        {
            if (fStr.Length == 0) return new byte[4] { 0, 0, 0, 0 };

            UEncoding codec;
            if (fStr.Length < 0)
            {
                fStr.Length *= -2;
                codec = UEncoding.Unicode;
            }
            else codec = UEncoding.ASCII;

            byte[] buffer = new byte[4 + fStr.Length];
            Buffer.BlockCopy(GetBytes(fStr.Length), 0, buffer, 0, 4);
            Buffer.BlockCopy(GetBytes(fStr.Data, codec), 0, buffer, 4, fStr.Length);

            return buffer;
        }

        public static byte[] GetBytes(UNameIndex nameIndex)
        {
            byte[] buffer = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), nameIndex.NameTableIndex);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 8), nameIndex.Numeric);
            return buffer;
        }
    }
}
