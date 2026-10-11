using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ForagersGarden
{
    /// <summary>
    /// Plantable mushrooms (Mushroom, Yellow mushroom, Blue mushroom), the same cycle as carrot / carrot seeds:
    /// plant a mushroom -> it grows spores; plant spores -> they grow mushrooms. Everything is cloned from vanilla
    /// prefabs at start-up: the spores from the carrot seeds, the crops from the wild mushroom pickables (without
    /// respawn), the saplings from the carrot sapling. They sit in the Cultivator's build list, grow on cultivated
    /// ground only and show up in the scarecrow's crop list (CropMap) like every other sapling.
    /// Every client and the server need the mod (same as the scarecrow): the prefabs are saved in the world.
    /// </summary>
    internal static class MushroomCrops
    {
        private sealed class Kind
        {
            public string Id;          // part of the prefab names
            public string Item;        // vanilla mushroom item prefab
            public string Pickable;    // vanilla wild pickable (visual)
            public string SporesEn, SporesDe;
            public string IconFile;    // own icon in Icons/ (no file: made from the mushroom icon)
            public Color Tint;         // colour of the spores lying on the ground
            public GameObject Spores, MushroomCrop, SporesCrop, SaplingFromSpores, SaplingForSpores;
        }

        private static readonly Kind[] Kinds =
        {
            new Kind { Id = "sac_mushroom", Item = "Mushroom", Pickable = "Pickable_Mushroom",
                SporesEn = "Mushroom spores", SporesDe = "Pilzsporen", IconFile = "spores_mushroom.png", Tint = new Color(0.96f, 0.95f, 0.9f) },
            new Kind { Id = "sac_mushroom_yellow", Item = "MushroomYellow", Pickable = "Pickable_Mushroom_yellow",
                SporesEn = "Yellow mushroom spores", SporesDe = "Gelbe Pilzsporen", IconFile = "spores_mushroom_yellow.png", Tint = new Color(1f, 0.78f, 0.3f) },
            new Kind { Id = "sac_mushroom_blue", Item = "MushroomBlue", Pickable = "Pickable_Mushroom_blue",
                SporesEn = "Blue mushroom spores", SporesDe = "Blaue Pilzsporen", IconFile = "spores_mushroom_blue.png", Tint = new Color(0.62f, 0.7f, 1f) },
        };

        private const float GrowTime = 3000f;
        private const float GrowTimeMax = 4000f;

        private static GameObject _hide;
        private static bool _done;
        private static bool _busy;
        private static readonly Dictionary<int, GameObject> ByHash = new Dictionary<int, GameObject>();

        internal static GameObject PrefabForHash(int hash)
        {
            GameObject go;
            return ByHash.TryGetValue(hash, out go) ? go : null;
        }

        // ---------------------------------------------------------------- scene

        internal static void RegisterForScene(ZNetScene scene)
        {
            if (scene == null || _busy)
                return;
            _busy = true;
            try
            {
                if (!_done)
                    Build(scene);
                else
                    NameAll(scene);
                AddItemsToDb(ObjectDB.instance);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Forager's Garden mushrooms skipped: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private static void Build(ZNetScene scene)
        {
            // Round turnip seeds look like spore balls on the ground (the carrot seeds are long).
            GameObject seeds = scene.GetPrefab("TurnipSeeds") ?? scene.GetPrefab("CarrotSeeds");
            GameObject carrotSapling = scene.GetPrefab("sapling_carrot");
            if (seeds == null || carrotSapling == null)
            {
                Plugin.Log.LogDebug("Forager's Garden mushrooms: carrot seeds / sapling not found yet.");
                return;
            }
            if (_hide == null)
            {
                _hide = new GameObject("sac_mushroom_hide");
                Object.DontDestroyOnLoad(_hide);
                _hide.SetActive(false);
            }

            for (int i = 0; i < Kinds.Length; i++)
                BuildKind(scene, Kinds[i], seeds, carrotSapling);
            _done = true;
            NameAll(scene);
            Plugin.Log.LogDebug("Forager's Garden mushrooms registered.");
        }

        private static void BuildKind(ZNetScene scene, Kind k, GameObject seeds, GameObject carrotSapling)
        {
            GameObject mushroomItem = scene.GetPrefab(k.Item);
            GameObject wild = scene.GetPrefab(k.Pickable);
            ItemDrop mushroomDrop = mushroomItem != null ? mushroomItem.GetComponent<ItemDrop>() : null;
            Pickable wildPick = wild != null ? wild.GetComponent<Pickable>() : null;
            if (mushroomDrop == null || wildPick == null)
            {
                Plugin.Log.LogDebug("Forager's Garden mushrooms: " + k.Item + " / " + k.Pickable + " not found, skipped.");
                return;
            }

            // Spores: carrot seeds with their own name and an icon made from the mushroom's.
            k.Spores = Clone(seeds, "SacSpores_" + k.Id);
            ItemDrop sporesDrop = k.Spores.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData shared = sporesDrop.m_itemData.m_shared;
            shared.m_name = Loc.T(k.SporesEn, k.SporesDe);
            shared.m_description = Loc.T(
                "Plant them on cultivated ground (Cultivator) and mushrooms grow.",
                "Auf bestelltem Boden pflanzen (Kultivator), daraus wachsen Pilze.");
            TintGroundModel(k.Spores, k.Tint);
            Sprite[] icons = mushroomDrop.m_itemData.m_shared.m_icons;
            Sprite icon = Icons.Get(k.IconFile) ?? SporeIcon(icons != null && icons.Length > 0 ? icons[0] : null);
            if (icon != null)
                shared.m_icons = new[] { icon };

            // The two crops (no respawn, the plant is removed when picked, like every field crop).
            k.MushroomCrop = ClonePickable(wild, "Pickable_" + k.Id, mushroomItem, 1);
            k.SporesCrop = ClonePickable(wild, "Pickable_" + k.Id + "_spores", k.Spores, 3);

            string mushroomName = mushroomDrop.m_itemData.m_shared.m_name;
            Sprite sporesIcon = icon != null ? icon : (icons != null && icons.Length > 0 ? icons[0] : null);
            Sprite mushroomIcon = icons != null && icons.Length > 0 ? icons[0] : null;

            // Plant spores -> mushrooms.
            k.SaplingFromSpores = CloneSapling(carrotSapling, "sapling_" + k.Id, k.MushroomCrop, wildPick,
                k.Spores, shared.m_name, sporesIcon);
            // Plant a mushroom -> spores (name starts with sapling_seed: the scarecrow marks it "for seeds").
            k.SaplingForSpores = CloneSapling(carrotSapling, "sapling_seed_" + k.Id, k.SporesCrop, wildPick,
                mushroomItem, mushroomName, mushroomIcon);
        }

        private static GameObject Clone(GameObject template, string name)
        {
            bool wasActive = template.activeSelf;
            template.SetActive(false);
            GameObject clone = Object.Instantiate(template, _hide.transform);
            template.SetActive(wasActive);
            clone.name = name;
            clone.SetActive(true); // the parent is inactive: no Awake / OnEnable runs
            return clone;
        }

        /// <summary>The spores lying on the ground get the colour of their mushroom.</summary>
        private static void TintGroundModel(GameObject item, Color tint)
        {
            foreach (Renderer r in item.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                    continue;
                Material[] mats = r.materials; // own copies: the turnip seeds stay as they are
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null)
                        continue;
                    if (m.HasProperty("_Color"))
                        m.SetColor("_Color", tint);
                    if (m.HasProperty("_EmissionColor"))
                        m.SetColor("_EmissionColor", Color.black);
                }
                r.materials = mats;
            }
        }

        private static GameObject ClonePickable(GameObject template, string name, GameObject item, int amount)
        {
            GameObject clone = Clone(template, name);
            Pickable pick = clone.GetComponent<Pickable>();
            pick.m_itemPrefab = item;
            pick.m_amount = amount;
            pick.m_respawnTimeMinutes = 0f;
            pick.m_hideWhenPicked = null; // no hide + no respawn = the plant is removed when picked
            pick.m_overrideName = "";
            return clone;
        }

        private static GameObject CloneSapling(GameObject template, string name, GameObject grown, Pickable visualFrom,
            GameObject required, string requiredName, Sprite icon)
        {
            GameObject clone = Clone(template, name);
            Plant plant = clone.GetComponent<Plant>();
            Piece piece = clone.GetComponent<Piece>();

            bool forSpores = name.StartsWith("sapling_seed");
            string title = forSpores
                ? requiredName + " " + Loc.T("(grows spores)", "(wächst zu Sporen)")
                : requiredName + " " + Loc.T("(grows mushrooms)", "(wachsen zu Pilzen)");

            plant.m_name = title;
            plant.m_grownPrefabs = new[] { grown };
            plant.m_growTime = GrowTime;
            plant.m_growTimeMax = GrowTimeMax;
            plant.m_needCultivatedGround = true;
            plant.m_biome |= Heightmap.Biome.Swamp; // the blue mushroom lives there
            SwapVisuals(plant, visualFrom != null ? visualFrom.m_hideWhenPicked : null);
            WildCrops.FitViews(plant); // the carrot stages' own scale / rotation squeezed the mushroom models

            piece.m_name = title;
            piece.m_description = Loc.T(
                "Grows on cultivated ground. Needs 1 " + requiredName + ".",
                "Wächst auf bestelltem Boden. Braucht 1 " + requiredName + ".");
            if (icon != null)
                piece.m_icon = icon;
            ItemDrop need = required != null ? required.GetComponent<ItemDrop>() : null;
            piece.m_resources = need != null
                ? new[] { new Piece.Requirement { m_resItem = need, m_amount = 1, m_recover = false } }
                : new Piece.Requirement[0];
            return clone;
        }

        /// <summary>The four growth stages show the wild mushroom (small while young, full size when grown).</summary>
        internal static void SwapVisuals(Plant plant, GameObject mushroom, float youngScale = 0.45f)
        {
            string[] fields = { "m_healthy", "m_unhealthy", "m_healthyGrown", "m_unhealthyGrown" };
            float[] scale = { youngScale, youngScale, 1f, 1f };
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = AccessTools.Field(typeof(Plant), fields[i]);
                GameObject stage = f != null ? f.GetValue(plant) as GameObject : null;
                if (stage == null)
                    continue;
                for (int c = stage.transform.childCount - 1; c >= 0; c--)
                    Object.DestroyImmediate(stage.transform.GetChild(c).gameObject);
                // The carrot stages carry their green leaves on the stage object itself (mesh + renderer): remove them too.
                foreach (Renderer r in stage.GetComponents<Renderer>())
                    Object.DestroyImmediate(r);
                foreach (MeshFilter mf in stage.GetComponents<MeshFilter>())
                    Object.DestroyImmediate(mf);
                foreach (LODGroup lod in stage.GetComponents<LODGroup>())
                    Object.DestroyImmediate(lod);
                if (mushroom == null)
                    continue;
                GameObject view = Object.Instantiate(mushroom, stage.transform, false);
                view.transform.localScale = mushroom.transform.localScale * scale[i];
                foreach (Collider col in view.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(col); // a collider here would block the plant's own grow space
                view.SetActive(true);
            }
        }

        /// <summary>Icon of the spores: the mushroom's own, washed out so it is not mistaken for it.</summary>
        private static Sprite SporeIcon(Sprite source)
        {
            if (source == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return null; // a dedicated server has no graphics and needs no icon
            try
            {
                Texture2D tex = source.texture;
                Rect r = source.textureRect;
                RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0);
                Graphics.Blit(tex, rt);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var copy = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);

                Color[] px = copy.GetPixels();
                var wash = new Color(0.86f, 0.80f, 0.62f);
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a < 0.05f)
                        continue;
                    Color c = Color.Lerp(px[i], wash, 0.5f);
                    c.a = px[i].a;
                    px[i] = c;
                }
                // a few spore dots in the lower right
                int w = copy.width, h = copy.height;
                for (int d = 0; d < 7; d++)
                {
                    int cx = (int)(w * (0.58f + 0.07f * (d % 4)));
                    int cy = (int)(h * (0.16f + 0.06f * (d / 3)));
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = cx + dx, y = cy + dy;
                            if (x >= 0 && y >= 0 && x < w && y < h)
                                px[y * w + x] = new Color(0.45f, 0.32f, 0.18f, 1f);
                        }
                    }
                }
                copy.SetPixels(px);
                copy.Apply();
                return Sprite.Create(copy, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("Spore icon: " + ex.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------- registration

        private static void NameAll(ZNetScene scene)
        {
            for (int i = 0; i < Kinds.Length; i++)
            {
                Kind k = Kinds[i];
                Name(scene, k.Spores);
                Name(scene, k.MushroomCrop);
                Name(scene, k.SporesCrop);
                Name(scene, k.SaplingFromSpores);
                Name(scene, k.SaplingForSpores);
            }
        }

        private static void Name(ZNetScene scene, GameObject prefab)
        {
            if (prefab == null)
                return;
            ByHash[prefab.name.GetStableHashCode()] = prefab;
            if (scene.m_prefabs != null && !scene.m_prefabs.Contains(prefab))
                scene.m_prefabs.Add(prefab);
            FieldInfo named = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
            var map = named != null ? named.GetValue(scene) as Dictionary<int, GameObject> : null;
            if (map != null)
                map[prefab.name.GetStableHashCode()] = prefab;
        }

        /// <summary>The spores are items: ObjectDB has to know them (inventories, drops, recipes look them up there).</summary>
        internal static void AddItemsToDb(ObjectDB db)
        {
            if (!_done || db == null || db.m_items == null || db.GetItemPrefab("Hammer") == null)
                return; // not filled yet: the CopyOtherDB patch tries again
            bool added = false;
            for (int i = 0; i < Kinds.Length; i++)
            {
                GameObject spores = Kinds[i].Spores;
                if (spores != null && !db.m_items.Contains(spores))
                {
                    db.m_items.Add(spores);
                    added = true;
                }
            }
            if (!added)
                return;
            MethodInfo update = AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");
            if (update != null)
                update.Invoke(db, null);
        }

        /// <summary>
        /// The saplings in the Cultivator's build list (like the scarecrow in the hammer's) while the extra plants are
        /// on (WildCrops.Enabled), removed when off (planted ones and spores keep working). True when the list changed.
        /// </summary>
        internal static bool TryRegisterCultivator()
        {
            if (!_done || ObjectDB.instance == null)
                return false;
            GameObject item = ObjectDB.instance.GetItemPrefab("Cultivator");
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            PieceTable table = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                ? drop.m_itemData.m_shared.m_buildPieces : null;
            if (table == null || table.m_pieces == null)
                return false;
            bool on = WildCrops.Enabled;
            bool changed = false;
            for (int i = 0; i < Kinds.Length; i++)
            {
                changed |= Set(table, Kinds[i].SaplingFromSpores, on);
                changed |= Set(table, Kinds[i].SaplingForSpores, on);
            }
            return changed;
        }

        private static bool Set(PieceTable table, GameObject sapling, bool on)
        {
            if (sapling == null)
                return false;
            if (!on)
                return table.m_pieces.Remove(sapling);
            if (table.m_pieces.Contains(sapling))
                return false;
            table.m_pieces.Add(sapling);
            return true;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneMushroomPatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            MushroomCrops.RegisterForScene(__instance);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDbMushroomPatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            MushroomCrops.AddItemsToDb(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PlayerMushroomTablePatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null || !__instance.IsOwner())
                return;
            if (ZNetScene.instance != null)
                MushroomCrops.RegisterForScene(ZNetScene.instance);
            MushroomCrops.AddItemsToDb(ObjectDB.instance);
            // Valheim decided which pieces the player knows when the player was created, before these were in the list.
            if (MushroomCrops.TryRegisterCultivator())
                QueenHives.RefreshKnown(__instance);
        }
    }
}
