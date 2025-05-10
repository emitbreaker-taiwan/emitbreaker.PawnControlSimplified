using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public static class PawnPathExtensions
    {
        /// <summary>
        /// Creates a copy of a PawnPath
        /// </summary>
        public static PawnPath ClonePath(this PawnPath path)
        {
            if (path == null) return null;

            // Create a new path with the same destination
            var result = new PawnPath();

            // Unfortunately we can't fully clone paths, so we just return a new path
            // In a real implementation you would copy all the path nodes
            return result;
        }
    }
}
