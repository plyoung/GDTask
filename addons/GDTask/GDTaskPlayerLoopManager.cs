﻿#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Linq;
using GDTask.Internal;
using System.Threading;
using Godot;

namespace GDTask
{
    public static class GDTaskLoopRunners
    {
        public struct GDTaskLoopRunnerProcess { };
        public struct GDTaskLoopRunnerPhysicsProcess { };
    }

    public enum PlayerLoopTiming
    {
        Process = 1,
        PhysicsProcess = 2,
    }

    [Flags]
    public enum InjectPlayerLoopTimings
    {
        /// <summary>
        /// Preset: All loops(default).
        /// </summary>
        All = Process | PhysicsProcess,

        /// <summary>
        /// Preset: All without last except LastPostLateUpdate.
        /// </summary>
        Standard = Process | PhysicsProcess,

        /// <summary>
        /// Preset: Minimum pattern, Update | PhysicsProcess | LastPostLateUpdate
        /// </summary>
        Minimum =
            Process | PhysicsProcess,

        // PlayerLoopTiming

        PhysicsProcess = 1,
        Process = 2,
    }

    public interface IPlayerLoopItem
    {
        bool MoveNext();
    }

    /// <summary>
    /// Singleton that forwards Godot calls and values to GDTasks.
    /// </summary>
    public class GDTaskPlayerLoopManager : Node
    {
        public static int MainThreadId => Global.mainThreadId;
        public static bool IsMainThread => System.Threading.Thread.CurrentThread.ManagedThreadId == Global.mainThreadId;
        public static void AddAction(PlayerLoopTiming timing, IPlayerLoopItem action) => Global.LocalAddAction(timing, action);
        public static void ThrowInvalidLoopTiming(PlayerLoopTiming playerLoopTiming) => throw new InvalidOperationException("Target playerLoopTiming is not injected. Please check PlayerLoopHelper.Initialize. PlayerLoopTiming:" + playerLoopTiming);
        public static void AddContinuation(PlayerLoopTiming timing, Action continuation) => Global.LocalAddContinuation(timing, continuation);

        public void LocalAddAction(PlayerLoopTiming timing, IPlayerLoopItem action)
        {
            var runner = runners[(int)timing];
            if (runner == null)
            {
                ThrowInvalidLoopTiming(timing);
            }
            runner.AddAction(action);
        }

        // NOTE: Continuation means a asynchronous task invoked by another task after the other task finishes.
        public void LocalAddContinuation(PlayerLoopTiming timing, Action continuation)
        {
            var q = yielders[(int)timing];
            if (q == null)
            {
                ThrowInvalidLoopTiming(timing);
            }
            q.Enqueue(continuation);
        }

        public static GDTaskPlayerLoopManager Global { get; private set; }
        public float DeltaTime => GetProcessDeltaTime();
        public float PhysicsDeltaTime => GetPhysicsProcessDeltaTime();

        private int mainThreadId;
        private ContinuationQueue[] yielders;
        private PlayerLoopRunner[] runners;

        public override void _Ready()
        {
            if (Global != null)
            {
                QueueFree();
                return;
            }
            Global = this;

            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            yielders = new[] {
                new ContinuationQueue(PlayerLoopTiming.Process),
                new ContinuationQueue(PlayerLoopTiming.PhysicsProcess),
            };
            runners = new[] {
                new PlayerLoopRunner(PlayerLoopTiming.Process),
                new PlayerLoopRunner(PlayerLoopTiming.PhysicsProcess),
            };
        }

        public override void _Notification(int what)
        {
            if (what == NotificationPredelete)
            {
                if (Global == this)
                    Global = null;
                if (yielders != null)
                {
                    foreach (var yielder in yielders)
                        yielder.Clear();
                    foreach (var runner in runners)
                        runner.Clear();
                }
            }
        }

        public override void _Process(float delta)
        {
            yielders[0].Run();
            runners[0].Run();
        }

        public override void _PhysicsProcess(float delta)
        {
            yielders[1].Run();
            runners[1].Run();
        }
    }
}
