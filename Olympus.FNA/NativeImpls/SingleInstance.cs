// This file is here because it contains os dependent code that has to run too early to be put on a NativeImpl

using MonoMod.Utils;
using SDL2;
using System;
using System.Diagnostics;
using System.IO;

namespace Olympus.NativeImpls {
    public static class SingleInstance {
        public static readonly string PidFileName = App.Name + ".pid";
        public static readonly string PidFilePath = Path.Combine(Config.GetDefaultDir(), PidFileName);

        /// <summary>
        /// Checks whether another instance of this same program is already open
        /// </summary>
        /// <returns>Returns true if multiple instances are open</returns>
        public static bool CheckInstances() {
            Process currentProcess = Process.GetCurrentProcess();
            if (File.Exists(PidFilePath)) {
                string text = File.ReadAllText(PidFilePath);
                if (int.TryParse(text, out int parsedPid)) {
                    try {
                        Process foundProcess = Process.GetProcessById(parsedPid);
                        if (foundProcess.ProcessName == currentProcess.ProcessName)
                            return true;
                    } catch (Exception e) {
                        // NOOP
                    }
                }
            }

            // this array is never disposed
            foreach (Process process in Process.GetProcessesByName(currentProcess.ProcessName)) {
                if (currentProcess.Id != process.Id) return true;
            }

            return false;
        }

        public static PidFile WritePidFile() {
            return new PidFile();
        }

        public class PidFile : IDisposable {
            // This should be less accessible
            public PidFile() {
                File.WriteAllText(PidFilePath, Environment.ProcessId.ToString());
            }

            public void Dispose() {
                if (File.Exists(PidFilePath))
                    File.Delete(PidFilePath);
            }
        }

        /// <summary>
        /// Request focus for the currently open window, noop if theres none
        /// </summary>
        public static void RequestFocus() {
            if (PlatformHelper.Is(Platform.Windows)) // on windows its needed to force since it wont work at all because M$
                SDL.SDL_SetHint(SDL.SDL_HINT_FORCE_RAISEWINDOW, "1");
            SDL.SDL_RaiseWindow(App.Instance.Window.Handle);
        }

    }
}