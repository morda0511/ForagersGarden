using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ForagersGarden
{
    /// <summary>
    /// Wild plants you can plant: berry bushes (Raspberry, Blueberry, Cloudberry) and wild herbs (Dandelion, Thistle,
    /// Fiddlehead, Smoke puff). The grown plant is a copy of the wild one (it keeps its respawn, berries grow back),
    /// the sapling sits in the Cultivator's build list and costs a few of what the plant gives. Because it is a normal
    /// Cultivator sapling with a Pickable as grown prefab, CropMap picks it up and the scarecrow works it like any field
    /// crop. Same registration pattern as MushroomCrops (clones in ZNetScene, Cultivator list), but no new items.
    /// </summary>
    internal static class WildCrops
    {
        // The sapling already looks like the bush and only has to turn into it: the long wait is the berries growing
        // back (the wild plant's own respawn). Over 10 s so the plant's own room / sun checks still count.
        private const float GrowTime = 12f;
        private const float GrowTimeMax = 20f;

        private sealed class Kind
        {
            public string Id;       // part of the prefab names
            public string Wild;     // vanilla prefab with the Pickable
            public bool Bush;       // "(bush)" in the name, else "(plant)"
            public float Radius;    // free space the plant needs
            public int Cost = 5;    // how many of the plant's own product one sapling costs
            public int Yield;       // what one harvest gives (0 = what the wild plant gives)
            public GameObject Crop, Sapling;
        }

        private static readonly Kind[] Kinds =
        {
            new Kind { Id = "raspberry", Wild = "RaspberryBush", Bush = true, Radius = 0.8f },
            new Kind { Id = "blueberry", Wild = "BlueberryBush", Bush = true, Radius = 0.8f },
            new Kind { Id = "cloudberry", Wild = "CloudberryBush", Bush = true, Radius = 0.8f },
            // Herbs: 1 plants it, a harvest gives 3 (the wild ones give 1, which would never pay back the sapling).
            new Kind { Id = "dandelion", Wild = "Pickable_Dandelion", Radius = 0.4f, Cost = 1, Yield = 3 },
            new Kind { Id = "thistle", Wild = "Pickable_Thistle", Radius = 0.4f, Cost = 1, Yield = 3 },
            new Kind { Id = "fiddlehead", Wild = "Pickable_Fiddlehead", Radius = 0.4f, Cost = 1, Yield = 3 },
            new Kind { Id = "smokepuff", Wild = "Pickable_SmokePuff", Radius = 0.4f, Cost = 1, Yield = 3 },
        };

        /// <summary>
        /// Saplings whose model has a part that is hidden while the plant waits to regrow (the berries of a bush), by
        /// sapling name: the name of that part. The placement ghost shows it, so you see the grown plant you are building.
        /// </summary>
        internal static readonly Dictionary<string, string> GhostReveal = new Dictionary<string, string>();

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
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Forager's Garden wild crops skipped: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private static void Build(ZNetScene scene)
        {
            GameObject carrotSapling = scene.GetPrefab("sapling_carrot");
            if (carrotSapling == null)
            {
                Plugin.Log.LogDebug("Forager's Garden wild crops: carrot sapling not found yet.");
                return;
            }
            if (_hide == null)
            {
                _hide = new GameObject("sac_wildcrop_hide");
                Object.DontDestroyOnLoad(_hide);
                _hide.SetActive(false);
            }
            for (int i = 0; i < Kinds.Length; i++)
            {
                try
                {
                    BuildKind(scene, Kinds[i], carrotSapling);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("Forager's Garden wild crop " + Kinds[i].Id + " skipped: " + ex.Message);
                }
            }
            _done = true;
            NameAll(scene);
            Plugin.Log.LogDebug("Forager's Garden wild crops registered.");
        }

        private static void BuildKind(ZNetScene scene, Kind k, GameObject carrotSapling)
        {
            GameObject wild = scene.GetPrefab(k.Wild);
            Pickable pick = wild != null ? wild.GetComponentInChildren<Pickable>(true) : null;
            ItemDrop item = pick != null && pick.m_itemPrefab != null ? pick.m_itemPrefab.GetComponent<ItemDrop>() : null;
            if (item == null || wild.GetComponent<ZNetView>() == null)
            {
                Plugin.Log.LogDebug("Forager's Garden wild crops: " + k.Wild + " has no pickable / item, skipped.");
                return;
            }

            // The grown plant: a copy of the wild one, saved with the world (the wild ones come from the world generator).
            k.Crop = Clone(wild, "sac_wild_" + k.Id);
            k.Crop.GetComponent<ZNetView>().m_persistent = true;
            // A plant that grows its harvest back starts as if it had just been picked: no berries yet, the respawn
            // time starts now. Only where something stays to look at (a bush without its berries): a plant that IS the
            // picked thing (a smoke puff) would be invisible until the respawn. (One that never respawns is ripe at
            // once: it would stay empty for ever otherwise.)
            Pickable grownPick = k.Crop.GetComponentInChildren<Pickable>(true);
            if (grownPick != null && k.Yield > 0)
                grownPick.m_amount = k.Yield;
            if (grownPick != null && grownPick.m_respawnTimeMinutes > 0f && ModelStays(k.Crop, grownPick))
            {
                grownPick.m_defaultPicked = true;
                grownPick.m_respawnTimeInitMin = 0f;
                grownPick.m_respawnTimeInitMax = 0f;
            }

            // The sapling: carrot sapling that grows into it. Bushes and herbs have different name parts so the
            // crop list can tell them apart (CropMap.DisplayName).
            string kind = k.Bush ? "bush" : "wild";
            k.Sapling = CloneSapling(carrotSapling, "sapling_sac_" + kind + "_" + k.Id, k, item, wild);
            if (grownPick != null && grownPick.m_respawnTimeMinutes > 0f && ModelStays(k.Crop, grownPick))
                GhostReveal[k.Sapling.name] = grownPick.m_hideWhenPicked.name;
        }

        /// <summary>
        /// True when something of the plant is still visible with the part that disappears on picking switched off
        /// (the bush without its berries). False when picking hides the whole plant (smoke puff, mushrooms).
        /// </summary>
        private static bool ModelStays(GameObject root, Pickable pick)
        {
            GameObject hide = pick != null ? pick.m_hideWhenPicked : null;
            if (hide == null)
                return false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || !r.enabled || !r.gameObject.activeSelf)
                    continue;
                if (!r.transform.IsChildOf(hide.transform))
                    return true;
            }
            return false;
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

        private static GameObject CloneSapling(GameObject template, string name, Kind k, ItemDrop item, GameObject wild)
        {
            GameObject clone = Clone(template, name);
            Plant plant = clone.GetComponent<Plant>();
            Piece piece = clone.GetComponent<Piece>();

            string itemName = item.m_itemData.m_shared.m_name; // a $token: Valheim translates it inside the text
            string title = itemName + " " + (k.Bush ? Loc.T("(bush)", "(Strauch)") : Loc.T("(plant)", "(Pflanze)"));

            plant.m_name = title;
            plant.m_grownPrefabs = new[] { k.Crop };
            plant.m_growTime = GrowTime;
            plant.m_growTimeMax = GrowTimeMax;
            plant.m_growRadius = k.Radius;
            plant.m_needCultivatedGround = true;
            plant.m_biome = Heightmap.Biome.All;
            plant.m_minScale = 1f;
            plant.m_maxScale = 1f;

            // The young plant looks exactly like the grown one (full size, no berries): a planted bush is a bush right away.
            GameObject view = StaticView(wild);
            MushroomCrops.SwapVisuals(plant, view, 1f);
            FitViews(plant);
            Object.Destroy(view);

            piece.m_name = title;
            piece.m_description = Loc.T(
                "Grows on cultivated ground and keeps growing " + itemName + ". Needs " + k.Cost + " " + itemName + ".",
                "Wächst auf bestelltem Boden und trägt immer wieder " + itemName + ". Braucht " + k.Cost + " " + itemName + ".");
            Sprite[] icons = item.m_itemData.m_shared.m_icons;
            if (icons != null && icons.Length > 0 && icons[0] != null)
                piece.m_icon = icons[0];
            piece.m_resources = new[] { new Piece.Requirement { m_resItem = item, m_amount = k.Cost, m_recover = false } };
            return clone;
        }

        private static readonly string[] StageFields = { "m_healthy", "m_unhealthy", "m_healthyGrown", "m_unhealthyGrown" };

        /// <summary>
        /// The carrot's stage objects carry an uneven scale (and rotation) of their own, because the carrot mesh sat on
        /// them. A plant model put below one is squeezed with it. Cancel the stage's scale / rotation so the model keeps
        /// the world size of the wild plant.
        /// </summary>
        internal static void FitViews(Plant plant)
        {
            for (int i = 0; i < StageFields.Length; i++)
            {
                FieldInfo field = AccessTools.Field(typeof(Plant), StageFields[i]);
                GameObject stage = field != null ? field.GetValue(plant) as GameObject : null;
                if (stage == null || stage.transform.childCount == 0)
                    continue;
                Transform view = stage.transform.GetChild(stage.transform.childCount - 1); // the one SwapVisuals added
                Vector3 ls = stage.transform.lossyScale;
                Vector3 inverse = new Vector3(Inverse(ls.x), Inverse(ls.y), Inverse(ls.z));
                Quaternion undo = Quaternion.Inverse(stage.transform.localRotation);
                // What SwapVisuals set (size of the wild model, young / grown factor, offset) was meant for a parent
                // without a scale of its own: keep it, only take the stage's scale and rotation out.
                view.localScale = Vector3.Scale(view.localScale, inverse);
                view.localPosition = Vector3.Scale(undo * view.localPosition, inverse);
                view.localRotation = undo * view.localRotation;
            }
        }

        private static float Inverse(float v)
        {
            return Mathf.Abs(v) > 0.0001f ? 1f / v : 1f;
        }

        /// <summary>
        /// A copy of the wild plant with everything that makes it a real object removed: models only. A plant that
        /// grows its harvest back is shown as just picked (no berries), like the plant it turns into.
        /// </summary>
        private static GameObject StaticView(GameObject wild)
        {
            GameObject view = Clone(wild, "sac_wild_view");
            // The copy keeps the wild prefab's own position (about 23 m up for these prefabs): every stage model made
            // from it would hang in the air above the sapling. It stands at the sapling's root, like the wild plant's pivot.
            view.transform.localPosition = Vector3.zero;
            view.transform.localRotation = Quaternion.identity;
            Pickable viewPick = view.GetComponentInChildren<Pickable>(true);
            if (viewPick != null && viewPick.m_respawnTimeMinutes > 0f && ModelStays(view, viewPick))
                viewPick.m_hideWhenPicked.SetActive(false);
            foreach (MonoBehaviour mb in view.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && !(mb is ZNetView))
                    Object.DestroyImmediate(mb);
            }
            ZNetView nv = view.GetComponent<ZNetView>();
            if (nv != null)
                Object.DestroyImmediate(nv);
            foreach (Collider c in view.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
            foreach (Rigidbody rb in view.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(rb);
            return view;
        }

        // ---------------------------------------------------------------- registration

        private static void NameAll(ZNetScene scene)
        {
            for (int i = 0; i < Kinds.Length; i++)
            {
                Name(scene, Kinds[i].Crop);
                Name(scene, Kinds[i].Sapling);
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

        /// <summary>F10 "Extra Cultivator plants" (ExtraCultivatorPlants), shared with MushroomCrops.</summary>
        internal static bool Enabled
        {
            get { return Plugin.CultivatorPlants == null || Plugin.CultivatorPlants.Value; }
        }

        /// <summary>
        /// The saplings in the Cultivator's build list (like the mushrooms) while the extra plants are on, removed
        /// when off (planted ones keep growing: their prefabs stay registered). True when the list changed.
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
            bool on = Enabled;
            bool changed = false;
            for (int i = 0; i < Kinds.Length; i++)
            {
                GameObject sapling = Kinds[i].Sapling;
                if (sapling == null)
                    continue;
                if (on && !table.m_pieces.Contains(sapling))
                {
                    table.m_pieces.Add(sapling);
                    changed = true;
                }
                else if (!on && table.m_pieces.Remove(sapling))
                {
                    changed = true;
                }
            }
            return changed;
        }
    }

    /// <summary>
    /// The placement ghost of a mushroom or wild-plant sapling shows the grown plant (berries on, mushrooms full size),
    /// not the young stage a planted sapling shows. Valheim builds the ghost from the prefab (SetupPlacementGhost);
    /// this changes that copy only, placed saplings are not touched.
    /// </summary>
    [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
    internal static class SaplingGhostGrownPatch
    {
        private static readonly FieldInfo GhostField = AccessTools.Field(typeof(Player), "m_placementGhost");

        private static void Postfix(Player __instance)
        {
            GameObject ghost = GhostField != null ? GhostField.GetValue(__instance) as GameObject : null;
            if (ghost == null || !(ghost.name.StartsWith("sapling_sac_") || ghost.name.StartsWith("sapling_seed_sac_")))
                return;
            Transform young = ghost.transform.Find("healthy");
            Transform grown = ghost.transform.Find("healthy_grown");
            if (young == null || grown == null)
                return;
            young.gameObject.SetActive(false);
            grown.gameObject.SetActive(true);
            string reveal;
            WildCrops.GhostReveal.TryGetValue(ghost.name, out reveal);
            // Valheim set up the layer of the active parts only: the stage that was off gets it here.
            foreach (Transform t in grown.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = ghost.layer;
                if (reveal != null && t.name == reveal)
                    t.gameObject.SetActive(true);
            }
            foreach (Renderer r in grown.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneWildCropsPatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            WildCrops.RegisterForScene(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PlayerWildCropsTablePatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null || !__instance.IsOwner())
                return;
            if (ZNetScene.instance != null)
                WildCrops.RegisterForScene(ZNetScene.instance);
            // Valheim decided which pieces the player knows when the player was created, before these were in the list:
            // let it check again, or they show up only after the next new item / station.
            if (WildCrops.TryRegisterCultivator())
                QueenHives.RefreshKnown(__instance);
        }
    }
}
