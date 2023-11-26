using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI.MegaCanvas;
using Olympus;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OlympUI {
    public static class UI {

        public static Thread MainThread = Thread.CurrentThread;
        public static bool IsOnMainThread => MainThread == Thread.CurrentThread;

        public static readonly Root Root = new();

        public static int MultiSampleCount = 4;

        public static Vector2 TransformOffset;

        public static uint GlobalReflowID = 1;
        public static uint GlobalRepaintID = 1;
        public static uint GlobalUpdateID = 1;
        public static uint GlobalDrawID = 1;

        public static bool GlobalDrawDebug = false;

        public static Element? Hovering;
        public static Element? Dragging;
        public static Element? Focusing;

        public static bool? ForceMSAA = null;
        public static bool ForceDisableCache = false;

        private static readonly RasterizerState _RasterizerStateCullCounterClockwiseUnscissoredWithMSAA = new() {
            Name = $"{nameof(UI)}.{nameof(RasterizerStateCullCounterClockwiseUnscissoredWithMSAA)}",
            CullMode = CullMode.CullCounterClockwiseFace,
            ScissorTestEnable = false,
            MultiSampleAntiAlias = true,
        };
        private static readonly RasterizerState _RasterizerStateCullCounterClockwiseUnscissoredNoMSAA = new() {
            Name = $"{nameof(UI)}.{nameof(RasterizerStateCullCounterClockwiseUnscissoredNoMSAA)}",
            CullMode = CullMode.CullCounterClockwiseFace,
            ScissorTestEnable = false,
            MultiSampleAntiAlias = false,
        };

        public static RasterizerState RasterizerStateCullCounterClockwiseUnscissoredWithMSAA =>
            (ForceMSAA ?? true) ? _RasterizerStateCullCounterClockwiseUnscissoredWithMSAA :
            _RasterizerStateCullCounterClockwiseUnscissoredNoMSAA;

        public static RasterizerState RasterizerStateCullCounterClockwiseUnscissoredNoMSAA =>
            (ForceMSAA ?? false) ? _RasterizerStateCullCounterClockwiseUnscissoredWithMSAA :
            _RasterizerStateCullCounterClockwiseUnscissoredNoMSAA;

#pragma warning disable CS8618 // Initialize and LoadContent always run first.
        public static Game Game;
        public static UINativeImpl Native;
        public static SpriteBatch SpriteBatch;
        public static CanvasManager MegaCanvas;
#pragma warning restore CS8618

        private static readonly AutoRotatingPool<List<Action>> RunListPool = new(8);
        private static List<Action> RunList = RunListPool.Next();
        private static List<Action>? IteratingRunList;
        private static IEnumerator<Action>? RunListEnumerator = null;
        private static readonly AutoRotatingPool<List<Action>> RunLateListPool = new(8);
        private static List<Action> RunLateList = RunLateListPool.Next();
        private static HashSet<Action> RunOnceList = new();

        private static Point WHPrev;

        private static bool ReflowingPrev;

        public static void Initialize(Game game, UINativeImpl native, IReloadableTemporaryContext reloadableTmpCtx) {
            Game = game;
            Native = native;
            Reloadable.TemporaryContext = reloadableTmpCtx;

            UIInput.Initialize();

            UIInput.OnFastClick += OnFastClick;
        }

        public static void LoadContent() {
            SpriteBatch?.Dispose();
            SpriteBatch = new SpriteBatch(Game.GraphicsDevice);
            MegaCanvas?.Dispose();
            MegaCanvas = new(Game) {
                MultiSampleCount = MultiSampleCount
            };
        }

        private static void OnFastClick(int x, int y, MouseButtons btn) {
            Element? el = UIInput.MouseFocus ? Root.GetInteractiveChildAt(UIInput.Mouse.ToPoint()) : null;
            if (el != Focusing) {
                el?.InvokeUp(new FocusEvent.Unfocus());
                Focusing = el;
                el?.InvokeUp(new FocusEvent.Focus());
            }
            el?.InvokeUp(new MouseEvent.Click() {
                Button = btn,
                Dragging = false
            });
        }

        public static void Update(float dt) {
            GlobalUpdateID++;

            bool forceReflow = WHPrev != Root.WH || Root.ReflowingForce;
            WHPrev = Root.WH;
            Root.Reflowing |= forceReflow;
            Root.ReflowingForce = false;

            RunOnceList.Clear();

            #region RunList
            {
                // Here we are running all the queued actions we can before exceeding a maximum time
                // if that's the case we'll just stop and continue next frame 
                if (RunListEnumerator == null || IteratingRunList == null)
                    lock (RunListPool) {
                        IteratingRunList = RunList;
                        RunListEnumerator = IteratingRunList.GetEnumerator();
                        RunList = RunListPool.Next();
                    }

                bool outOfTime = false;
                DateTime startingTime = DateTime.Now;
                while (RunListEnumerator.MoveNext()) {
#if DEBUG
                    DateTime lastUpdateTime = DateTime.Now;
#endif
                    Action run = RunListEnumerator.Current;
                    run();
#if DEBUG
                    if (DateTime.Now - lastUpdateTime > App.UpdateDelaySpan) {
                        AppLogger.Log.Debug("A single UI Update took more than max amount");
                    }
#endif
                    if (DateTime.Now - startingTime > App.UpdateDelaySpan) {
                        outOfTime = true;
                        break;
                    }

                }

                if (!outOfTime) {
                    lock (RunListPool) {
                        RunListEnumerator.Dispose();
                        RunListEnumerator = null;
                        IteratingRunList.Clear();
                        IteratingRunList = null;
                    }
                }

            }
            #endregion

            UIDraw.Update();

            #region Input and other event handling
            {

                UIInput.Update();

                if (UIInput.MouseDXY != default || ReflowingPrev) {
                    Element? hoveringPrev = Hovering;
                    Element? hoveringNext = Hovering = UIInput.MouseFocus ? Root.GetInteractiveChildAt(UIInput.Mouse.ToPoint()) : null;

                    if (hoveringPrev != hoveringNext) {
                        hoveringPrev?.InvokeUp(new MouseEvent.Leave());
                        hoveringNext?.InvokeUp(new MouseEvent.Enter());
                    }

                    if (UIInput.MouseDXY != default) {
                        Dragging?.InvokeUp(new MouseEvent.Drag());
                    }
                }

                for (MouseButtons btn = MouseButtons.First; btn <= MouseButtons.Last; btn = (MouseButtons) ((int) btn << 1)) {
                    if (UIInput.Pressed(btn)) {
                        if (Dragging is null || Dragging == Hovering) {
                            Focusing?.InvokeUp(new FocusEvent.Unfocus());
                            Dragging = Hovering;
                            Focusing = Hovering;
                            Hovering?.InvokeUp(new FocusEvent.Focus());
                            Hovering?.InvokeUp(new MouseEvent.Press() {
                                Button = btn,
                                Dragging = true
                            });
                        } else {
                            Hovering?.InvokeUp(new MouseEvent.Press() {
                                Button = btn,
                                Dragging = false
                            });
                        }
                    } else if (UIInput.Released(btn)) {
                        if (UIInput.MousePresses == 0) {
                            Element? dragging = Dragging;
                            Dragging = null;
                            dragging?.InvokeUp(new MouseEvent.Release() {
                                Button = btn,
                                Dragging = false
                            });
                            if (dragging == Hovering) {
                                dragging?.InvokeUp(new MouseEvent.Click() {
                                    Button = btn,
                                    Dragging = false
                                });
                            }
                        } else {
                            Dragging?.InvokeUp(new MouseEvent.Release() {
                                Button = btn,
                                Dragging = true
                            });
                        }
                    }
                }

                if (UIInput.MouseScrollDXY != default)
                    Hovering?.InvokeUp(new MouseEvent.Scroll());

            }
            #endregion

            #region Main loop
            {
                ReflowingPrev = false;

                Style.UpdateTypeStyles(dt);

                do {
                    List<Element> allOffScreen = Root.AllOffScreen;
                    List<Element> allOnScreen = Root.AllOnScreen;
                    foreach (Element el in allOffScreen) {
                        if (!el.UpdatePending)
                            continue;
                        bool revive = el.RevivePending;
                        el.UpdatePending = false;

                        if (!el.Awakened) {
                            el.Awakened = true;
                            el.Awake();

                            if (revive)
                                el.Revive();

                            el.Update(dt);
                            el.UpdateHiddenTime = 0f;

                        } else {
                            if (revive)
                                el.Revive();

                            el.UpdateHiddenTime += dt;
                            el.UpdateHidden(dt);
                        }
                    }

                    foreach (Element el in allOnScreen) {
                        if (!el.UpdatePending)
                            continue;
                        bool revive = el.RevivePending;
                        el.UpdatePending = false;

                        if (!el.Awakened) {
                            el.Awakened = true;
                            el.Awake();
                        }

                        if (revive)
                            el.Revive();

                        float udt = dt + el.UpdateHiddenTime;
                        el.Update(udt);
                        el.UpdateHiddenTime = 0f;
                    }

                    bool reflowing = Root.Reflowing;
                    if (Root.Recollecting) {
                        reflowing = true;
                        Root.CollectLayoutPasses();
                    }

                    if (reflowing || forceReflow) {
                        ReflowingPrev = true;
                        Root.INTERNAL_ReflowLoopReset();
                        FirstPass:
                        Root.Reflowing = false; // Set to false early so that it can be set to true only when necessary.
                        LayoutEvent reflow = LayoutEvent.Instance;
                        foreach (LayoutPass pass in Root.AllLayoutPasses) {
                            reflow.ForceReflow = forceReflow ? LayoutForce.All : LayoutForce.None;
                            reflow.Pass = pass;
                            reflow.Recursive = true;
                            // No need to InvokeDown as reflows follow their own recursion rules.
                            Root.Invoke(reflow);
                            if (Root.Reflowing) {
                                Root.INTERNAL_ReflowLoopCount();
                                goto FirstPass;
                            }
                        }
                    }

                    if (!Root.Recollecting && !reflowing)
                        break;

                    Root.Collect();
                    Root.Recollecting = false;

                } while (true);


            }
            #endregion

            #region RunLateList
            {

                List<Action> runLateList;
                lock (RunLateListPool) {
                    runLateList = RunLateList;
                    RunLateList = RunLateListPool.Next();
                }
                foreach (Action runLate in runLateList)
                    runLate();
                runLateList.Clear();

            }
            #endregion

            Root.Repainting |= forceReflow;
        }

        public static void UpdateDraw() {
            Game.GraphicsDevice.Textures[0] = null;
            MegaCanvas.Update();
        }

        public static void Paint() {
            GlobalDrawID = GlobalUpdateID;

            Root.Paint();

            UIDraw.Draw();
        }

        public static MaybeAwaitable Run(Action run) {
            MaybeAwaitable awaitable = new(() => true);
            Action runReal = () => {
                run();
                awaitable.SetResult();
            };
            lock (RunListPool)
                RunList.Add(runReal);
            return awaitable;
        }

        public static void RunLate(Action runLate) {
            lock (RunLateListPool)
                RunLateList.Add(runLate);
        }

        public static void RunOnce(Action runLate) {
            lock (RunOnceList)
                if (RunOnceList.Add(runLate))
                    runLate();
        }

        public static Matrix CreateTransform()
            => CreateTransform(TransformOffset);
        public static Matrix CreateTransform(Vector2 offset) {
            offset += TransformOffset;
            Viewport view = Game.GraphicsDevice.Viewport;
            return Matrix.CreateOrthographicOffCenter(
                -offset.X, -offset.X + view.Width,
                -offset.Y + view.Height, -offset.Y,
                view.MinDepth, view.MaxDepth
            );
        }

        public static void BeginUI(this SpriteBatch batch)
            => batch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.Default,
                RasterizerStateCullCounterClockwiseUnscissoredWithMSAA,
                null,
                Matrix.CreateTranslation(new(TransformOffset, 0f))
            );

        public static void DrawDebugRect(this SpriteBatch batch, Color color, Rectangle rect) {
            batch.Draw(Assets.White.Value, new Rectangle(rect.Left, rect.Top, 1, rect.Height), color);
            batch.Draw(Assets.White.Value, new Rectangle(rect.Right - 1, rect.Top, 1, rect.Height), color);
            batch.Draw(Assets.White.Value, new Rectangle(rect.Left + 1, rect.Top, rect.Width - 2, 1), color);
            batch.Draw(Assets.White.Value, new Rectangle(rect.Left + 1, rect.Bottom - 1, rect.Width - 2, 1), color);
        }

    }
}
