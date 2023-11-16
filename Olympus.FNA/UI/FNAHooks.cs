// #define FNAHOOKS_RENDERTARGETDISCARDCLEAR

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using SDL2;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OlympUI {
    public static class FNAHooks {

        public static bool ApplyWindowChangesWithoutRestore = false;
        public static bool ApplyWindowChangesWithoutResize = false;
        public static bool ApplyWindowChangesWithoutCenter = false;
#if FNAHOOKS_RENDERTARGETDISCARDCLEAR
        public static bool RenderTargetDiscardClear = false;
#endif
        public static event Action<Game>? ExposeEvent;

        public static string? FNA3DDriver;
        public static FNA3DDeviceInfo? FNA3DDevice;
        public static event Action? FNA3DDeviceUpdated;

        private static ILHook? ApplyWindowChangesPatch;
        private static ILHook? DebugFNA3DPatch;
        public static Hook? RedrawWindowPatch;
#if FNAHOOKS_RENDERTARGETDISCARDCLEAR
        private static ILHook? DisableRenderTargetDiscardClearPatch;
#endif

        private static Action<RenderTarget2D, RenderTargetUsage>? _SetRenderTargetUsage;

        public static void Apply() {
            Undo();

            Type? t_FNAHooks =
                typeof(FNAHooks)
                ?? throw new Exception("Olympus without FNAPatches?");
            Type? t_SDL2_FNAPlatform =
                typeof(Game).Assembly.GetType("Microsoft.Xna.Framework.SDL2_FNAPlatform")
                ?? throw new Exception("FNA without SDL2_FNAPlatform?");

            typeof(GraphicsDevice).GetField("DiscardColor", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, new Vector4(0f, 0f, 0f, 0f));

            using (DynamicMethodDefinition dmd = new("FNAHooks.SetRenderTargetUsage", typeof(void), new Type[] { typeof(RenderTarget2D), typeof(RenderTargetUsage) })) {
                ILProcessor il = dmd.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Callvirt, il.Import(
                    typeof(RenderTarget2D).GetProperty(nameof(RenderTarget2D.RenderTargetUsage))?.GetSetMethod(true)
                    ?? throw new Exception("FNA without RenderTarget2D.RenderTargetUsage?")
                ));
                il.Emit(OpCodes.Ret);
                _SetRenderTargetUsage = dmd.Generate().CreateDelegate<Action<RenderTarget2D, RenderTargetUsage>>();
            }

            ApplyWindowChangesPatch = new(
                t_SDL2_FNAPlatform.GetMethod("ApplyWindowChanges"),
                il => {
                    ILCursor c = new(il);
                    c.GotoNext(i => i.MatchCall(typeof(SDL), "SDL_RestoreWindow"));
                    c.Next.Operand = il.Import(t_FNAHooks.GetMethod(nameof(ApplyWindowChangesPatch_RestoreWindow), BindingFlags.NonPublic | BindingFlags.Static));
                    c.GotoNext(i => i.MatchCall(typeof(SDL), "SDL_SetWindowSize"));
                    c.Next.Operand = il.Import(t_FNAHooks.GetMethod(nameof(ApplyWindowChangesPatch_SetWindowSize), BindingFlags.NonPublic | BindingFlags.Static));
                    c.GotoNext(i => i.MatchCall(typeof(SDL), "SDL_SetWindowPosition"));
                    c.Next.Operand = il.Import(t_FNAHooks.GetMethod(nameof(ApplyWindowChangesPatch_SetWindowPosition), BindingFlags.NonPublic | BindingFlags.Static));
                }
            );

            string? debugFNA3D = Environment.GetEnvironmentVariable("OLYMPUS_DEBUG_FNA3D");
            if (debugFNA3D == "0" || debugFNA3D == "1") {
                DebugFNA3DPatch = new(
                    typeof(GraphicsDevice).GetConstructor(new Type[] { typeof(GraphicsAdapter), typeof(GraphicsProfile), typeof(PresentationParameters) }),
                    il => {
                        ILCursor c = new(il);
                        c.GotoNext(
                            i => i.MatchLdcI4(0) || i.MatchLdcI4(1),
                            i => i.MatchCall("Microsoft.Xna.Framework.Graphics.FNA3D", "FNA3D_CreateDevice")
                        );
                        c.Next.OpCode = debugFNA3D == "0" ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1;
                    }
                );
            }

#if FNAHOOKS_RENDERTARGETDISCARDCLEAR
            DisableRenderTargetDiscardClearPatch = new(
                typeof(GraphicsDevice).GetMethod(nameof(GraphicsDevice.SetRenderTargets))
                ?? throw new Exception("FNA without GraphicsDevice.SetRenderTargets?"),
                il => {
                    ILCursor c = new(il);
                    c.GotoNext(
                        i => i.MatchCallOrCallvirt<GraphicsDevice>("Clear")
                    );
                    c.Next.OpCode = OpCodes.Call;
                    c.Next.Operand = il.Import(t_FNAHooks.GetMethod(nameof(GraphicsDevice_RenderTargetDiscardClear), BindingFlags.NonPublic | BindingFlags.Static));
                }
            );
#endif

            RedrawWindowPatch = new Hook(
                typeof(Game).GetMethod("RedrawWindow", BindingFlags.Instance | BindingFlags.NonPublic),
                (Action<Game> orig, Game game) => {
                    ExposeEvent?.Invoke(game);
                    orig(game);
                }
            );

            FNALoggerEXT.LogInfo += OnLogInfo;
        }

        public static void Undo() {
            ApplyWindowChangesPatch?.Dispose();
            DebugFNA3DPatch?.Dispose();
#if FNAHOOKS_RENDERTARGETDISCARDCLEAR
            DisableRenderTargetDiscardClearPatch?.Dispose();
#endif
            RedrawWindowPatch?.Dispose();
            FNALoggerEXT.LogInfo -= OnLogInfo;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetRenderTargetUsage(this RenderTarget2D self, RenderTargetUsage value) {
            _SetRenderTargetUsage!.Invoke(self, value);
        }

        private static void ApplyWindowChangesPatch_RestoreWindow(IntPtr window) {
            if (!ApplyWindowChangesWithoutRestore)
                SDL.SDL_RestoreWindow(window);
        }

        private static void ApplyWindowChangesPatch_SetWindowSize(IntPtr window, int width, int height) {
            if (!ApplyWindowChangesWithoutResize)
                SDL.SDL_SetWindowSize(window, width, height);
        }

        private static void ApplyWindowChangesPatch_SetWindowPosition(IntPtr window, int x, int y) {
            if (!ApplyWindowChangesWithoutCenter)
                SDL.SDL_SetWindowPosition(window, x, y);
        }

#if FNAHOOKS_RENDERTARGETDISCARDCLEAR
        private static void GraphicsDevice_RenderTargetDiscardClear(GraphicsDevice gd, ClearOptions options, Vector4 color, float depth, int stencil) {
            if (RenderTargetDiscardClear)
                gd.Clear(options, color, depth, stencil);
        }
#endif

        private static void OnLogInfo(string line) {
            Console.WriteLine(line);

            if (line.StartsWith("FNA3D Driver: ")) {
                FNA3DDriver = line.Substring("FNA3D Driver: ".Length).Trim();

            } else if (line.StartsWith("D3D11 Adapter: ")) {
                FNA3DDevice = new FNA3DD3D11DeviceInfo(line.Substring("D3D11 Adapter: ".Length).Trim());
                FNA3DDeviceUpdated?.Invoke();

            } else if (line.StartsWith("Vulkan Device: ")) {
                FNA3DDevice = new FNA3DVulkanDeviceInfo(line.Substring("Vulkan Device: ".Length).Trim());
                FNA3DDeviceUpdated?.Invoke();

            } else if (line.StartsWith("OpenGL Renderer: ")) {
                FNA3DDevice = new FNA3DOpenGLDeviceInfo(line.Substring("OpenGL Renderer: ".Length).Trim());
                FNA3DDeviceUpdated?.Invoke();
            }

            FNA3DDevice?.OnLogInfo(line);
        }

    }

    public abstract class FNA3DDeviceInfo {

        public readonly string Device;

        protected FNA3DDeviceInfo(string device) {
            Device = device;
        }

        public virtual void OnLogInfo(string line) {
        }

        public override string ToString() => Device;

    }

    public class FNA3DD3D11DeviceInfo : FNA3DDeviceInfo {

        public FNA3DD3D11DeviceInfo(string device) : base(device) {
        }

        public override string ToString() => $"{Device} (using D3D11)";

    }

    public class FNA3DVulkanDeviceInfo : FNA3DDeviceInfo {

        public string? Driver { get; private set; }

        public FNA3DVulkanDeviceInfo(string device) : base(device) {
        }

        public override void OnLogInfo(string line) {
            base.OnLogInfo(line);

            if (line.StartsWith("Vulkan Driver: ")) {
                Driver = line.Substring("Vulkan Driver: ".Length).Trim();
            }
        }

        public override string ToString() => $"{Device} (using Vulkan: {Driver})";

    }

    public class FNA3DOpenGLDeviceInfo : FNA3DDeviceInfo {

        public string? Driver { get; private set; }
        public string? Vendor { get; private set; }

        public FNA3DOpenGLDeviceInfo(string device) : base(device) {
        }

        public override void OnLogInfo(string line) {
            base.OnLogInfo(line);

            if (line.StartsWith("OpenGL Driver: ")) {
                Driver = line.Substring("OpenGL Driver: ".Length).Trim();

            } else if (line.StartsWith("OpenGL Vendor: ")) {
                Vendor = line.Substring("OpenGL Vendor: ".Length).Trim();
            }
        }

        public override string ToString() => $"{Device} (using OpenGL Driver: {Vendor} - {Driver})";

    }
}
