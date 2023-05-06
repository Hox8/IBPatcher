using System.Security.Cryptography;

namespace UnrealLib.Coalesced
{
    public class AESLib
    {
        public static byte[] GetGameKey(Shared.GameType game)
        {
            if (game == Shared.GameType.IB3) return new byte[] { 54, 110, 72, 109, 106, 100, 58, 104, 98, 87, 78, 102, 61, 57, 124, 85, 79, 50, 58, 63, 59, 75, 48, 121, 43, 103, 90, 76, 45, 106, 80, 53 };
            if (game == Shared.GameType.IB2) return new byte[] { 124, 70, 75, 125, 83, 93, 59, 118, 93, 33, 33, 99, 119, 64, 69, 52, 108, 45, 103, 77, 88, 97, 57, 121, 68, 80, 118, 82, 102, 70, 42, 66 };
            return new byte[] { 68, 75, 107, 115, 69, 75, 72, 107, 108, 100, 70, 35, 40, 87, 68, 74, 35, 70, 77, 83, 55, 106, 108, 97, 53, 102, 40, 64, 74, 49, 50, 124 };
        }
        public static byte[] CryptoECB(byte[] data, byte[] key, bool modeIsDecrypt)
        {
            Aes aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Key = key;
            aes.Padding = PaddingMode.Zeros;

            int remainder = data.Length % 16;
            if (remainder != 0) // If bin file uses an invalid block size (multiples of 16), resize array to make it so
            {
                Array.Resize(ref data, data.Length + (16 - remainder));
            }

            ICryptoTransform crypto = modeIsDecrypt ? aes.CreateDecryptor() : aes.CreateEncryptor();
            return crypto.TransformFinalBlock(data, 0, data.Length);
        }
    }
}
