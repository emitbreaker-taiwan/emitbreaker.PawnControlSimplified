using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Simplified base that routes all TryGiveJob calls through StandardTryGiveJob,
    /// with cache throttling and a unified priority lookup.
    /// </summary>
    public abstract class JobGiver_PawnControl : ThinkNode_JobGiver
    {
        #region Configuration

        /// <summary>
        /// Tag used for eligibility checks in the wrapper
        /// </summary>
        protected abstract string WorkTag { get; }

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected virtual string DebugName => GetType().Name;

        /// <summary>
        /// How many ticks between cache rebuilds
        /// </summary>
        protected virtual int CacheUpdateInterval => 120;

        #endregion

        #region Caching

        private readonly Dictionary<int, int> _lastCacheTick = new Dictionary<int, int>();
        private readonly Dictionary<int, List<Thing>> _cachedTargets = new Dictionary<int, List<Thing>>();

        #endregion

        #region Core flow

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManagerOld.StandardTryGiveJob<JobGiver_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) =>
                {
                    if (p?.Map == null)
                        return null;

                    int mapId = p.Map.uniqueID;
                    int now = Find.TickManager.TicksGame;

                    if (!ShouldExecuteNow(mapId))
                        return null;

                    if (!_lastCacheTick.TryGetValue(mapId, out int last)
                        || now - last >= CacheUpdateInterval)
                    {
                        _lastCacheTick[mapId] = now;
                        _cachedTargets[mapId] = new List<Thing>(GetTargets(p.Map));
                    }

                    var list = _cachedTargets[mapId];
                    if (list == null || list.Count == 0)
                        return null;

                    return ExecuteJobGiverInternal(p, list);
                },
                debugJobDesc: DebugName,
                skipEmergencyCheck: false,
                jobGiverType: GetType()
            );
        }

        protected virtual bool ShouldExecuteNow(int mapId)
        {
            return Find.TickManager.TicksGame % 5 == 0;
        }

        #endregion

        #region Priority

        /// <summary>
        /// Unified priority lookup based on WorkTag
        /// </summary>
        public override float GetPriority(Pawn pawn)
        {
            return GetBasePriority(WorkTag);
        }

        private static float GetBasePriority(string workTag)
        {
            switch (workTag)
            {
                // Emergency/Critical
                case "Firefighter": return 9.0f;
                case "Patient": return 8.8f;
                case "Doctor": return 8.5f;

                // High Priority
                case "PatientBedRest": return 8.0f;
                case "BasicWorker": return 7.8f;
                case "Childcare": return 7.5f;
                case "Warden": return 7.2f;
                case "Handling": return 7.0f;
                case "Cooking": return 6.8f;

                // Medium-High Priority
                case "Hunting": return 6.5f;
                case "Construction": return 6.2f;
                case "Growing": return 5.8f;
                case "Mining": return 5.5f;

                // Medium Priority
                case "PlantCutting": return 5.2f;
                case "Smithing": return 4.9f;
                case "Tailoring": return 4.7f;
                case "Art": return 4.5f;
                case "Crafting": return 4.3f;

                // Low Priority
                case "Hauling": return 3.9f;
                case "Cleaning": return 3.5f;
                case "Research": return 3.2f;
                case "DarkStudy": return 3.0f;

                default: return 5.0f;
            }
        }

        #endregion

        #region Hooks for derived classes

        protected abstract IEnumerable<Thing> GetTargets(Map map);
        protected abstract Job ExecuteJobGiverInternal(Pawn pawn, List<Thing> targets);

        #endregion
    }
}
