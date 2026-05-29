using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace XAnimationEngine
{
    internal static class XAnimationRuntimePlayerLoopRunner
    {
        private sealed class PrepareMarker { }
        private sealed class FinalizeMarker { }

        private static readonly List<XAnimationDriverScheduler> s_Schedulers = new();
        private static bool s_Installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            EnsureInstalled();
            s_Schedulers.Clear();
        }

        internal static void EnsureInstalled()
        {
            if (s_Installed)
            {
                return;
            }

            PlayerLoopSystem playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            if (TryInsertIntoPreLateUpdate(ref playerLoop))
            {
                PlayerLoop.SetPlayerLoop(playerLoop);
                s_Installed = true;
            }
        }

        internal static void Register(XAnimationDriverScheduler scheduler)
        {
            if (scheduler == null)
            {
                return;
            }

            EnsureInstalled();
            if (!s_Schedulers.Contains(scheduler))
            {
                s_Schedulers.Add(scheduler);
            }
        }

        internal static void Unregister(XAnimationDriverScheduler scheduler)
        {
            if (scheduler == null)
            {
                return;
            }

            s_Schedulers.Remove(scheduler);
        }

        private static void PrepareTick()
        {
            float deltaTime = Time.deltaTime;
            for (int i = s_Schedulers.Count - 1; i >= 0; i--)
            {
                XAnimationDriverScheduler scheduler = s_Schedulers[i];
                if (scheduler == null || !scheduler.IsRegisteredForAutomaticUpdate)
                {
                    s_Schedulers.RemoveAt(i);
                    continue;
                }

                scheduler.TickPrepareFromScheduler(deltaTime);
            }
        }

        private static void FinalizeTick()
        {
            for (int i = s_Schedulers.Count - 1; i >= 0; i--)
            {
                XAnimationDriverScheduler scheduler = s_Schedulers[i];
                if (scheduler == null || !scheduler.IsRegisteredForAutomaticUpdate)
                {
                    s_Schedulers.RemoveAt(i);
                    continue;
                }

                scheduler.TickFinalizeFromScheduler();
            }
        }

        private static bool TryInsertIntoPreLateUpdate(ref PlayerLoopSystem root)
        {
            if (root.subSystemList == null)
            {
                return false;
            }

            for (int i = 0; i < root.subSystemList.Length; i++)
            {
                if (root.subSystemList[i].type != typeof(PreLateUpdate))
                {
                    continue;
                }

                PlayerLoopSystem parent = root.subSystemList[i];
                List<PlayerLoopSystem> children = new(parent.subSystemList ?? Array.Empty<PlayerLoopSystem>());
                children.RemoveAll(system =>
                    system.type == typeof(XAnimationRuntimePlayerLoopRunner) ||
                    system.type == typeof(PrepareMarker) ||
                    system.type == typeof(FinalizeMarker));

                int prepareIndex = FindPreLateUpdatePrepareIndex(children);
                children.Insert(prepareIndex, new PlayerLoopSystem
                {
                    type = typeof(PrepareMarker),
                    updateDelegate = PrepareTick,
                });

                int finalizeIndex = FindPreLateUpdateFinalizeIndex(children);
                if (finalizeIndex < 0)
                {
                    finalizeIndex = children.Count;
                }

                children.Insert(finalizeIndex, new PlayerLoopSystem
                {
                    type = typeof(FinalizeMarker),
                    updateDelegate = FinalizeTick,
                });
                parent.subSystemList = children.ToArray();
                root.subSystemList[i] = parent;
                return true;
            }

            return false;
        }

        private static int FindPreLateUpdatePrepareIndex(List<PlayerLoopSystem> children)
        {
            int directorBeginIndex = children.FindIndex(system => IsPlayerLoopType(system, "DirectorUpdateAnimationBegin"));
            if (directorBeginIndex >= 0)
            {
                return directorBeginIndex;
            }

            int legacyAnimationIndex = children.FindIndex(system => IsPlayerLoopType(system, "LegacyAnimationUpdate"));
            if (legacyAnimationIndex >= 0)
            {
                return legacyAnimationIndex;
            }

            return 0;
        }

        private static int FindPreLateUpdateFinalizeIndex(List<PlayerLoopSystem> children)
        {
            int lateUpdateIndex = children.FindIndex(system => system.type == typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate));
            if (lateUpdateIndex >= 0)
            {
                return lateUpdateIndex;
            }

            int directorEndIndex = children.FindIndex(system => IsPlayerLoopType(system, "DirectorUpdateAnimationEnd"));
            if (directorEndIndex >= 0)
            {
                return directorEndIndex + 1;
            }

            return -1;
        }

        private static bool IsPlayerLoopType(PlayerLoopSystem system, string typeName)
        {
            return system.type != null && string.Equals(system.type.Name, typeName, StringComparison.Ordinal);
        }
    }
}
