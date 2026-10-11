using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ForagersGarden
{
    /// <summary>
    /// Forager's Garden: plant what you would forage (berry bushes, wild herbs, mushrooms) with the Cultivator, and
    /// Ice / Royal Jelly / Crystal beehives. This content was part of StoreAndCraft up to 1.5.x; the prefab names are
    /// the same, so pieces and items from those versions keep working.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    // StoreAndCraft up to 1.5.x had this content built in: it loads first so the check below sees its version.
    [BepInDependency(StoreAndCraftGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.morda.foragersgarden";
        public const string ModName = "Forager's Garden";
        public const string ModVersion = "1.0.0";
        public const string ModAuthor = "Morda";
        private const string StoreAndCraftGuid = "com.morda.storeandcraft";

        internal static ManualLogSource Log { get; private set; }
        internal static ConfigEntry<bool> Beehives { get; private set; }
        internal static ConfigEntry<bool> CultivatorPlants { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            // Both would register the same pieces and items: never run next to a StoreAndCraft that still has them.
            System.Version old = OldStoreAndCraft();
            if (old != null)
            {
                Logger.LogError("StoreAndCraft " + old + " still contains the beehives and plants of " + ModName
                    + ". Update StoreAndCraft to 1.6.0 or newer. " + ModName + " stays off.");
                return;
            }

            Beehives = Config.Bind("1 - General", "Beehives", true,
                "Ice, Royal Jelly and Crystal beehive in the hammer and their queen bee recipes at the workbench. Off = not offered any more (hives already built keep working). The vanilla beehive is untouched.");
            CultivatorPlants = Config.Bind("1 - General", "CultivatorPlants", true,
                "Berry bushes, wild herbs, mushrooms and spores in the Cultivator. Off = not offered any more (plants already planted keep growing). Vanilla crops are untouched.");
            Config.SettingChanged += OnSettingChanged;

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo(ModName + " v" + ModVersion + " by " + ModAuthor + " loaded.");
        }

        /// <summary>The version of a loaded StoreAndCraft older than 1.6.0 (null when none).</summary>
        private static System.Version OldStoreAndCraft()
        {
            PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(StoreAndCraftGuid, out info) || info == null || info.Metadata == null)
                return null;
            System.Version v = info.Metadata.Version;
            return v != null && v < new System.Version(1, 6, 0) ? v : null;
        }

        /// <summary>The switches: into / out of the build menus at once.</summary>
        private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (e == null)
                return;
            if (e.ChangedSetting == Beehives)
                QueenHives.ApplySetting();
            if (e.ChangedSetting == CultivatorPlants)
            {
                bool changed = WildCrops.TryRegisterCultivator() | MushroomCrops.TryRegisterCultivator();
                if (changed && Player.m_localPlayer != null)
                    QueenHives.RefreshKnown(Player.m_localPlayer);
            }
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            _harmony?.UnpatchSelf();
        }
    }
}
