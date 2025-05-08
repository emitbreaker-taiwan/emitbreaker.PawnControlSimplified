using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Class to store runtime-added mod extensions for saving/loading
    /// </summary>
    public class RuntimeModExtensionRecord : IExposable
    {
        // The defName of the ThingDef this extension applies to
        public string targetDefName;

        // The actual mod extension instance with all configured settings
        public NonHumanlikePawnControlExtension extension;

        // Default constructor for deserialization
        public RuntimeModExtensionRecord() { }

        // Constructor for creating new records
        public RuntimeModExtensionRecord(string defName, NonHumanlikePawnControlExtension ext)
        {
            targetDefName = defName;
            extension = ext;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref targetDefName, "targetDefName");
            Scribe_Deep.Look(ref extension, "extension");
        }
    }
}
