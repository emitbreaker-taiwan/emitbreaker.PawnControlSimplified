using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_CEPatchExporter
    {
        public static void ExportToCEPatchFormat(ThingDef def, List<string> tags, string outputPath)
        {
            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Defs");
            doc.AppendChild(root);

            XmlElement cePatch = doc.CreateElement("PatchOperationAddModExtension");
            cePatch.SetAttribute("Class", "PatchOperationAddModExtension");
            root.AppendChild(cePatch);

            XmlElement xpath = doc.CreateElement("xpath");
            xpath.InnerText = $"/Defs/ThingDef[defName=\"{def.defName}\"]";
            cePatch.AppendChild(xpath);

            XmlElement value = doc.CreateElement("value");
            cePatch.AppendChild(value);

            XmlElement ext = doc.CreateElement("li");
            ext.SetAttribute("Class", "emitbreaker.PawnControl.NonHumanlikePawnControlExtension");
            value.AppendChild(ext);

            XmlElement tagsNode = doc.CreateElement("tags");
            foreach (string tag in tags)
            {
                XmlElement tagEl = doc.CreateElement("li");
                tagEl.InnerText = tag;
                tagsNode.AppendChild(tagEl);
            }
            ext.AppendChild(tagsNode);

            doc.Save(outputPath);
        }
    }
}
