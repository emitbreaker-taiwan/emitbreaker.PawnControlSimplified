using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    public abstract class ScopedFlagContextBase<T> where T : ScopedFlagContextBase<T>, new()
    {
        [ThreadStatic]
        private static int _depth;

        public static bool IsOverrideActive => _depth > 0;

        public static IDisposable Begin()
        {
            _depth++;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (!disposed)
                {
                    _depth--;
                    disposed = true;
                }
            }
        }
    }
}
