using System;
using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Map component that ensures caches are cleaned up when a map is removed
    /// </summary>
    public class MapComponent_JobGiverCacheCleanup : MapComponent
    {
        public MapComponent_JobGiverCacheCleanup(Map map) : base(map)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Utility_DebugManager.LogNormal($"Initialized cache cleanup for map {map.uniqueID}");
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            //JobGiver_PawnControl.CleanupMap(map.uniqueID);

            // Clear cache for this map
            if (map != null)
            {
                Utility_MapCacheManager.ClearMapCache(map.uniqueID);
                Utility_JobGiverManager.CleanupMapData(map.uniqueID);
            }

            Utility_DebugManager.LogNormal($"Cleaned up caches for removed map {map.uniqueID}");
        }

        public override void MapGenerated()
        {
            base.MapGenerated();

            if (map != null)
            {
                Utility_MapCacheManager.ClearMapCache(map.uniqueID);
                Utility_JobGiverManager.CleanupMapData(map.uniqueID);
            }
            // Initialize any needed caches for the new map
            // Map ID will now be valid
        }
    }
}