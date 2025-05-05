using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    public class Utility_IdentityManager_ScopedFlagContext : IDisposable
    {
        private readonly FlagScopeTarget flag;

        public Utility_IdentityManager_ScopedFlagContext(FlagScopeTarget flag)
        {
            this.flag = flag;
            Utility_IdentityManager.SetFlagOverride(flag, true);
        }

        public void Dispose()
        {
            Utility_IdentityManager.SetFlagOverride(flag, false);
        }
    }
}
