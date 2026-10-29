using HarmonyLib;
using UnityEngine;

namespace ForagersGarden
{
    /// <summary>
    /// Our pieces / items for ZNetScene.GetPrefab(hash) when vanilla does not know them (one patch for all of them:
    /// GetPrefab runs for every object that is loaded). Only fills a null result.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.GetPrefab), typeof(int))]
    internal static class ZNetSceneGetPrefabPatch
    {
        private static void Postfix(int hash, ref GameObject __result)
        {
            if (__result != null)
                return;
            GameObject found = MushroomCrops.PrefabForHash(hash);
            if (found == null)
                found = WildCrops.PrefabForHash(hash);
            if (found == null)
                found = QueenHives.PrefabForHash(hash);
            if (found == null)
                found = TreeDecor.PrefabForHash(hash);
            if (found != null)
                __result = found;
        }
    }
}
