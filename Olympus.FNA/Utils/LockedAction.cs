using System;
using System.Collections.Generic;

namespace Olympus.Utils {

    public class LockedAction<T>  {

        private Action<T> actionHolder;
        private bool isRunning = false;
        private Queue<T> queuedRuns = new();

        public LockedAction(Action<T> action) {
            actionHolder = action;
        }

        public bool TryRun(T obj, bool enqueueRuns = true) {
            if (!isRunning) {
                Run(obj);
                return true; // it ran
            }

            if (!enqueueRuns) return false; // run will be discarded
            
            queuedRuns.Enqueue(obj);
            return true; // it will be ran
        }

        private void Run(T obj) {
            isRunning = true;
            actionHolder(obj);
            // after running it, check if anything was queued
            if (queuedRuns.Count != 0) {
                while (queuedRuns.TryDequeue(out T? newObj)) {
                    actionHolder(newObj);
                }
            }
            isRunning = false;
        }
    }
}
