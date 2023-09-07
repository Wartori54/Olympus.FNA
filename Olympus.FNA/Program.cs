using Mono.Options;
using MonoMod.Utils;
using OlympUI;
using Olympus.NativeImpls;
using SDL2;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

namespace Olympus {
    public class Program {

        public static void Main(string[] args) {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            } catch (NotSupportedException) {
                Console.WriteLine("TLS 1.3 NOT SUPPORTED! CONTINUE AT YOUR OWN RISK!");
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }

            bool forceSDL2 = Environment.GetEnvironmentVariable("OLYMPUS_FORCE_SDL2") == "1";

            // FIXME: For some reason DWM hates FNA3D's D3D11 renderer and misrepresents the backbuffer too often on multi-GPU setups?!
            // FIXME: Default to D3D11, but detect multi-GPU setups and use the non-Intel GPU with OpenGL (otherwise buggy drivers).
            if (PlatformHelper.Is(Platform.Windows) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNA3D_FORCE_DRIVER"))) {
                // Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "Vulkan");
                // Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "OpenGL");
                // Environment.SetEnvironmentVariable("FNA3D_OPENGL_FORCE_COMPATIBILITY_PROFILE", "1");
            }

            // Crappy DirectInput drivers can cause Olympus to hang for a minute when starting up.
            // This is new: https://github.com/libsdl-org/SDL/commit/6a2e6c82a0764a00123447d93999ebe14d509aa8
            if (Environment.GetEnvironmentVariable("OLYMPUS_DIRECTINPUT_ENABLED") != "1") {
                SDL.SDL_SetHint("SDL_DIRECTINPUT_ENABLED", "0");
            }

            bool help = false;
            bool console = false;
            OptionSet options = new() {
                { "h|help", "Show this message and exit.", v => help = v is not null },
                { "force-sdl2", "Force using the SDL2 native helpers.", v => forceSDL2 = v is not null },
            };

#if DEBUG
            console = true;
#else
            if (PlatformHelper.Is(Platform.Windows)) {
                options.Add("console", "Open a debug console.", v => console = v is not null);
            }
#endif

            List<string> extra;
            try {
                // parse the command line
                extra = options.Parse(args);
            } catch (OptionException e) {
                Console.Write("Olympus CLI error: ");
                Console.WriteLine(e.Message);
                help = true;
            }

            if (help) {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            if (PlatformHelper.Is(Platform.Windows) && console) {
                AllocConsole();
                Console.SetError(Console.Out);
            }

            External.DllManager.PrepareResolver(typeof(Program).Assembly);
            // External.DllManager.PrepareResolver(typeof(Microsoft.Xna.Framework.Game).Assembly);

            FNAHooks.Apply();

            if (forceSDL2) {
                NativeImpl.Native = new NativeSDL2();

            } else if (PlatformHelper.Is(Platform.Windows)) {
#if WINDOWS
                NativeImpl.Native = new NativeWin32();
#else
                Console.WriteLine("Olympus compiled without Windows dependencies, using NativeSDL2");
                NativeImpl.Native = new NativeSDL2();
#endif
            } else if (PlatformHelper.Is(Platform.Linux)) {
#if WINDOWS
                Console.WriteLine("Olympus compiled with Windows dependencies and running on linux");
#endif
                NativeImpl.Native = new NativeLinux();
            } else {
                NativeImpl.Native = new NativeSDL2();
            }

            try {
                using (NativeImpl.Native)
                    NativeImpl.Native.Run();
            } catch (Exception ex) { // Native.Run() can be mulithreaded, force the exit to make sure no hanging threads stay alive
                Console.WriteLine("Exception on init! ");
                Console.Error.WriteLine(ex);
                Environment.Exit(-1);
            }
        }


        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

    }
}
