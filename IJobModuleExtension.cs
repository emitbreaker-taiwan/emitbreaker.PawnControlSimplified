using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Interface for module extensions that add compatibility with other mods
    /// </summary>
    public interface IJobModuleExtension
    {
        /// <summary>
        /// Called when a map's cache is being cleaned up
        /// </summary>
        void OnMapCacheCleanup(int mapId);
    }
}
