using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ForagersGarden
{
    /// <summary>
    /// Ice, Royal Jelly and Crystal beehives. A normal Queen Bee plus the product (Ice / Royal Jelly / Crystal) is crafted at the
    /// workbench into a coloured queen; the queen is what the matching hive (clone of piece_beehive, coloured
    /// like its queen) needs instead of the vanilla Queen Bee. The hive is a vanilla Beehive component with
    /// another m_honeyItem: same rhythm as honey, and auto-store / notices / links of SAC work as for honey.
    /// Same registration pattern as MushroomCrops (clones in ZNetScene + ObjectDB) and ScarecrowPrefab (hammer).
    /// </summary>
    internal static class QueenHives
    {
        /// <summary>Besides the vanilla Queen Bee a queen recipe needs this many of the hive's product.</summary>
        private const int MaterialAmount = 10;
        private const int MaxTexture = 1024;
        private const float TintMix = 0.9f;

        private sealed class Kind
        {
            public string QueenId, HiveId;
            public string Output;      // prefab the hive produces (and the queen recipe needs)
            public string QueenEn, QueenDe, QueenDescEn, QueenDescDe;
            public string HiveEn, HiveDe, HiveDescEn, HiveDescDe;
            public Color Tint;
            public GameObject Queen, Hive;
            public ItemDrop OutputDrop, QueenBeeDrop;
            public Recipe Recipe;
        }

        private static readonly Kind[] Kinds =
        {
            new Kind
            {
                QueenId = "sac_queen_ice", HiveId = "sac_hive_ice", Output = "Ice",
                QueenEn = "Ice Queen Bee", QueenDe = "Eis-Bienenkönigin",
                QueenDescEn = "A frozen queen bee, made from a Queen Bee and Ice at the workbench. An Ice Beehive needs her instead of a normal queen.",
                QueenDescDe = "Eine gefrorene Bienenkönigin, an der Werkbank aus einer Bienenkönigin und Eis hergestellt. Ein Eis-Bienenstock braucht sie statt einer normalen Königin.",
                HiveEn = "Ice Beehive", HiveDe = "Eis-Bienenstock",
                HiveDescEn = "A white beehive that makes ice instead of honey. Needs an Ice Queen Bee.",
                HiveDescDe = "Ein weißer Bienenstock, der Eis statt Honig herstellt. Braucht eine Eis-Bienenkönigin.",
                Tint = new Color(0.82f, 0.93f, 1f)
            },
            new Kind
            {
                QueenId = "sac_queen_jelly", HiveId = "sac_hive_jelly", Output = "RoyalJelly",
                QueenEn = "Royal Jelly Queen Bee", QueenDe = "Gelée-Royale-Bienenkönigin",
                QueenDescEn = "A golden queen bee, made from a Queen Bee and Royal Jelly at the workbench. A Royal Jelly Beehive needs her instead of a normal queen.",
                QueenDescDe = "Eine goldene Bienenkönigin, an der Werkbank aus einer Bienenkönigin und Gelée Royale hergestellt. Ein Gelée-Royale-Bienenstock braucht sie statt einer normalen Königin.",
                HiveEn = "Royal Jelly Beehive", HiveDe = "Gelée-Royale-Bienenstock",
                HiveDescEn = "A yellow beehive that makes royal jelly instead of honey. Needs a Royal Jelly Queen Bee.",
                HiveDescDe = "Ein gelber Bienenstock, der Gelée Royale statt Honig herstellt. Braucht eine Gelée-Royale-Bienenkönigin.",
                Tint = new Color(1f, 0.84f, 0.28f)
            },
            new Kind
            {
                QueenId = "sac_queen_crystal", HiveId = "sac_hive_crystal", Output = "Crystal",
                QueenEn = "Crystal Queen Bee", QueenDe = "Kristall-Bienenkönigin",
                QueenDescEn = "A violet queen bee, made from a Queen Bee and Crystal at the workbench. A Crystal Beehive needs her instead of a normal queen.",
                QueenDescDe = "Eine violette Bienenkönigin, an der Werkbank aus einer Bienenkönigin und Kristall hergestellt. Ein Kristall-Bienenstock braucht sie statt einer normalen Königin.",
                HiveEn = "Crystal Beehive", HiveDe = "Kristall-Bienenstock",
                HiveDescEn = "A violet beehive that makes crystal instead of honey. Needs a Crystal Queen Bee.",
                HiveDescDe = "Ein violetter Bienenstock, der Kristall statt Honig herstellt. Braucht eine Kristall-Bienenkönigin.",
                Tint = new Color(0.72f, 0.62f, 1f)
            },
        };

        private static readonly MethodInfo UpdateRegisters = AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");
        private static readonly MethodInfo UpdateKnownRecipes = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList");
        private static readonly MethodInfo UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");

        private static GameObject _hide;
        private static CraftingStation _workbench;
        private static bool _done;
        private static bool _busy;
        private static readonly Dictionary<int, GameObject> ByHash = new Dictionary<int, GameObject>();
        private static readonly Dictionary<string, Texture2D> TextureCache = new Dictionary<string, Texture2D>();

        private static bool NoGraphics
        {
            get { return SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null; }
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
                AddToDb(ObjectDB.instance);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Forager's Garden queen hives skipped: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private static GameObject Find(ZNetScene scene, string name)
        {
            GameObject go = scene.GetPrefab(name);
            if (go == null && ObjectDB.instance != null)
                go = ObjectDB.instance.GetItemPrefab(name);
            return go;
        }

        private static void Build(ZNetScene scene)
        {
            GameObject queenBee = Find(scene, "QueenBee");
            GameObject beehive = scene.GetPrefab("piece_beehive");
            if (queenBee == null || beehive == null)
            {
                Plugin.Log.LogDebug("Forager's Garden queen hives: QueenBee / piece_beehive not found yet.");
                return;
            }
            if (_hide == null)
            {
                _hide = new GameObject("sac_queenhive_hide");
                Object.DontDestroyOnLoad(_hide);
                _hide.SetActive(false);
            }
            GameObject bench = scene.GetPrefab("piece_workbench");
            _workbench = bench != null ? bench.GetComponent<CraftingStation>() : null;
            if (_workbench == null)
                Plugin.Log.LogWarning("Forager's Garden queen hives: workbench not found, the queens cannot be crafted.");

            for (int i = 0; i < Kinds.Length; i++)
            {
                try
                {
                    BuildKind(scene, Kinds[i], queenBee, beehive);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("Forager's Garden queen hive " + Kinds[i].HiveId + " skipped: " + ex.Message);
                }
            }
            _done = true;
            NameAll(scene);
            Plugin.Log.LogDebug("Forager's Garden queen hives registered.");
        }

        private static void BuildKind(ZNetScene scene, Kind k, GameObject queenBee, GameObject beehive)
        {
            GameObject outPrefab = Find(scene, k.Output);
            ItemDrop outDrop = outPrefab != null ? outPrefab.GetComponent<ItemDrop>() : null;
            ItemDrop beeDrop = queenBee.GetComponent<ItemDrop>();
            if (outDrop == null || beeDrop == null)
            {
                Plugin.Log.LogDebug("Forager's Garden queen hives: " + k.Output + " / QueenBee item not found, skipped.");
                return;
            }
            k.OutputDrop = outDrop;
            k.QueenBeeDrop = beeDrop;

            // The queen: Queen Bee item with its own name, coloured icon and ground model.
            k.Queen = Clone(queenBee, k.QueenId);
            ItemDrop queenDrop = k.Queen.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData shared = queenDrop.m_itemData.m_shared;
            shared.m_name = Loc.T(k.QueenEn, k.QueenDe);
            shared.m_description = Loc.T(k.QueenDescEn, k.QueenDescDe);
            Sprite[] icons = shared.m_icons;
            Sprite queenIcon = icons != null && icons.Length > 0 ? TintSprite(icons[0], k.Tint) : null;
            if (queenIcon != null)
                shared.m_icons = new[] { queenIcon };
            Recolor(k.Queen, k.Tint, null);

            // The hive: vanilla beehive that makes the product and needs the queen.
            k.Hive = Clone(beehive, k.HiveId);
            Beehive bee = k.Hive.GetComponent<Beehive>();
            Piece piece = k.Hive.GetComponent<Piece>();
            if (bee == null || piece == null)
            {
                Plugin.Log.LogWarning("Forager's Garden queen hives: piece_beehive lost Beehive/Piece.");
                Object.Destroy(k.Hive);
                k.Hive = null;
                return;
            }
            string hiveName = Loc.T(k.HiveEn, k.HiveDe);
            bee.m_honeyItem = outDrop;
            bee.m_name = hiveName;
            piece.m_name = hiveName;
            piece.m_description = Loc.T(k.HiveDescEn, k.HiveDescDe);
            piece.m_resources = HiveResources(piece.m_resources, queenDrop);
            Sprite hiveIcon = piece.m_icon != null ? TintSprite(piece.m_icon, k.Tint) : null;
            if (hiveIcon != null)
                piece.m_icon = hiveIcon;
            // The hive and the bees flying around it get the same colour.
            Recolor(k.Hive, k.Tint, bee.m_beeEffect != null ? bee.m_beeEffect.transform : null);
            RecolorBees(bee.m_beeEffect, k.Tint);
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

        /// <summary>The vanilla hive's requirements, with the vanilla Queen Bee replaced by the coloured queen.</summary>
        private static Piece.Requirement[] HiveResources(Piece.Requirement[] source, ItemDrop queen)
        {
            var list = new List<Piece.Requirement>();
            bool swapped = false;
            if (source != null)
            {
                for (int i = 0; i < source.Length; i++)
                {
                    Piece.Requirement req = source[i];
                    if (req == null)
                        continue;
                    var copy = new Piece.Requirement
                    {
                        m_resItem = req.m_resItem,
                        m_amount = req.m_amount,
                        m_amountPerLevel = req.m_amountPerLevel,
                        m_recover = req.m_recover
                    };
                    if (req.m_resItem != null && Utils.GetPrefabName(req.m_resItem.gameObject) == "QueenBee")
                    {
                        copy.m_resItem = queen;
                        swapped = true;
                    }
                    list.Add(copy);
                }
            }
            if (!swapped)
            {
                Plugin.Log.LogDebug("Forager's Garden queen hives: piece_beehive has no Queen Bee requirement, queen added.");
                list.Add(new Piece.Requirement { m_resItem = queen, m_amount = 1, m_amountPerLevel = 0, m_recover = false });
            }
            return list.ToArray();
        }

        // ---------------------------------------------------------------- colour

        /// <summary>Colours the model: main textures get a tinted copy (keeps the shading), others the colour.</summary>
        private static void Recolor(GameObject root, Color tint, Transform skip)
        {
            if (NoGraphics)
                return;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                    continue;
                if (skip != null && r.transform.IsChildOf(skip))
                    continue;
                Material[] mats = r.materials; // own copies: the vanilla materials stay as they are
                for (int i = 0; i < mats.Length; i++)
                    TintMaterial(mats[i], tint);
                r.materials = mats;
            }
        }

        /// <summary>Main texture of the material replaced by a tinted copy, else its colour. True when the texture was tinted.</summary>
        private static bool TintMaterial(Material m, Color tint)
        {
            if (m == null)
                return false;
            Texture main = m.HasProperty("_MainTex") ? m.mainTexture : null;
            Texture2D tinted = main != null ? TintedTexture(main, tint) : null;
            if (tinted != null)
            {
                m.mainTexture = tinted;
                return true;
            }
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", tint);
            return false;
        }

        /// <summary>The bees flying around the hive (particles, or meshes below the bee effect) get the colour of the hive.</summary>
        private static void RecolorBees(GameObject effect, Color tint)
        {
            if (effect == null || NoGraphics)
                return;
            Recolor(effect, tint, null); // bees that are meshes
            foreach (ParticleSystem ps in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                bool textured = false;
                ParticleSystemRenderer pr = ps.GetComponent<ParticleSystemRenderer>();
                if (pr != null)
                {
                    Material[] mats = pr.materials;
                    for (int i = 0; i < mats.Length; i++)
                        textured |= TintMaterial(mats[i], tint);
                    pr.materials = mats;
                }
                // Tinted texture: the particles stay white (no tint twice); without one the particle colour does it.
                ParticleSystem.MainModule main = ps.main;
                main.startColor = textured ? Color.white : tint;
            }
        }

        private static void TintPixels(Color32[] px, Color tint)
        {
            float tr = tint.r * 255f, tg = tint.g * 255f, tb = tint.b * 255f;
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                float lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                float k = Mathf.Clamp01(0.55f + 0.6f * lum); // keeps the shading, lifts the dark parts
                px[i].r = (byte)Mathf.Clamp(Mathf.Lerp(c.r, tr * k, TintMix), 0f, 255f);
                px[i].g = (byte)Mathf.Clamp(Mathf.Lerp(c.g, tg * k, TintMix), 0f, 255f);
                px[i].b = (byte)Mathf.Clamp(Mathf.Lerp(c.b, tb * k, TintMix), 0f, 255f);
            }
        }

        private static Texture2D TintedTexture(Texture src, Color tint)
        {
            string key = src.GetInstanceID() + "|" + ColorUtility.ToHtmlStringRGB(tint);
            Texture2D cached;
            if (TextureCache.TryGetValue(key, out cached) && cached != null)
                return cached;
            try
            {
                float scale = Mathf.Min(1f, (float)MaxTexture / Mathf.Max(src.width, src.height));
                int w = Mathf.Max(4, (int)(src.width * scale));
                int h = Mathf.Max(4, (int)(src.height * scale));
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(src, rt);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, true);
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);

                Color32[] px = copy.GetPixels32();
                TintPixels(px, tint);
                copy.SetPixels32(px);
                copy.wrapMode = src.wrapMode;
                copy.filterMode = FilterMode.Bilinear;
                copy.Apply(true, true);
                TextureCache[key] = copy;
                return copy;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("Queen hive texture: " + ex.Message);
                return null;
            }
        }

        private static Sprite TintSprite(Sprite source, Color tint)
        {
            if (source == null || NoGraphics)
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

                Color32[] px = copy.GetPixels32();
                TintPixels(px, tint);
                copy.SetPixels32(px);
                copy.Apply();
                return Sprite.Create(copy, new Rect(0f, 0f, copy.width, copy.height), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("Queen hive icon: " + ex.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------- registration

        private static void NameAll(ZNetScene scene)
        {
            for (int i = 0; i < Kinds.Length; i++)
            {
                Name(scene, Kinds[i].Queen);
                Name(scene, Kinds[i].Hive);
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

        private static Recipe MakeRecipe(Kind k)
        {
            if (_workbench == null || k.Queen == null || k.QueenBeeDrop == null || k.OutputDrop == null)
                return null;
            Recipe r = ScriptableObject.CreateInstance<Recipe>();
            r.name = "Recipe_" + k.QueenId;
            r.hideFlags = HideFlags.HideAndDontSave;
            r.m_item = k.Queen.GetComponent<ItemDrop>();
            r.m_amount = 1;
            r.m_enabled = true;
            r.m_craftingStation = _workbench;
            r.m_minStationLevel = 1;
            r.m_resources = new[]
            {
                new Piece.Requirement { m_resItem = k.QueenBeeDrop, m_amount = 1, m_amountPerLevel = 0, m_recover = false },
                new Piece.Requirement { m_resItem = k.OutputDrop, m_amount = MaterialAmount, m_amountPerLevel = 0, m_recover = false }
            };
            return r;
        }

        /// <summary>The queens are items (ObjectDB has to know them: inventories, drops, recipes) and have recipes.</summary>
        internal static void AddToDb(ObjectDB db)
        {
            if (!_done || db == null || db.m_items == null || db.m_recipes == null || db.GetItemPrefab("Hammer") == null)
                return; // not filled yet: the CopyOtherDB patch tries again
            bool added = false;
            for (int i = 0; i < Kinds.Length; i++)
            {
                GameObject queen = Kinds[i].Queen;
                if (queen != null && !db.m_items.Contains(queen))
                {
                    db.m_items.Add(queen);
                    added = true;
                }
            }
            if (added && UpdateRegisters != null)
                UpdateRegisters.Invoke(db, null);

            // The queen recipes only while the extra beehives are switched on (the queens stay known items:
            // queens already in a chest or bag must not vanish).
            bool on = Enabled;
            for (int i = 0; i < Kinds.Length; i++)
            {
                Kind k = Kinds[i];
                if (k.Queen == null)
                    continue;
                if (k.Recipe == null)
                    k.Recipe = MakeRecipe(k);
                if (k.Recipe == null)
                    continue;
                if (on && !db.m_recipes.Contains(k.Recipe))
                    db.m_recipes.Add(k.Recipe);
                else if (!on)
                    db.m_recipes.Remove(k.Recipe);
            }
        }

        /// <summary>F10 "Extra beehives" (ExtraBeehives). Off: no hives in the hammer, no queen recipes.</summary>
        internal static bool Enabled
        {
            get { return Plugin.Beehives == null || Plugin.Beehives.Value; }
        }

        /// <summary>The switch was changed: add / remove hives and recipes now and let the player's lists follow.</summary>
        internal static void ApplySetting()
        {
            if (!_done || ObjectDB.instance == null)
                return;
            AddToDb(ObjectDB.instance);
            TryRegisterHammer();
            if (Player.m_localPlayer != null)
                RefreshKnown(Player.m_localPlayer);
        }

        /// <summary>
        /// The hives in the hammer's build list (like the scarecrow) while the extra beehives are on, removed when
        /// off (built hives keep working: their prefabs stay registered). True when the list changed.
        /// </summary>
        internal static bool TryRegisterHammer()
        {
            if (!_done || ObjectDB.instance == null)
                return false;
            GameObject hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            ItemDrop drop = hammer != null ? hammer.GetComponent<ItemDrop>() : null;
            PieceTable table = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                ? drop.m_itemData.m_shared.m_buildPieces : null;
            if (table == null || table.m_pieces == null)
                return false;
            bool on = Enabled;
            bool changed = false;
            for (int i = 0; i < Kinds.Length; i++)
            {
                GameObject hive = Kinds[i].Hive;
                if (hive == null)
                    continue;
                if (on && !table.m_pieces.Contains(hive))
                {
                    table.m_pieces.Add(hive);
                    changed = true;
                }
                else if (!on && table.m_pieces.Remove(hive))
                {
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>The player is already in the world: let Valheim re-check which recipes / pieces are known.</summary>
        internal static void RefreshKnown(Player player)
        {
            try
            {
                if (UpdateKnownRecipes != null)
                    UpdateKnownRecipes.Invoke(player, null);
                if (UpdateAvailablePieces != null)
                    UpdateAvailablePieces.Invoke(player, null);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("Queen hives refresh: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneQueenHivePatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            QueenHives.RegisterForScene(__instance);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDbQueenHivePatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            QueenHives.AddToDb(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class PlayerQueenHivePatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null || !__instance.IsOwner())
                return;
            if (ZNetScene.instance != null)
                QueenHives.RegisterForScene(ZNetScene.instance);
            QueenHives.AddToDb(ObjectDB.instance);
            if (QueenHives.TryRegisterHammer())
                QueenHives.RefreshKnown(__instance);
        }
    }
}
