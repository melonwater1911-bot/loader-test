using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using static NobndlLog;

sealed class NobndlSkinFile
{
    public string PackId;
    public string RelativePath;
    public byte[] Data;
}

static class NobndlSkins
{
    private const string StagingFolderName=".nobndl-temp";
    private const long MaxFileBytes=256L*1024L*1024L;

    private static readonly HashSet<string> AllowedExtensions=
        new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            ".json",".hash",".bundle",".bin"
        };

    private static string root;
    private static bool rootResolved;

    public static string Root
    {
        get
        {
            if (!rootResolved)
            {
                rootResolved=true;
                root=FindGameSkinFolder();
            }
            return root;
        }
    }

    public static bool Validate(string relativePath,byte[] data,string packId)
    {
        if (string.IsNullOrEmpty(relativePath) || data==null)
        {
            LogError("Skin entry has no name or no data | pack="+packId);
            return false;
        }
        if (relativePath.IndexOfAny(Path.GetInvalidPathChars())>=0)
        {
            LogError("Skin path has invalid characters, refused: "+relativePath+" | pack="+packId);
            return false;
        }
        string normalized=relativePath.Replace('\\','/');
        if (normalized.StartsWith("/") || normalized.Contains(":") || Path.IsPathRooted(normalized))
        {
            LogError("Skin path is absolute, refused: "+relativePath+" | pack="+packId);
            return false;
        }
        string[] segments=normalized.Split('/');
        for (int i=0; i<segments.Length; i++)
        {
            if (segments[i]==".." || segments[i]=="." || segments[i].Length==0)
            {
                LogError("Skin path walks outside the skin folder, refused: "+relativePath+" | pack="+packId);
                return false;
            }
        }
        if (segments.Length<2)
        {
            LogError("Skin file must sit in a subfolder named after the skin: "+relativePath+" | pack="+packId);
            return false;
        }
        string extension=Path.GetExtension(normalized);
        if (!AllowedExtensions.Contains(extension))
        {
            LogError("Skin file extension is not allowed: "+relativePath+" | pack="+packId+
                     " | allowed: .json .hash .bundle .bin");
            return false;
        }
        if (data.LongLength>MaxFileBytes)
        {
            LogError("Skin file is too large: "+relativePath+" | pack="+packId+
                     " bytes="+data.LongLength+" limit="+MaxFileBytes);
            return false;
        }
        return true;
    }

    public static bool Install(string packId,List<NobndlSkinFile> files)
    {
        if (files==null || files.Count==0)
            return true;
        if (string.IsNullOrEmpty(Root))
        {
            LogError("Skins not installed, the game skin folder was not found | pack="+packId+" files="+files.Count);
            return false;
        }
        string staging=StagingFolder(packId);
        List<string> staged=new List<string>();
        List<string> targets=new List<string>();
        try
        {
            for (int i=0; i<files.Count; i++)
            {
                NobndlSkinFile file=files[i];
                string relative=file.RelativePath.Replace('/',Path.DirectorySeparatorChar);
                string target=Path.Combine(Root,relative);
                if (!IsInside(Root,target))
                {
                    LogError("Skin path escapes the skin folder, refused: "+file.RelativePath+" | pack="+packId);
                    return false;
                }
                string stagedPath=Path.Combine(staging,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath));
                using (FileStream stream=new FileStream(stagedPath,FileMode.Create,FileAccess.Write))
                {
                    stream.Write(file.Data,0,file.Data.Length);
                    stream.Flush(true);
                }
                staged.Add(stagedPath);
                targets.Add(target);
            }
            string backupRoot=Path.Combine(staging,".replaced");
            List<string> movedTargets=new List<string>();
            List<string> backups=new List<string>();
            int replaced=0;
            try
            {
                for (int i=0; i<staged.Count; i++)
                {
                    if (File.Exists(targets[i]) && Same(targets[i],files[i].Data))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(targets[i]));
                    string backup=null;
                    if (File.Exists(targets[i]))
                    {
                        backup=Path.Combine(backupRoot,files[i].RelativePath.Replace('/',Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));
                        File.Move(targets[i],backup);
                    }
                    File.Move(staged[i],targets[i]);
                    movedTargets.Add(targets[i]);
                    backups.Add(backup);
                    replaced++;
                }
            }
            catch (Exception moveEx)
            {
                LogError("Skin install failed while replacing files, rolling back: pack="+packId+" | "+moveEx.Message);
                for (int i=movedTargets.Count-1; i>=0; i--)
                {
                    try
                    {
                        if (File.Exists(movedTargets[i]))
                            File.Delete(movedTargets[i]);
                        if (backups[i]!=null && File.Exists(backups[i]))
                            File.Move(backups[i],movedTargets[i]);
                    }
                    catch (Exception rollbackEx)
                    {
                        LogError("Rollback failed for "+movedTargets[i]+" | "+rollbackEx.Message);
                    }
                }
                return false;
            }
            LogWarning("Skins installed: pack="+packId+" files="+files.Count+
                       " replaced="+replaced+" alreadyCurrent="+(files.Count-replaced));
            return true;
        }
        catch (Exception ex)
        {
            LogError("Skin install failed, nothing was applied: pack="+packId+" | "+ex.Message);
            return false;
        }
        finally
        {
            Discard(packId);
        }
    }

    public static void Discard(string packId)
    {
        if (string.IsNullOrEmpty(Root))
            return;
        string staging=StagingFolder(packId);
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging,true);
        }
        catch (Exception ex)
        {
            LogWarning("Could not remove skin staging folder: "+staging+" | "+ex.Message);
        }
    }

    private static string StagingFolder(string packId)
    {
        string safe=string.IsNullOrEmpty(packId) ? "unnamed" : packId;
        char[] bad=Path.GetInvalidFileNameChars();
        for (int i=0; i<bad.Length; i++)
            safe=safe.Replace(bad[i],'_');
        return Path.Combine(Path.Combine(Root,StagingFolderName),safe);
    }

    private static bool Same(string path,byte[] data)
    {
        try
        {
            byte[] onDisk=File.ReadAllBytes(path);
            if (onDisk.Length!=data.Length)
                return false;
            for (int i=0; i<data.Length; i++)
            {
                if (onDisk[i]!=data[i])
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInside(string folder,string path)
    {
        string full=Path.GetFullPath(path);
        string prefix=Path.GetFullPath(folder);
        if (!prefix.EndsWith(Path.DirectorySeparatorChar.ToString()))
            prefix+=Path.DirectorySeparatorChar;
        return full.StartsWith(prefix,StringComparison.InvariantCultureIgnoreCase);
    }

    private static string FindGameSkinFolder()
    {
        Type paths=FindType("NuclearOption.AddressablePaths");
        if (paths==null)
        {
            LogError("NuclearOption.AddressablePaths not found, skins from packs cannot be installed. Game update?");
            return "";
        }
        string value=ReadMember(paths,"AppDataSkinPath");
        if (string.IsNullOrEmpty(value))
        {
            LogError("AddressablePaths.AppDataSkinPath is empty, skins from packs cannot be installed");
            return "";
        }
        Log("Game skin folder: "+value);
        return value;
    }

    private static string ReadMember(Type type,string name)
    {
        PropertyInfo property=type.GetProperty(name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
        if (property!=null && property.CanRead)
            return property.GetValue(null,null) as string;
        FieldInfo field=type.GetField(name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
        if (field!=null)
            return field.GetValue(null) as string;
        LogError("AddressablePaths has no member named "+name);
        return "";
    }

    private static Type FindType(string fullName)
    {
        Assembly[] assemblies=AppDomain.CurrentDomain.GetAssemblies();
        for (int i=0; i<assemblies.Length; i++)
        {
            try
            {
                Type found=assemblies[i].GetType(fullName,false);
                if (found!=null)
                    return found;
            }
            catch (Exception ex)
            {
                LogSuppressed("FindType "+fullName,ex);
            }
        }
        return null;
    }
}
