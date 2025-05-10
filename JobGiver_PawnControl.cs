using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Simplified base that routes all TryGiveJob calls through StandardTryGiveJob
    /// </summary>
    public abstract class JobGiver_PawnControl : ThinkNode_JobGiver
    {
        /// <summary>
        /// Tag used in eligibility checks
        /// </summary>
        protected abstract string WorkTag { get; }

        /// <summary>
        /// Name used for debug logging
        /// </summary>
        protected virtual string DebugName => GetType().Name;

        /// <summary>
        /// How many ticks before rebuilding the target cache
        /// </summary>
        protected virtual int CacheUpdateInterval => 120;

        // Per-map cache timestamp
        private readonly Dictionary<int, int> _lastCacheTick = new Dictionary<int, int>();

        // Per-map list of cached Things
        private readonly Dictionary<int, List<Thing>> _cachedTargets = new Dictionary<int, List<Thing>>();

        /// <summary>
        /// Routes through your StandardTryGiveJob wrapper for consistency
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_PawnControl>(
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

                    // rebuild cache if stale
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
                jobGiverType: GetType());
        }

        /// <summary>
        /// Simple throttle: only run every 5 ticks by default
        /// </summary>
        protected virtual bool ShouldExecuteNow(int mapId)
        {
            return Find.TickManager.TicksGame % 5 == 0;
        }

        /// <summary>
        /// Must yield all potential targets (e.g. plants needing cut)
        /// </summary>
        protected abstract IEnumerable<Thing> GetTargets(Map map);

        /// <summary>
        /// Must pick one of the cached targets and return a Job (or null)
        /// </summary>
        protected abstract Job ExecuteJobGiverInternal(Pawn pawn, List<Thing> targets);
    }
}
