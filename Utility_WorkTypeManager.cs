using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_WorkTypeManager
    {
        public static WorkTypeDef Named(string defName)
        {
            return DefDatabase<WorkTypeDef>.GetNamed(defName);
        }

        /// <summary>
        /// Gets the priority value for a specific work type
        /// </summary>
        /// <param name="workTypeName">The name of the work type</param>
        /// <returns>Priority value (higher = more important)</returns>
        public static float GetWorkTypePriority(string workTypeName)
        {
            if (string.IsNullOrEmpty(workTypeName))
                return 5.0f; // Default medium priority

            switch (workTypeName)
            {
                // Emergency/Critical work types
                case "Firefighter": return 9.0f;
                case "Patient": return 8.8f;
                case "Doctor": return 8.5f;

                // High Priority work types
                case "PatientBedRest": return 8.0f;
                case "BasicWorker": return 7.8f;
                case "Childcare": return 7.5f;
                case "Warden": return 7.2f;
                case "Handling": return 7.0f;
                case "Cooking": return 6.8f;

                // Medium-High Priority work types
                case "Hunting": return 6.5f;
                case "Construction": return 6.2f;
                case "Growing": return 5.8f;
                case "Mining": return 5.5f;

                // Medium Priority work types
                case "PlantCutting": return 5.2f;
                case "Smithing": return 4.9f;
                case "Tailoring": return 4.7f;
                case "Art": return 4.5f;
                case "Crafting": return 4.3f;

                // Low Priority work types
                case "Hauling": return 3.9f;
                case "Cleaning": return 3.5f;
                case "Research": return 3.2f;
                case "DarkStudy": return 3.0f; // Ideology DLC

                // Default for any unspecified work types
                default: return 5.0f;
            }
        }
    }
}
