namespace UnrealLib.Coalesced
{
    public class Property
    {
        public FString Key;
        public FString Value;
    }
    public class Section
    {
        public FString Name;
        public List<Property> Properties;
    }
    public class Ini
    {
        public FString Path;
        public List<Section> Sections;

        public int GetSectionIndex(string sectionName)
        {
            for (int i = 0; i < Sections.Count; i++)
            {
                if (Sections[i].Name.Data == sectionName) return i;
            }
            return -1;
        }
    }

    public class Coalesced
    {
        public Shared.GameType Game;
        public Dictionary<string, Ini> Inis;

        /// <summary>
        /// Creates a Coalesced.bin file in memory. Will be unencrypted and deserialized on init.
        /// </summary>
        /// <param name="coalescedStream"></param>
        /// <param name="game">The game the given Coalesced.bin file originates from.</param>
        public Coalesced(MemoryStream coalescedStream, Shared.GameType game)
        {
            Game = game;
            if (Game != Shared.GameType.IB1 && CoalescedIsEncrypted(ref coalescedStream))
            {
                coalescedStream = new(AESLib.CryptoECB(coalescedStream.ToArray(), AESLib.GetGameKey(Game), true));
            }

            // If coalesced was successfully decrypted, attempt deserializaton
            if (!CoalescedIsEncrypted(ref coalescedStream))
            {
                CoalescedToMemory(ref coalescedStream);  // Store coalesced structure in memory
            }
        }

        private static bool CoalescedIsEncrypted(ref MemoryStream ms)
        {
            ms.Position = 2;
            return (ms.ReadByte() == 0 && ms.ReadByte() == 0) ? false : true;
        }

        /// <summary>
        /// Takes a coalesced file and reads it into this Coalesced instance. Called by constructor
        /// </summary>
        /// <param name="unReader">Unreal Reader of coalesced data</param>
        private void CoalescedToMemory(ref MemoryStream coalStream)
        {
            using (var unReader = new UnrealReader(coalStream))
            {
                unReader.BaseStream.Position = 0;
                int iniCount = unReader.ReadInt32();
                Inis = new(iniCount);

                for (int ini = 0; ini < iniCount; ini++)
                {
                    Ini activeIni = new()  // Initialize new ini
                    {
                        Path = unReader.ReadFString(),
                        Sections = new(unReader.ReadInt32())
                    };

                    for (int section = 0; section < activeIni.Sections.Capacity; section++)
                    {
                        activeIni.Sections.Add(new()  // Initialize new section
                        {
                            Name = unReader.ReadFString(),
                            Properties = new(unReader.ReadInt32())
                        });

                        for (int prop = 0; prop < activeIni.Sections[section].Properties.Capacity; prop++)
                        {
                            activeIni.Sections[section].Properties.Add(new()  // Initialize new property
                            {
                                Key = unReader.ReadFString(),
                                Value = unReader.ReadFString()
                            });
                        }
                    }
                    Inis.Add(activeIni.Path.Data, activeIni);
                }
            }
        }

        /// <summary>
        /// Serializes the coalesced file in-memory into a byte array
        /// </summary>
        /// <returns></returns>
        public byte[] MemoryToCoalesced()
        {
            var outStream = new UnrealWriter(new MemoryStream());

            outStream.Write(Inis.Count);
            foreach (KeyValuePair<string, Ini> ini in Inis)
            {
                outStream.Write(ini.Value.Path);
                outStream.Write(ini.Value.Sections.Count);

                foreach (Section section in ini.Value.Sections)
                {
                    outStream.Write(section.Name);
                    outStream.Write(section.Properties.Count);

                    foreach (Property property in section.Properties)
                    {
                        outStream.Write(property.Key);
                        outStream.Write(property.Value);
                    }
                }
            }

            if (Game != Shared.GameType.IB1)
            {
                return AESLib.CryptoECB(((MemoryStream)outStream.BaseStream).ToArray(), AESLib.GetGameKey(Game), false);
            }
            return ((MemoryStream)outStream.BaseStream).ToArray();
        }
    }
}
