using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Custom reservation manager that extends RimWorld's native reservation system
    /// to provide better integration with PawnControl job givers.
    /// </summary>
    public class Utility_ReservationManager
    {
        #region Inner Classes

        /// <summary>
        /// Represents a custom reservation made by a pawn for a job target
        /// </summary>
        public class ModReservation
        {
            public Pawn Claimant { get; private set; }
            public Job Job { get; private set; }
            public Thing Target { get; private set; }
            public int MaxPawns { get; private set; }
            public string JobGiverType { get; private set; }
            public int ReserveTick { get; private set; }

            public ModReservation(Pawn claimant, Job job, Thing target, int maxPawns = 1, string jobGiverType = null)
            {
                Claimant = claimant;
                Job = job;
                Target = target;
                MaxPawns = maxPawns;
                JobGiverType = jobGiverType;
                ReserveTick = Find.TickManager.TicksGame;
            }

            public override string ToString()
            {
                return $"{Claimant?.LabelShort ?? "null"}:{Job.ToStringSafe()} -> {Target.ToStringSafe()} ({JobGiverType})";
            }
        }

        #endregion

        #region Static Members

        private static readonly Dictionary<int, Utility_ReservationManager> _instances = new Dictionary<int, Utility_ReservationManager>();

        // Get manager for a specific map
        public static Utility_ReservationManager GetFor(Map map)
        {
            if (map == null) return null;

            int mapId = map.uniqueID;
            if (!_instances.TryGetValue(mapId, out var manager))
            {
                manager = new Utility_ReservationManager(map);
                _instances[mapId] = manager;
            }
            return manager;
        }

        // Clean up a specific map's manager
        public static void RemoveMap(Map map)
        {
            if (map == null) return;
            _instances.Remove(map.uniqueID);
        }

        // Reset all managers
        public static void ResetAll()
        {
            foreach (var manager in _instances.Values)
            {
                manager.Reset();
            }
        }

        #endregion

        #region Instance Members

        private readonly Map _map;
        private readonly List<ModReservation> _reservations = new List<ModReservation>();
        private int _lastCleanupTick = 0;
        private const int CLEANUP_INTERVAL = 300; // 5 seconds

        /// <summary>
        /// Constructor for the reservation manager
        /// </summary>
        private Utility_ReservationManager(Map map)
        {
            _map = map;
            _lastCleanupTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Reset all reservations
        /// </summary>
        public void Reset()
        {
            _reservations.Clear();
            _lastCleanupTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Clean up stale reservations periodically
        /// </summary>
        private void CleanupStaleReservations()
        {
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - _lastCleanupTick < CLEANUP_INTERVAL)
                return;

            _lastCleanupTick = currentTick;

            List<ModReservation> toRemove = new List<ModReservation>();
            foreach (var reservation in _reservations)
            {
                // Remove if target no longer exists
                if (reservation.Target == null || reservation.Target.Destroyed || !reservation.Target.Spawned)
                {
                    toRemove.Add(reservation);
                    continue;
                }

                // Remove if pawn no longer exists or has a different job
                if (reservation.Claimant == null || 
                    reservation.Claimant.Destroyed || 
                    !reservation.Claimant.Spawned || 
                    reservation.Claimant.Dead ||
                    reservation.Claimant.CurJob == null ||
                    reservation.Claimant.CurJob != reservation.Job)
                {
                    toRemove.Add(reservation);
                    continue;
                }

                // Reserve expired after a set time (to prevent deadlocks)
                if (currentTick - reservation.ReserveTick > 6000) // 100 seconds
                {
                    toRemove.Add(reservation);
                    if (Prefs.DevMode)
                        Utility_DebugManager.LogNormal($"Reservation expired for {reservation.Claimant} -> {reservation.Target}");
                }
            }

            // Remove stale reservations
            foreach (var reservation in toRemove)
            {
                _reservations.Remove(reservation);
            }

            if (Prefs.DevMode && toRemove.Count > 0)
            {
                Utility_DebugManager.LogNormal($"Cleaned up {toRemove.Count} stale mod reservations");
            }
        }

        /// <summary>
        /// Check if a target is already reserved by any pawn
        /// </summary>
        public bool IsReserved(Thing target)
        {
            if (target == null) return false;

            // First check RimWorld's native reservation system (source of truth)
            if (_map.reservationManager.IsReserved(target))
                return true;
            
            // Check our custom reservations
            return _reservations.Any(r => r.Target == target);
        }

        /// <summary>
        /// Check if a target is reserved by a specific pawn
        /// </summary>
        public bool IsReservedBy(Thing target, Pawn pawn)
        {
            if (target == null || pawn == null) return false;

            // First check RimWorld's native reservation system
            if (_map.reservationManager.ReservedBy(target, pawn))
                return true;
                
            // Check our custom reservations
            return _reservations.Any(r => r.Target == target && r.Claimant == pawn);
        }

        /// <summary>
        /// Check if a pawn can reserve a target
        /// </summary>
        public bool CanReserve(Pawn pawn, Thing target, int maxPawns = 1)
        {
            if (pawn == null || target == null) return false;
            
            // Quick validation
            if (!pawn.Spawned || pawn.Map != _map || 
                !target.Spawned || target.Map != _map)
                return false;

            // First try RimWorld's native reservation system
            // If the pawn can reserve there, then we're good
            if (_map.reservationManager.CanReserve(pawn, target, maxPawns))
                return true;
                
            // Check if this pawn already reserved this target in our system
            if (_reservations.Any(r => r.Target == target && r.Claimant == pawn))
                return true;

            // Check if another pawn has reserved this target in our system
            var existing = _reservations.FirstOrDefault(r => r.Target == target);
            if (existing != null)
            {
                // If reserved by someone of same faction, respect it
                if (pawn.Faction == existing.Claimant.Faction)
                    return false;
                
                // If reserved by allied faction, respect it too
                if (pawn.Faction != null && 
                    existing.Claimant.Faction != null && 
                    !pawn.Faction.HostileTo(existing.Claimant.Faction))
                    return false;
                    
                // Otherwise, we can take it
                return true;
            }
            
            return true;
        }

        /// <summary>
        /// Reserve a target for a pawn
        /// </summary>
        public bool Reserve(Pawn pawn, Job job, Thing target, int maxPawns = 1, string jobGiverType = null, bool forceVanillaReserve = false)
        {
            if (pawn == null || job == null || target == null) 
                return false;

            // Quick validation
            if (!pawn.Spawned || pawn.Map != _map ||
                !target.Spawned || target.Map != _map)
                return false;

            // Clean up stale reservations first
            CleanupStaleReservations();

            // Check if already reserved by this pawn
            if (_reservations.Any(r => r.Target == target && r.Claimant == pawn && r.Job == job))
                return true;

            // Try to reserve in RimWorld's system first if forced
            if (forceVanillaReserve)
            {
                if (_map.reservationManager.CanReserve(pawn, target, maxPawns))
                {
                    bool vanillaReserved = _map.reservationManager.Reserve(
                        pawn, job, target, maxPawns);
                    
                    if (!vanillaReserved)
                    {
                        // If vanilla reservation failed, our reservation would be inconsistent
                        if (Prefs.DevMode)
                            Utility_DebugManager.LogNormal($"Failed to reserve {target} for {pawn} in vanilla system");
                        return false;
                    }
                }
            }

            // Check if we can reserve in our system
            if (!CanReserve(pawn, target, maxPawns))
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"Cannot reserve {target} for {pawn} - already reserved");
                return false;
            }

            // Create our reservation
            _reservations.Add(new ModReservation(
                pawn, job, target, maxPawns, jobGiverType));

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reserved {target} for {pawn} with job {job} (jobGiver: {jobGiverType})");
            }

            return true;
        }

        /// <summary>
        /// Release a reservation for a specific pawn and target
        /// </summary>
        public void Release(Pawn pawn, Thing target)
        {
            if (pawn == null || target == null) return;

            // Remove from our system
            _reservations.RemoveAll(r => r.Claimant == pawn && r.Target == target);
            
            // Also try to release from RimWorld's system
            if (pawn.Map != null && target.Map != null && 
                pawn.Map.reservationManager.ReservedBy(target, pawn))
            {
                if (pawn.CurJob != null)
                {
                    pawn.Map.reservationManager.Release(target, pawn, pawn.CurJob);
                }
            }
        }

        /// <summary>
        /// Release all reservations for a specific pawn
        /// </summary>
        public void ReleaseAllFor(Pawn pawn)
        {
            if (pawn == null) return;

            // Remove from our system
            _reservations.RemoveAll(r => r.Claimant == pawn);
            
            // Also try to release from RimWorld's system
            if (pawn.Map != null && pawn.CurJob != null)
            {
                pawn.Map.reservationManager.ReleaseClaimedBy(pawn, pawn.CurJob);
            }
        }

        /// <summary>
        /// Release all reservations for a specific target
        /// </summary>
        public void ReleaseAllForTarget(Thing target)
        {
            if (target == null) return;

            // Remove from our system
            _reservations.RemoveAll(r => r.Target == target);
            
            // Also try to release from RimWorld's system
            if (target.Map != null)
            {
                target.Map.reservationManager.ReleaseAllForTarget(target);
            }
        }

        /// <summary>
        /// Get all pawns that have reserved a specific target
        /// </summary>
        public IEnumerable<Pawn> GetReservers(Thing target)
        {
            if (target == null) yield break;

            // Combine results from both systems
            HashSet<Pawn> results = new HashSet<Pawn>();
            
            // Check our system first
            foreach (var reservation in _reservations.Where(r => r.Target == target))
            {
                results.Add(reservation.Claimant);
            }
            
            // Then check RimWorld's system
            if (target.Map != null)
            {
                HashSet<Pawn> vanillaReservers = new HashSet<Pawn>();
                target.Map.reservationManager.ReserversOf(target, vanillaReservers);
                foreach (var reserver in vanillaReservers)
                {
                    results.Add(reserver);
                }
            }
            
            // Return combined results
            foreach (var pawn in results)
            {
                yield return pawn;
            }
        }

        /// <summary>
        /// Debug method to get all current reservations
        /// </summary>
        public string DebugString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== ModReservationManager Reservations ===");
            
            if (_reservations.Count == 0)
            {
                sb.AppendLine("No active reservations");
            }
            else
            {
                foreach (var res in _reservations)
                {
                    sb.AppendLine($"- {res}");
                }
            }
            
            return sb.ToString();
        }

        #endregion
    }
}