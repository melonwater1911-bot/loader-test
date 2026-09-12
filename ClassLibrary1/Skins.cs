using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Logs;

partial class Loader
{
    private static class Skins
    {
        public static void Install(List<SkinFile> files)
        {
            if(files.Count==0) return;
            string root=Path.GetFullPath(NuclearOption.AddressablePaths.AppDataSkinPath).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            int written=0;
            for (int i=0; i<files.Count; i++)
            {
                string target=Path.GetFullPath(Path.Combine(root,files[i].RelativePath));
                if (!target.StartsWith(root,StringComparison.InvariantCultureIgnoreCase))
                {
                    LogError("Skin path escapes the skin folder, refused: "+files[i].RelativePath);
                    continue;
                }
                if(File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(files[i].Data)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target,files[i].Data);
                written++;
            }
            if(written>0) Log("Skins: wrote "+written+" of "+files.Count+" files");
        }
    }
}
