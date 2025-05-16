using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public static class Utility_JobGiverRegistry
    {
        public static void RegisterAllJobGivers()
        {
            // Use reflection to find all types that inherit from JobGiver_PawnControl
            var jobGiverTypes = typeof(JobGiver_PawnControl).AllSubclassesNonAbstract();

            // Register emergency job givers
            RegisterJobGiversByCategory(jobGiverTypes, IsEmergencyJobGiver, RegisterEmergencyJobGiver);

            // Register core job givers
            RegisterJobGiversByCategory(jobGiverTypes, IsCoreJobGiver, RegisterCoreJobGiver);

            // Register background job givers
            RegisterJobGiversByCategory(jobGiverTypes, IsBackgroundJobGiver, RegisterBackgroundJobGiver);
        }

        private static void RegisterJobGiversByCategory(
            IEnumerable<Type> jobGiverTypes,
            Func<Type, bool> categoryPredicate,
            Action<Type> registerAction)
        {
            foreach (var type in jobGiverTypes.Where(categoryPredicate))
            {
                registerAction(type);
            }
        }

        private static bool IsEmergencyJobGiver(Type jobGiverType)
        {
            // Try name-based detection first (more explicit)
            if (jobGiverType.Name.Contains("Firefighter") ||
                jobGiverType.Name.Contains("Emergency") ||
                jobGiverType.Name.Contains("UrgentPatient"))
                return true;

            // Try work tag detection
            try
            {
                var instance = Activator.CreateInstance(jobGiverType) as JobGiver_PawnControl;
                if (instance != null)
                {
                    string workTag = instance.WorkTag;
                    return workTag == "Firefighter" || workTag == "Patient";
                }
            }
            catch { }

            // Default fallback: try to derive from priority system
            try
            {
                var method = jobGiverType.GetMethod("GetBasePriority",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (method != null)
                {
                    var instance = Activator.CreateInstance(jobGiverType) as JobGiver_PawnControl;
                    if (instance != null)
                    {
                        float priority = (float)method.Invoke(instance, new object[] { instance.WorkTag });
                        return priority >= 8.0f;
                    }
                }
            }
            catch { }

            return false;
        }

        private static void RegisterEmergencyJobGiver(Type jobGiverType)
        {
            string workTag = GetWorkTagFromType(jobGiverType);
            int priority = GetPriority(jobGiverType, 9); // Default to 9
            int tickGroups = GetTickGroups(jobGiverType, 2); // Default to 2

            Utility_JobGiverTickManager.RegisterJobGiver(
                jobGiverType,
                workTag,
                priority,
                Utility_JobGiverTickManager.HIGH_PRIORITY_INTERVAL,
                tickGroups);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Registered emergency JobGiver: {jobGiverType.Name}, WorkType: {workTag}, Priority: {priority}, Tick Groups: {tickGroups}");
            }
        }

        private static bool IsCoreJobGiver(Type jobGiverType)
        {
            // Try name-based detection for core job givers
            if (jobGiverType.Name.Contains("Doctor") ||
                jobGiverType.Name.Contains("Handling") ||
                jobGiverType.Name.Contains("Construction"))
                return true;

            // Try work tag detection
            try
            {
                var instance = Activator.CreateInstance(jobGiverType) as JobGiver_PawnControl;
                if (instance != null)
                {
                    string workTag = instance.WorkTag;
                    return workTag == "Doctor" || workTag == "Construction" ||
                           workTag == "Handling" || workTag == "Cooking" ||
                           workTag == "Growing" || workTag == "Mining";
                }
            }
            catch { }

            // Default fallback: try to derive from priority system
            try
            {
                var method = jobGiverType.GetMethod("GetBasePriority",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (method != null)
                {
                    var instance = Activator.CreateInstance(jobGiverType) as JobGiver_PawnControl;
                    if (instance != null)
                    {
                        float priority = (float)method.Invoke(instance, new object[] { instance.WorkTag });
                        return priority >= 5.0f && priority < 8.0f;
                    }
                }
            }
            catch { }

            return false;
        }

        private static void RegisterCoreJobGiver(Type jobGiverType)
        {
            string workTag = GetWorkTagFromType(jobGiverType);
            int priority = GetPriority(jobGiverType, 6); // Default to 6
            int tickGroups = GetTickGroups(jobGiverType, 4); // Default to 4
            int? interval = GetInterval(jobGiverType, Utility_JobGiverTickManager.MEDIUM_PRIORITY_INTERVAL);

            Utility_JobGiverTickManager.RegisterJobGiver(
                jobGiverType,
                workTag,
                priority,
                interval,
                tickGroups);

            if (Utility_DebugManager.ShouldLogDetailed())
            {
                Utility_DebugManager.LogNormal($"Registered core JobGiver: {jobGiverType.Name}, WorkType: {workTag}, Priority: {priority}, Tick Groups: {tickGroups}");
            }
        }

        private static bool IsBackgroundJobGiver(Type jobGiverType)
        {
            // If not emergency or core, it's background by default
            if (IsEmergencyJobGiver(jobGiverType) || IsCoreJobGiver(jobGiverType))
                return false;

            // Try name-based detection for background job givers
            if (jobGiverType.Name.Contains("Cleaning") ||
                jobGiverType.Name.Contains("Hauling"))
                return true;

            // Try work tag detection
            try
            {
                var instance = Activator.CreateInstance(jobGiverType) as JobGiver_PawnControl;
                if (instance != null)
                {
                    string workTag = instance.WorkTag;
                    return workTag == "Cleaning" || workTag == "Hauling" ||
                           workTag == "Research" || workTag == "Art";
                }
            }
            catch { }

            // Default fallback: try to derive from priority system
            try
            {
                var method = jobGiverType.GetMethod("GetBasePriority",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (method != null)
                {
                    var instance = Activator.CreateInstance(jobGiverType) as JobGiver_PawnControl;
                    if (instance != null)
                    {
                        float priority = (float)method.Invoke(instance, new object[] { instance.WorkTag });
                        return priority < 5.0f;
                    }
                }
            }
            catch { }

            // If we couldn't determine otherwise, assume it's background
            return true;
        }

        private static void RegisterBackgroundJobGiver(Type jobGiverType)
        {
            string workTag = GetWorkTagFromType(jobGiverType);
            int priority = GetPriority(jobGiverType, 3); // Default to 3
            int tickGroups = GetTickGroups(jobGiverType, 8); // Default to 8
            int? interval = GetInterval(jobGiverType, Utility_JobGiverTickManager.LOW_PRIORITY_INTERVAL);

            Utility_JobGiverTickManager.RegisterJobGiver(
                jobGiverType,
                workTag,
                priority,
                interval,
                tickGroups);

            if (Utility_DebugManager.ShouldLogDetailed())
            {
                Utility_DebugManager.LogNormal($"Registered background JobGiver: {jobGiverType.Name}, WorkType: {workTag}, Priority: {priority}, Tick Groups: {tickGroups}");
            }
        }

        private static string GetWorkTagFromType(Type jobGiverType)
        {
            // Try to get WorkTag property or extract from class name
            try
            {
                // Create an instance to access instance property
                var instance = Activator.CreateInstance(jobGiverType) as JobGiver_PawnControl;
                if (instance != null)
                    return instance.WorkTag;
            }
            catch { }

            // Fall back to name-based extraction
            string name = jobGiverType.Name;
            foreach (string segment in name.Split('_'))
            {
                // Common work types in RimWorld
                if (new[] { "Firefighter", "Doctor", "Patient", "Construction", "Growing",
                            "Mining", "Handling", "Cooking", "Hauling", "Cleaning",
                            "Research", "Crafting", "Art", "Warden", "Hunting", "Childcare", "DarkStudy" }.Contains(segment))
                {
                    return segment;
                }
            }

            return "Unknown";
        }

        private static int GetPriority(Type jobGiverType, int defaultValue)
        {
            // Try to get from static property first
            try
            {
                var property = jobGiverType.GetProperty("RecommendedPriority",
                    BindingFlags.Public | BindingFlags.Static);
                if (property != null && property.PropertyType == typeof(int))
                    return (int)property.GetValue(null);
            }
            catch { }

            // Then try to infer from GetBasePriority
            try
            {
                var method = jobGiverType.GetMethod("GetBasePriority",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (method != null)
                {
                    var instance = Activator.CreateInstance(jobGiverType) as JobGiver_PawnControl;
                    if (instance != null)
                    {
                        float priority = (float)method.Invoke(instance, new object[] { instance.WorkTag });
                        return (int)Math.Round(priority);
                    }
                }
            }
            catch { }

            return defaultValue;
        }

        private static int GetTickGroups(Type jobGiverType, int defaultValue)
        {
            // Try to get from static property first
            try
            {
                var property = jobGiverType.GetProperty("RecommendedTickGroups",
                    BindingFlags.Public | BindingFlags.Static);
                if (property != null && property.PropertyType == typeof(int))
                    return (int)property.GetValue(null);
            }
            catch { }

            return defaultValue;
        }

        private static int? GetInterval(Type jobGiverType, int defaultValue)
        {
            // Try to get from static property first
            try
            {
                var property = jobGiverType.GetProperty("RecommendedInterval",
                    BindingFlags.Public | BindingFlags.Static);
                if (property != null && property.PropertyType == typeof(int))
                    return (int)property.GetValue(null);

                // Check for nullable int version too
                if (property != null && property.PropertyType == typeof(int?))
                    return (int?)property.GetValue(null);
            }
            catch { }

            return defaultValue;
        }
    }
}