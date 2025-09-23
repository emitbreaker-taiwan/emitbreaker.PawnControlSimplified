using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    [StaticConstructorOnStartup]
    public static class FloatMenuOptionProviderInitializer
    {
        static FloatMenuOptionProviderInitializer()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    RegisterCustomFloatMenuProviders();
                    Utility_DebugManager.LogNormal("FloatMenuOptionProviderInitializer completed successfully");
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error in FloatMenuOptionProviderInitializer: {ex}");
                }
            });
        }

        private static void RegisterCustomFloatMenuProviders()
        {
            var providersField = AccessTools.Field(typeof(FloatMenuMakerMap), "providers");
            if (providersField?.GetValue(null) is List<FloatMenuOptionProvider> providers)
            {
                int beforeCount = providers.Count;

                // 커스텀 프로바이더 추가
                providers.Add(new FloatMenuOptionProvider_DraftedTendNonHumanlike());
                providers.Add(new FloatMenuOptionProvider_DraftedRepairNonHumanlike());

                int afterCount = providers.Count;
                Utility_DebugManager.LogNormal($"Registered {afterCount - beforeCount} custom FloatMenu providers (Total: {afterCount})");

                // 등록된 프로바이더 목록 로그 (디버그용)
                if (Utility_DebugManager.ShouldLog())
                {
                    foreach (var provider in providers)
                    {
                        if (provider.GetType().Namespace?.Contains("emitbreaker") == true)
                        {
                            Utility_DebugManager.LogNormal($"Custom provider registered: {provider.GetType().Name}");
                        }
                    }
                }
            }
            else
            {
                Utility_DebugManager.LogError("Failed to access FloatMenuMakerMap.providers field");
            }
        }
    }
}
