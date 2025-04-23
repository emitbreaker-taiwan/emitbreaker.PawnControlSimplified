using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Default implementation of IVirtualTagStorage using GameComponent for persistence.
    /// </summary>
    public class VirtualTagStorageService : IVirtualTagStorage
    {
        // The singleton instance
        private static VirtualTagStorageService _instance;

        // Public static property to access the instance
        public static VirtualTagStorageService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new VirtualTagStorageService();
                }
                return _instance;
            }
        }

        private Dictionary<string, List<string>> GetStore()
        {
            var comp = GameComponent_SimpleNonHumanlikePawnControl.Instance;

            if (comp == null)
            {
                Log.Warning("[PawnControl] Tried to access virtual tag storage before GameComponent was ready.");
                return new Dictionary<string, List<string>>();
            }

            if (comp.serializedVirtualTagBuffer == null)
            {
                Log.Warning("[PawnControl] VirtualTagStorage was null. Initializing.");
                comp.serializedVirtualTagBuffer = new Dictionary<string, List<string>>();
            }

            return comp.serializedVirtualTagBuffer;
        }

        public List<string> Get(ThingDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.defName))
                return new List<string>();

            var store = GetStore();
            List<string> list;
            if (store.TryGetValue(def.defName, out list))
                return new List<string>(list);

            return new List<string>();
        }

        public void Set(ThingDef def, List<string> tags)
        {
            if (def == null || string.IsNullOrEmpty(def.defName))
                return;

            var store = GetStore();
            store[def.defName] = (tags != null)
                ? new List<string>(tags)
                : new List<string>();
        }

        public void Add(ThingDef def, string tag)
        {
            var store = GetStore();
            List<string> list;
            if (!store.TryGetValue(def.defName, out list))
            {
                list = new List<string>();
                store[def.defName] = list;
            }

            if (!list.Contains(tag))
                list.Add(tag);
        }

        public void Remove(ThingDef def, string tag)
        {
            var store = GetStore();
            List<string> list;
            if (store.TryGetValue(def.defName, out list))
                list.Remove(tag);
        }

        public bool HasVirtualTags(ThingDef def)
        {
            return GetStore().ContainsKey(def.defName);
        }

        public void Clear()
        {
            GetStore().Clear();
        }

        public Dictionary<string, List<string>> Export()
        {
            return GetStore()
                .Where(pair => !Utility_ModExtensionResolver.HasPhysicalModExtension(DefDatabase<ThingDef>.GetNamedSilentFail(pair.Key)))
                .ToDictionary(pair => pair.Key, pair => new List<string>(pair.Value));
        }

        public void LoadFrom(Dictionary<string, List<string>> data)
        {
            var comp = GameComponent_SimpleNonHumanlikePawnControl.Instance;
            if (comp != null)
            {
                comp.serializedVirtualTagBuffer = (data != null)
                    ? data
                    : new Dictionary<string, List<string>>();
            }
        }

        public void LoadFromXmlFile(string path)
        {
            if (!File.Exists(path)) return;

            Dictionary<string, List<string>> loaded = new Dictionary<string, List<string>>();
            XmlDocument doc = new XmlDocument();
            doc.Load(path);

            foreach (XmlNode raceNode in doc.SelectNodes("/Defs/PawnTagGroup"))
            {
                string defName = raceNode["defName"] != null ? raceNode["defName"].InnerText : null;
                if (string.IsNullOrWhiteSpace(defName)) continue;

                List<string> tags = new List<string>();
                foreach (XmlNode tagNode in raceNode.SelectNodes("tags/tag"))
                {
                    string tag = tagNode.InnerText != null ? tagNode.InnerText.Trim() : null;
                    if (!string.IsNullOrWhiteSpace(tag))
                        tags.Add(tag);
                }

                if (tags.Count > 0)
                    loaded[defName] = tags;
            }

            LoadFrom(loaded);

            foreach (KeyValuePair<string, List<string>> entry in loaded)
            {
                foreach (string tag in entry.Value)
                {
                    PawnTagDef existing = DefDatabase<PawnTagDef>.GetNamedSilentFail(tag);
                    if (existing == null || (existing.category != "Enum" && existing.category != "Auto"))
                    {
                        Utility_TagCatalog.EnsureTagDefExists(tag, "Imported", "PawnControl_ImportedDesc");
                    }
                }
            }
        }

        public void SaveToXmlFile(string path)
        {
            var store = GetStore();

            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.WriteLine("<Defs>");
                foreach (KeyValuePair<string, List<string>> pair in store)
                {
                    writer.WriteLine("  <PawnTagGroup>");
                    writer.WriteLine(string.Format("    <defName>{0}</defName>", pair.Key));
                    writer.WriteLine("    <tags>");
                    foreach (string tag in pair.Value)
                    {
                        writer.WriteLine(string.Format("      <tag>{0}</tag>", tag));
                    }
                    writer.WriteLine("    </tags>");
                    writer.WriteLine("  </PawnTagGroup>");
                }
                writer.WriteLine("</Defs>");
            }
        }

        public IReadOnlyDictionary<string, List<string>> AllVirtualDefs
        {
            get { return GetStore(); }
        }

        public Dictionary<ThingDef, List<string>> GetAll()
        {
            var store = GetStore();
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => store.ContainsKey(def.defName))
                .ToDictionary(def => def, def => new List<string>(store[def.defName]));
        }

        public VirtualNonHumanlikePawnControlExtension GetOrCreate(ThingDef def)
        {
            var store = GetStore();

            if (!store.TryGetValue(def.defName, out var tags))
            {
                tags = new List<string>();
                store[def.defName] = tags;
            }

            return new VirtualNonHumanlikePawnControlExtension
            {
                tags = tags
            };
        }
    }
}
