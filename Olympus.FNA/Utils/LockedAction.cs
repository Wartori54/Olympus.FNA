using System;
using System.Collections.Generic;

namespace Olympus.Utils {

    public class LockedAction<T>  {

        private Action<T> actionHolder;
        public bool IsRunning { get; private set; } = false;
        private Queue<T> queuedRuns = new();

        public LockedAction(Action<T> action) {
            actionHolder = action;
        }

        public bool TryRun(T obj, bool enqueueRuns = true) {
            if (!IsRunning) {
                Run(obj);
                return true; // it ran
            }

            if (!enqueueRuns) return false; // run will be discarded
            
            queuedRuns.Enqueue(obj);
            return true; // it will be ran
        }

        private void Run(T obj) {
            IsRunning = true;
            actionHolder(obj);
            // after running it, check if anything was queued
            if (queuedRuns.Count != 0) {
                while (queuedRuns.TryDequeue(out T? newObj)) {
                    actionHolder(newObj);
                }
            }
            IsRunning = false;
        }
    }
}
