using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    [StaticConstructorOnStartup]
    public static class Startup_IdentityPreloader
    {
        static Startup_IdentityPreloader()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                // Process any mod extensions marked for removal
                ProcessPendingRemovals();

                // ✅ Clear old mod extension cache
                Utility_CacheManager.ClearAllModExtensions();

                // ✅ Preload mod extensions into runtime cache
                Utility_CacheManager.PreloadModExtensions();

                // ✅ Build identity flag cache based on injected extensions
                Utility_IdentityManager.BuildIdentityFlagCache(false);

                // ✅ Attach skill trackers to eligible pawns
                Utility_SkillManager.AttachSkillTrackersToPawnsSafely();

                // ✅ Preload forced animal cache
                PreloadForcedAnimalCache();
            });
        }

        private static void PreloadForcedAnimalCache()
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race != null)
                {
                    Utility_CacheManager.ForcedAnimals[def] = Utility_CacheManager.GetModExtension(def)?.forceIdentity == ForcedIdentityType.ForceAnimal;
                }
            }
        }

        private static void ProcessPendingRemovals()
        {
            try
            {
                int removed = 0;
                List<ThingDef> defsToProcess = new List<ThingDef>();
                Dictionary<ThingDef, (string mainTree, string constTree)> originalTreesMap =
                    new Dictionary<ThingDef, (string, string)>();

                // First pass: Find races with mod extensions marked for removal and store original trees
                foreach (var cacheEntry in Utility_CacheManager._modExtensionCache.ToList())
                {
                    ThingDef def = cacheEntry.Key;
                    NonHumanlikePawnControlExtension extension = cacheEntry.Value;

                    if (def == null || extension == null) continue;

                    if (extension.toBeRemoved)
                    {
                        defsToProcess.Add(def);
                        // Store original trees before removal
                        originalTreesMap[def] = (extension.originalMainWorkThinkTreeDefName, extension.originalConstantThinkTreeDefName);
                    }
                }

                // Process each def with pending removals
                foreach (ThingDef def in defsToProcess)
                {
                    if (def?.modExtensions == null) continue;

                    // Clean up trackers before removing the extension
                    Utility_DrafterManager.CleanupTrackersForRace(def);

                    // Remove the extensions marked for removal
                    for (int i = def.modExtensions.Count - 1; i >= 0; i--)
                    {
                        if (def.modExtensions[i] is NonHumanlikePawnControlExtension ext && ext.toBeRemoved)
                        {
                            def.modExtensions.RemoveAt(i);
                            removed++;
                        }
                    }

                    // Restore original think trees at race level if we have them
                    if (originalTreesMap.TryGetValue(def, out var trees))
                    {
                        // Set race-level think trees
                        if (!string.IsNullOrEmpty(trees.mainTree))
                        {
                            def.race.thinkTreeMain = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(trees.mainTree);
                        }

                        if (!string.IsNullOrEmpty(trees.constTree))
                        {
                            def.race.thinkTreeConstant = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(trees.constTree);
                        }
                    }

                    // Clear the cached extension for this def
                    Utility_CacheManager.ClearModExtension(def);

                    // Clear race-specific tag caches
                    Utility_TagManager.ClearCacheForRace(def);

                    // Use existing method to restore think trees for all pawns
                    RestoreThinkTreesForAllPawns(def);
                }

                if (removed > 0)
                {
                    Utility_DebugManager.LogNormal($"Removed {removed} mod extensions that were marked for removal");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error processing mod extension removals: {ex}");
            }
        }

        // Helper method to restore think trees for all pawns of a race
        private static void RestoreThinkTreesForAllPawns(ThingDef raceDef)
        {
            if (raceDef == null || Find.Maps == null) return;

            int updated = 0;

            // Process all maps
            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.def != raceDef || pawn.Dead || pawn.Destroyed) continue;

                    // Use the existing utility method to restore think trees
                    Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn);
                    updated++;
                }
            }

            // Also process world pawns
            if (Find.World?.worldPawns?.AllPawnsAliveOrDead != null)
            {
                List<Pawn> worldPawns = Find.World.worldPawns.AllPawnsAliveOrDead
                    .Where(p => p != null && !p.Dead && p.def == raceDef)
                    .ToList();

                foreach (Pawn pawn in worldPawns)
                {
                    Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn);
                    updated++;
                }
            }

            if (updated > 0)
            {
                Utility_DebugManager.LogNormal($"Reset think trees for {updated} pawns of race {raceDef.defName}");
            }
        }
    }

}
