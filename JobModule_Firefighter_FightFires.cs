using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for fighting fires
    /// </summary>
    public class JobModule_Firefighter_FightFires : JobModule_Firefighter
    {
        public override string UniqueID => "FightFires";
        public override float Priority => 9.0f; // Highest priority - fires are emergencies
        public override string Category => "Safety";

        /// <summary>
        /// Process all fires in the home area and fires on colonists/allies
        /// </summary>
        public override bool ShouldProcessFire(Fire fire, Map map)
        {
            if (fire == null || !fire.Spawned || map == null)
                return false;

            // Process fires in home area
            if (map.areaManager.Home[fire.Position])
                return true;

            // Process fires on pawns that belong to player faction or are allies
            if (fire.parent is Pawn parentPawn)
            {
                return parentPawn.Faction == Faction.OfPlayer ||
                       (parentPawn.Faction?.AllyOrNeutralTo(Faction.OfPlayer) == true) ||
                       (parentPawn.HostFaction == Faction.OfPlayer);
            }

            return false;
        }

        /// <summary>
        /// Validates if a firefighter can put out this fire
        /// </summary>
        public override bool ValidateFirefightingJob(Fire fire, Pawn firefighter)
        {
            return CanFightFire(fire, firefighter);
        }

        /// <summary>
        /// Creates a job to fight this fire
        /// </summary>
        protected override Job CreateFirefightingJob(Pawn firefighter, Fire fire)
        {
            Job job = JobMaker.MakeJob(JobDefOf.BeatFire, fire);
            Utility_DebugManager.LogNormal($"{firefighter.LabelShort} created job for fighting fire at {fire.Position}");
            return job;
        }

        /// <summary>
        /// Override to optimize the update cache for fires
        /// </summary>
        public override void UpdateCache(Map map, List<Fire> targetCache)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick <= _lastLocalUpdateTick + CacheUpdateInterval && targetCache.Count > 0)
                return;

            targetCache.Clear();

            // Find all fires on map
            var fires = map.listerThings.ThingsOfDef(ThingDefOf.Fire);
            foreach (Thing thing in fires)
            {
                if (thing is Fire fire)
                {
                    // Only add home area fires unless on a pawn
                    if (ShouldProcessFire(fire, map))
                    {
                        targetCache.Add(fire);
                    }
                }
            }

            // Limit cache size for memory efficiency
            if (targetCache.Count > 500)
            {
                targetCache.RemoveRange(500, targetCache.Count - 500);
            }

            _lastLocalUpdateTick = currentTick;
        }

        // Local update tick tracker for this specific module
        private static int _lastLocalUpdateTick = -999;

        /// <summary>
        /// Reset cache data when game is loaded
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _lastLocalUpdateTick = -999;
        }
    }
}