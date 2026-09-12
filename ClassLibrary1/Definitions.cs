using System.Collections;
using System.Reflection;
using HarmonyLib;

partial class Loader
{
    private static readonly MethodInfo afterLoad=AccessTools.Method(typeof(Encyclopedia),"AfterLoad",new[] { typeof(Encyclopedia) });

    private static IList ListFor(Encyclopedia e,UnitDefinition def)
    {
        if(def is VehicleDefinition) return e.vehicles;
        if(def is ShipDefinition) return e.ships;
        if(def is BuildingDefinition) return e.buildings;
        if(def is AircraftDefinition) return e.aircraft;
        if(def is SceneryDefinition) return e.scenery;
        if(def is MissileDefinition) return e.missiles;
        return e.otherUnits;
    }

    private void UpdateEncyclopedia()
    {
        Encyclopedia e=Encyclopedia.i;
        foreach (UnityEngine.Object obj in runtimeObjects.Values)
        {
            if(obj is WeaponMount) e.weaponMounts.Add((WeaponMount)obj);
            else if(obj is UnitDefinition) ListFor(e,(UnitDefinition)obj).Add(obj);
        }
        afterLoad.Invoke(null,new object[] { e });
    }
}
