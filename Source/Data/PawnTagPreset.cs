using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    [Serializable]
    public class PawnTagPreset : IExposable
    {
        public string name;
        public string category;
        public List<string> tags = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref category, "category");
            Scribe_Collections.Look(ref tags, "tags", LookMode.Value);
        }
    }
}
