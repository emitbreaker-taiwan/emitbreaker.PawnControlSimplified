using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Interface for virtual tag storage, allowing modular and testable implementations.
    /// </summary>
    public interface IVirtualTagStorage
    {
        List<string> Get(ThingDef def);
        void Set(ThingDef def, List<string> tags);
        void Add(ThingDef def, string tag);
        void Remove(ThingDef def, string tag);
        bool HasVirtualTags(ThingDef def);
        void Clear();
        Dictionary<string, List<string>> Export();
        void LoadFrom(Dictionary<string, List<string>> data);
        void LoadFromXmlFile(string path);
        void SaveToXmlFile(string path);
        IReadOnlyDictionary<string, List<string>> AllVirtualDefs { get; }
        Dictionary<ThingDef, List<string>> GetAll();
        VirtualNonHumanlikePawnControlExtension GetOrCreate(ThingDef def);
    }
}
