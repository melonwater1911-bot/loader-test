using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class Loader
{
    private List<PackEntry> ReadEntries(string path,out long bundleOffset)
    {
        var entries = new List<PackEntry>();
        bundleOffset=0;
        using (FileStream stream=File.OpenRead(path))
        using (var reader = new BinaryReader(stream,Encoding.UTF8))
        {
            reader.ReadBytes(PackHeader.Length);
            int version=reader.ReadInt32();
            if(version!=SupportedVersion) throw new Exception("Pack format "+version+", loader wants "+SupportedVersion+". Rebuild the pack.");
            int count=reader.ReadInt32();
            for (int i=0; i<count; i++)
            {
                var entry = new PackEntry();
                entry.Name=Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                entry.Type=(EntryType)reader.ReadInt32();
                long length=reader.ReadInt64();
                if (entry.Type==EntryType.AssetBundle)
                {
                    bundleOffset=stream.Position;
                    stream.Seek(length,SeekOrigin.Current);
                }
                else entry.Data=reader.ReadBytes((int)length);
                entries.Add(entry);
            }
        }
        return entries;
    }
}
