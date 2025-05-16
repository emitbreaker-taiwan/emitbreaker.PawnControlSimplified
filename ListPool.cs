using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Utility class for pooling List<T> instances to reduce per-tick GC allocations.
    /// </summary>
    public static class ListPool<T>
    {
        // Internal stack of available lists
        private static readonly Stack<List<T>> pool = new Stack<List<T>>();

        /// <summary>
        /// Rent a List<T> from the pool, or create a new one if empty.
        /// </summary>
        public static List<T> Get()
        {
            return pool.Count > 0 ? pool.Pop() : new List<T>();
        }

        /// <summary>
        /// Return a List<T> to the pool after clearing its contents.
        /// </summary>
        public static void Return(List<T> toReturn)
        {
            toReturn.Clear();
            pool.Push(toReturn);
        }
    }
}