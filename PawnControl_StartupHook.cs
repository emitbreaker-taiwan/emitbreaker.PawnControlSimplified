using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    [StaticConstructorOnStartup]
    public static class PawnControl_StartupHook
    {
        static PawnControl_StartupHook()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (GameComponent_SimpleNonHumanlikePawnControl.Instance == null && Current.Game != null)
                {
                    GameComponent_SimpleNonHumanlikePawnControl.Instance =
                        Current.Game.GetComponent<GameComponent_SimpleNonHumanlikePawnControl>();
                }
            });
        }
    }
}
