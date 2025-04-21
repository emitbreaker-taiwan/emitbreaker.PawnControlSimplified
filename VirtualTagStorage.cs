using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Central static reference to access the service via interface.
    /// </summary>
    public static class VirtualTagStorage
    {
        public static IVirtualTagStorage Instance = new VirtualTagStorageService();
    }
}
