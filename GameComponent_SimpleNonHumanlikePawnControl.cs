using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    public class GameComponent_SimpleNonHumanlikePawnControl : GameComponent
    {
        public static List<string> FavoritePresets = new List<string>();
        public static List<string> PinnedTags = new List<string>();
        public static List<string> PinnedPresets = new List<string>();
        public static HashSet<string> IgnoredSuggestions = new HashSet<string>();

        public Dictionary<string, List<string>> serializedVirtualTagBuffer = new Dictionary<string, List<string>>();

        public static GameComponent_SimpleNonHumanlikePawnControl Instance;

        public GameComponent_SimpleNonHumanlikePawnControl(Game game)
        {
            Instance = this;
            if (serializedVirtualTagBuffer == null)
                serializedVirtualTagBuffer = new Dictionary<string, List<string>>();
        }

        public override void FinalizeInit()
        {
            Instance = this;

            // === Clear runtime-only caches ===
            Utility_NonHumanlikePawnControl.ClearCache();
            Utility_NonHumanlikePawnControl.PrefetchAll();
            Utility_NonHumanlikePawnControl.PrefetchAllTags();

            // === Compatibility patch caches ===
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                NonHumanlikePawnControlExtension ext = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (ext != null)
                {
                    Utility_CacheManager.RefreshTagCache(def, ext);
                }
            }

            Utility_CECompatibility.ClearCaches();
            Utility_HARCompatibility.ClearCache();
            Utility_CacheManager.ClearSimulatedSkillCache(); // ✅ Clear on new game or load
            Utility_CacheManager.ClearResolvedTagCache();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            ApplyVirtualTagsFromStorage();
        }

        private void ApplyVirtualTagsFromStorage()
        {
            // Create a copy of the keys to avoid modifying the collection during enumeration
            var keys = serializedVirtualTagBuffer.Keys.ToList();

            foreach (var key in keys)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(key);
                if (def == null || Utility_ModExtensionResolver.HasPhysicalModExtension(def)) continue;

                // Process the value associated with the key
                VirtualTagStorageService.Instance.Set(def, serializedVirtualTagBuffer[key]);
                Utility_CacheManager.InvalidateTagCachesFor(def);
            }
        }


        public override void ExposeData()
        {
            // === UI persistence ===
            Scribe_Collections.Look(ref PinnedTags, "PinnedTags", LookMode.Value);
            Scribe_Collections.Look(ref PinnedPresets, "PinnedPresets", LookMode.Value);
            Scribe_Collections.Look(ref FavoritePresets, "FavoritePresets", LookMode.Value);
            Scribe_Collections.Look(ref IgnoredSuggestions, "IgnoredSuggestions", LookMode.Value);

            // === Virtual tag persistence ===
            Scribe_Collections.Look(ref serializedVirtualTagBuffer, "serializedVirtualTagBuffer", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (serializedVirtualTagBuffer == null)
                    serializedVirtualTagBuffer = new Dictionary<string, List<string>>();
            }
            else if (Scribe.mode == LoadSaveMode.Saving)
            {
                serializedVirtualTagBuffer = VirtualTagStorageService.Instance.Export() ?? new Dictionary<string, List<string>>();
            }
        }

        public static void Save()
        {
            // External save trigger (optional, if used in future)
        }
    }
}
