# Правки для NobndlBuilderWindow.cs

Четыре вставки. Все по образцу пилонных патчей, которые уже там есть.

## 1. Поле в манифесте — строка ~3080

```csharp
public AircraftPylonPatchManifestEntry[] aircraftPylonPatches;
public NobndlListPatchManifestEntry[] listPatches;          // <-- добавить
```

## 2. Новая запись манифеста — рядом с `AircraftPylonPatchManifestEntry`, строка ~3140

```csharp
[Serializable]
private sealed class NobndlListPatchManifestEntry
{
    public string targetTypeName;
    public string targetAssemblyName;
    public string targetName;
    public string listName;
    public string valueName;
}
```

## 3. Сбор ассетов — рядом с `FindPylonPatchLists`, строка ~684

```csharp
private bool IsListPatchListAsset(string assetPath)
{
    return AssetDatabase.LoadAssetAtPath<NobndlListPatchList>(assetPath) != null;
}

private List<NobndlListPatchList> FindListPatchLists(List<string> sourceFilesAbsolute)
{
    List<NobndlListPatchList> result = new List<NobndlListPatchList>();

    foreach (string absolute in sourceFilesAbsolute)
    {
        string assetPath = ToAssetPath(absolute);

        if (string.IsNullOrWhiteSpace(assetPath))
        {
            continue;
        }

        NobndlListPatchList patchList = AssetDatabase.LoadAssetAtPath<NobndlListPatchList>(assetPath);

        if (patchList != null && !result.Contains(patchList))
        {
            result.Add(patchList);
        }
    }

    return result;
}

private NobndlListPatchManifestEntry[] BuildListPatchManifestEntries(List<NobndlListPatchList> patchLists)
{
    List<NobndlListPatchManifestEntry> result = new List<NobndlListPatchManifestEntry>();

    foreach (NobndlListPatchList patchList in patchLists)
    {
        if (patchList == null || patchList.patches == null)
        {
            continue;
        }

        foreach (NobndlListPatch patch in patchList.patches)
        {
            if (patch == null)
            {
                continue;
            }

            string value = patch.ResolvedValueName();

            if (string.IsNullOrWhiteSpace(patch.targetType))
            {
                throw new Exception("List patch has no vanilla type selected.");
            }

            if (string.IsNullOrWhiteSpace(patch.listName))
            {
                throw new Exception("List patch on " + patch.targetType + " has no list field selected.");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new Exception("List patch on " + patch.targetType + "." + patch.listName + " has nothing to add.");
            }

            result.Add(new NobndlListPatchManifestEntry
            {
                targetTypeName = patch.targetType.Trim(),
                targetAssemblyName = string.IsNullOrWhiteSpace(patch.targetAssembly)
                    ? "Assembly-CSharp"
                    : patch.targetAssembly.Trim(),
                targetName = (patch.targetName ?? "").Trim(),
                listName = patch.listName.Trim(),
                valueName = value
            });
        }
    }

    return result.ToArray();
}
```

Замени `ToAssetPath` на то, что используется в `FindPylonPatchLists` для того же преобразования — я его точного имени не вижу.

## 4. Вызовы в Build — рядом со строками 223, 281, 293, 365

```csharp
// рядом со строкой 223
List<NobndlListPatchList> listPatchLists = FindListPatchLists(sourceFilesAbsolute);

// рядом со строкой 281
NobndlListPatchManifestEntry[] listPatches = BuildListPatchManifestEntries(listPatchLists);

// в вызов BuildManifest (строки 293 и 379) добавить аргумент listPatches

// в лог рядом со строкой 365
foreach (NobndlListPatchManifestEntry patch in listPatches)
{
    log.AppendLine("List patch: " + patch.targetTypeName + "." + patch.listName + " += " + patch.valueName);
}
```

И там же, где ассеты отфильтровываются от попадания в бандл (строки 241, 256, 715, 1285), добавить `!IsListPatchListAsset(assetPath)` рядом с существующим `!IsPylonPatchListAsset(assetPath)` — ассет патчей нужен билдеру, а в бандл его класть незачем.

## 5. BuildManifest — строка ~2582

```csharp
AircraftPylonPatchManifestEntry[] pylonPatches,
NobndlListPatchManifestEntry[] listPatches)      // <-- параметр
...
manifest.aircraftPylonPatches = pylonPatches ?? Array.Empty<AircraftPylonPatchManifestEntry>();
manifest.listPatches = listPatches ?? Array.Empty<NobndlListPatchManifestEntry>();   // <-- строка
```
