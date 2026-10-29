using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ForagersGarden
{
    /// <summary>
    /// Decoration trees for the Cultivator: every tree species in three sizes (small, half-grown, grown) as build
    /// pieces in their own Cultivator category. They are plain decoration: they never grow, cannot be chopped and give
    /// no wood; the hammer removes them and returns the materials. A piece is a copy of a Cultivator sapling shell
    /// (Piece + ZNetView, so it is built and saved like a sapling) with the model of the real tree below it, scaled.
    /// </summary>
    internal static class TreeDecor
    {
        private sealed class Kind
        {
            public string Id;           // part of the prefab names
            public string Tree;         // vanilla tree prefab (the model)
            public string Seed;         // what it costs (the species' seed / cone), Wood when that item is missing
            public string Title;        // shown in the build menu
            public int CostFactor = 1;  // trees without a seed of their own cost wood, more of it
            public GameObject[] Pieces = new GameObject[3];
        }

        private static readonly string[] SizeIds = { "small", "half", "full" };
        private static readonly string[] SizeNames = { "small", "half-grown", "grown" };
        // Share of the real tree's size. The real trees are 20 - 40 m tall: small is a young tree, half is a stand-in.
        private static readonly float[] SizeScale = { 0.2f, 0.5f, 1f };
        private static readonly int[] SizeCost = { 2, 4, 6 };

        private static readonly Kind[] Kinds =
        {
            new Kind { Id = "beech", Tree = "Beech1", Seed = "BeechSeeds", Title = "Beech" },
            new Kind { Id = "birch", Tree = "Birch1", Seed = "BirchSeeds", Title = "Birch" },
            new Kind { Id = "birch_autumn", Tree = "Birch1_aut", Seed = "BirchSeeds", Title = "Autumn birch" },
            new Kind { Id = "oak", Tree = "Oak1", Seed = "Acorn", Title = "Oak" },
            new Kind { Id = "fir", Tree = "FirTree", Seed = "FirCone", Title = "Fir" },
            new Kind { Id = "fir_big", Tree = "FirTree_big", Seed = "FirConeFrost", Title = "Big fir" },
            new Kind { Id = "pine", Tree = "Pinetree_01", Seed = "PineCone", Title = "Pine" },
            new Kind { Id = "snow_pine", Tree = "Pinetree_Snow", Seed = "PineCone", Title = "Snow pine" },
            new Kind { Id = "snow_pine_dead", Tree = "Pinetree_Snow_dead", Seed = "PineCone", Title = "Dead snow pine" },
            new Kind { Id = "snow_fir", Tree = "SnowFirTree", Seed = "FirConeFrost", Title = "Snow fir" },
            new Kind { Id = "snow_fir_wide", Tree = "SnowFirTree 2", Seed = "FirConeFrost", Title = "Wide snow fir" },
            new Kind { Id = "ash1", Tree = "AshlandsTree1", Seed = "Wood", Title = "Ashlands tree 1", CostFactor = 5 },
            new Kind { Id = "ash3", Tree = "AshlandsTree3", Seed = "Wood", Title = "Ashlands tree 2", CostFactor = 5 },
            new Kind { Id = "ash4", Tree = "AshlandsTree4", Seed = "Wood", Title = "Ashlands tree 3", CostFactor = 5 },
            new Kind { Id = "ash5", Tree = "AshlandsTree5", Seed = "Wood", Title = "Ashlands tree 4", CostFactor = 5 },
            new Kind { Id = "ash6", Tree = "AshlandsTree6", Seed = "Wood", Title = "Ashlands giant tree", CostFactor = 5 },
        };

        private static GameObject _hide;
        private static bool _done;
        private static bool _busy;
        private static bool _menuSaved;
        private static bool _menuOriginal;
        private static readonly Dictionary<int, GameObject> ByHash = new Dictionary<int, GameObject>();

        internal static bool Enabled
        {
            get { return Plugin.DecorTrees == null || Plugin.DecorTrees.Value; }
        }

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
                Plugin.Log.LogWarning("Forager's Garden decoration trees skipped: " + ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private static void Build(ZNetScene scene)
        {
            GameObject shell = scene.GetPrefab("sapling_carrot");
            if (shell == null || ObjectDB.instance == null)
            {
                Plugin.Log.LogDebug("Forager's Garden decoration trees: sapling shell / items not found yet.");
                return;
            }
            if (_hide == null)
            {
                _hide = new GameObject("sac_decor_hide");
                Object.DontDestroyOnLoad(_hide);
                _hide.SetActive(false);
            }
            for (int i = 0; i < Kinds.Length; i++)
            {
                try
                {
                    BuildKind(scene, Kinds[i], shell);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("Forager's Garden decoration tree " + Kinds[i].Id + " skipped: " + ex.Message);
                }
            }
            _done = true;
            NameAll(scene);
            Plugin.Log.LogDebug("Forager's Garden decoration trees registered.");
        }

        private static void BuildKind(ZNetScene scene, Kind k, GameObject shellTemplate)
        {
            GameObject tree = scene.GetPrefab(k.Tree);
            if (tree == null)
            {
                Plugin.Log.LogDebug("Forager's Garden decoration trees: " + k.Tree + " not found, skipped.");
                return;
            }
            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(k.Seed);
            int factor = k.CostFactor;
            if (itemPrefab == null)
            {
                itemPrefab = ObjectDB.instance.GetItemPrefab("Wood");
                factor = System.Math.Max(factor, 5);
            }
            ItemDrop item = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (item == null)
            {
                Plugin.Log.LogDebug("Forager's Garden decoration trees: no cost item for " + k.Id + ", skipped.");
                return;
            }
            string itemName = item.m_itemData.m_shared.m_name; // a $token: Valheim translates it inside the text

            for (int s = 0; s < SizeIds.Length; s++)
            {
                try
                {
                    k.Pieces[s] = BuildPiece(shellTemplate, tree, k, s, item, itemName, SizeCost[s] * factor);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("Forager's Garden decoration tree " + k.Id + " (" + SizeIds[s] + ") skipped: " + ex.Message);
                }
            }
        }

        private static GameObject BuildPiece(GameObject shellTemplate, GameObject tree, Kind k, int size, ItemDrop item, string itemName, int cost)
        {
            GameObject shell = Clone(shellTemplate, "sac_decor_" + k.Id + "_" + SizeIds[size]);

            // The sapling's own behaviour and look go: no growing, no chopping, no sapling model.
            foreach (Component c in shell.GetComponents<Component>())
            {
                if (c is Plant || c is Destructible || c is StaticPhysics || c is Collider)
                    Object.DestroyImmediate(c);
            }
            for (int i = shell.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(shell.transform.GetChild(i).gameObject);

            GameObject model = Clone(tree, "model");
            StripToModel(model);
            model.transform.SetParent(shell.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = model.transform.localScale * SizeScale[size];
            int layer = LayerMask.NameToLayer("piece");
            shell.layer = layer >= 0 ? layer : shell.layer;

            ZNetView view = shell.GetComponent<ZNetView>();
            if (view != null)
                view.m_persistent = true;

            string title = Loc.T(k.Title + " (" + SizeNames[size] + ")", k.Title + " (" + SizeNames[size] + ")");
            Piece piece = shell.GetComponent<Piece>();
            piece.m_name = title;
            piece.m_description = Loc.T(
                "Decoration: does not grow and gives no wood. Remove it with the hammer to get the materials back. Needs " + cost + " " + itemName + ".",
                "Dekoration: wächst nicht und gibt kein Holz. Mit dem Hammer entfernen, um das Material zurückzubekommen. Braucht " + cost + " " + itemName + ".");
            // The icon is the tree itself (all sizes in the same frame, so a small tree looks small); the seed's icon
            // when it cannot be rendered (no graphics, dedicated server).
            Sprite rendered = RenderIcon(model, SizeScale[size], k.Id + "_" + SizeIds[size]);
            Sprite[] icons = item.m_itemData.m_shared.m_icons;
            if (rendered != null)
                piece.m_icon = rendered;
            else if (icons != null && icons.Length > 0 && icons[0] != null)
                piece.m_icon = icons[0];
            // Valheim 1.0 build menu: the tag buttons list the usage tags of the pieces (the Cultivator hides them,
            // TryRegisterCultivator shows them). The legacy category stays Misc like the other Cultivator pieces.
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_usage = Piece.UsageTagFlags.Decor;
            piece.m_resources = new[] { new Piece.Requirement { m_resItem = item, m_amount = cost, m_recover = true } };
            piece.m_groundOnly = true;
            piece.m_cultivatedGroundOnly = false;
            piece.m_noInWater = true;
            piece.m_canBeRemoved = true;
            return shell;
        }

        /// <summary>
        /// The tree as a model only: no TreeBase / HoverText / physics (nothing to chop, no logs), no view blocker.
        /// The solid parts (trunk, mesh colliders) stay and sit on the "piece" layer so they block and can be hovered.
        /// </summary>
        private static void StripToModel(GameObject model)
        {
            foreach (MonoBehaviour mb in model.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null)
                    Object.DestroyImmediate(mb);
            }
            ZNetView nv = model.GetComponent<ZNetView>();
            if (nv != null)
                Object.DestroyImmediate(nv);
            foreach (Rigidbody rb in model.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(rb);
            Transform blocker = model.transform.Find("viewblock");
            if (blocker != null)
                Object.DestroyImmediate(blocker.gameObject);
            int layer = LayerMask.NameToLayer("piece");
            if (layer >= 0)
            {
                foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
                {
                    if (c != null)
                        c.gameObject.layer = layer;
                }
            }
        }

        private const int IconSize = 128;

        /// <summary>
        /// A picture of the tree: a copy far away from the world, one frame with an own camera / light, read into a
        /// sprite. The frame is the one of the grown tree (scale 1), so the small trees are small in it.
        /// </summary>
        private static Sprite RenderIcon(GameObject model, float sizeScale, string debugName)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return null;
            GameObject copy = null, camObj = null, lightObj = null;
            RenderTexture rt = null;
            bool fog = RenderSettings.fog;
            UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientLight = RenderSettings.ambientLight;
            try
            {
                const int layer = 30;
                copy = Object.Instantiate(model, new Vector3(20000f, 20000f, 20000f), Quaternion.identity);
                copy.SetActive(true);
                foreach (ParticleSystem ps in copy.GetComponentsInChildren<ParticleSystem>(true))
                    Object.DestroyImmediate(ps.gameObject);
                foreach (Transform tr in copy.GetComponentsInChildren<Transform>(true))
                    tr.gameObject.layer = layer;
                foreach (LODGroup lod in copy.GetComponentsInChildren<LODGroup>(true))
                    lod.ForceLOD(0);

                // Bounds of the grown tree (scale 1) around the same base point.
                Vector3 baseAt = copy.transform.position;
                bool any = false;
                Bounds bounds = new Bounds();
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(false))
                {
                    if (r is ParticleSystemRenderer)
                        continue;
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                if (!any)
                    return null;
                float inv = 1f / Mathf.Max(0.01f, sizeScale);
                Vector3 fullSize = bounds.size * inv;
                Vector3 fullCenter = baseAt + (bounds.center - baseAt) * inv;
                float half = Mathf.Max(fullSize.x, fullSize.y, fullSize.z) * 0.5f * 1.08f;

                camObj = new GameObject("sac_decor_icon_camera");
                Camera cam = camObj.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << layer;
                cam.orthographic = true;
                cam.orthographicSize = half;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = half * 6f + fullSize.z;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.transform.position = fullCenter + new Vector3(0f, 0f, -(half * 3f + fullSize.z * 0.5f));
                cam.transform.rotation = Quaternion.identity;

                lightObj = new GameObject("sac_decor_icon_light");
                Light light = lightObj.AddComponent<Light>();
                light.type = LightType.Directional;
                light.cullingMask = 1 << layer;
                light.intensity = 1.15f;
                light.shadows = LightShadows.None;
                light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);

                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);

                rt = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
                // Cut-out shaders can leave the alpha of a drawn pixel at 0: anything coloured is opaque.
                Color[] px = tex.GetPixels();
                int drawn = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    Color c = px[i];
                    if (c.a < 0.99f && c.r + c.g + c.b > 0.03f)
                        c.a = 1f;
                    if (c.a > 0.5f)
                        drawn++;
                    px[i] = c;
                }
                tex.SetPixels(px);
                tex.Apply();
                RenderTexture.active = previous;
                if (drawn < IconSize)
                {
                    Object.Destroy(tex);
                    return null; // nothing was drawn
                }
                return Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("Forager's Garden decoration tree icon " + debugName + ": " + ex.Message);
                return null;
            }
            finally
            {
                RenderSettings.fog = fog;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                if (RenderTexture.active == rt)
                    RenderTexture.active = null;
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
                if (copy != null) Object.Destroy(copy);
                if (camObj != null) Object.Destroy(camObj);
                if (lightObj != null) Object.Destroy(lightObj);
            }
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

        // ---------------------------------------------------------------- registration

        private static void NameAll(ZNetScene scene)
        {
            for (int i = 0; i < Kinds.Length; i++)
            {
                for (int s = 0; s < Kinds[i].Pieces.Length; s++)
                    Name(scene, Kinds[i].Pieces[s]);
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

        /// <summary>
        /// The trees in the Cultivator's build list, under their own category, while the switch is on; removed (and the
        /// category with them) when off. Built trees stay: their prefabs stay registered. True when the list changed.
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
                for (int s = 0; s < Kinds[i].Pieces.Length; s++)
                {
                    GameObject piece = Kinds[i].Pieces[s];
                    if (piece == null)
                        continue;
                    if (on && !table.m_pieces.Contains(piece))
                    {
                        table.m_pieces.Add(piece);
                        changed = true;
                    }
                    else if (!on && table.m_pieces.Remove(piece))
                    {
                        changed = true;
                    }
                }
            }
            changed |= SetAdvancedMenu(table, on);
            return changed;
        }

        /// <summary>
        /// Valheim 1.0's build menu shows its tag buttons (Decor ...) only for tools that do not hide the advanced menu,
        /// and the Cultivator hides it. With the trees on it is shown, so "Decor" is a button like in the hammer's menu;
        /// without them the Cultivator is as vanilla again.
        /// </summary>
        private static bool SetAdvancedMenu(PieceTable table, bool on)
        {
            if (!_menuSaved)
            {
                _menuOriginal = table.m_hideAdvancedMenu;
                _menuSaved = true;
            }
            bool wanted = on ? false : _menuOriginal;
            if (table.m_hideAdvancedMenu == wanted)
                return false;
            table.m_hideAdvancedMenu = wanted;
            return true;
        }
    }
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneTreeDecorPatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            TreeDecor.RegisterForScene(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PlayerTreeDecorPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null || !__instance.IsOwner())
                return;
            if (ZNetScene.instance != null)
                TreeDecor.RegisterForScene(ZNetScene.instance);
            // Valheim decided which pieces the player knows when the player was created, before these were in the list.
            if (TreeDecor.TryRegisterCultivator())
                QueenHives.RefreshKnown(__instance);
        }
    }
}
