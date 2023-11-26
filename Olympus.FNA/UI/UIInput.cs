using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SDL2;
using System;
using System.Collections.Generic;

namespace OlympUI {
    public static unsafe class UIInput {

        public static KeyboardState KeyboardPrev;
        public static KeyboardState Keyboard;
        public static MouseState MousePrev;
        public static MouseState Mouse;
        
        private static HashSet<Keys> KeyboardRepeated = new();
        private static HashSet<Keys> KeyboardRepeatedNext = new();

        public static bool MouseFocus;
        public static int MousePresses;

        public static Point MouseDXY => new(
            Mouse.X - MousePrev.X,
            Mouse.Y - MousePrev.Y
        );
        public static Point MouseScrollDXY => new(0, (Mouse.ScrollWheelValue - MousePrev.ScrollWheelValue) / 120);

        public static event Action<int, int, MouseButtons>? OnFastClick;

        private static bool PrevSet;

        private static int MouseButtonsSubFrame;

        public static void Initialize() {
            SDL.SDL_GetEventFilter(out EventFilterPrev, out IntPtr userdata);
            SDL.SDL_SetEventFilter(EventFilter, userdata);
        }

        private static SDL.SDL_EventFilter? EventFilterPrev;
        private static readonly SDL.SDL_EventFilter EventFilter = (userdata, evtPtr) => {
            SDL.SDL_Event* evt = (SDL.SDL_Event*) evtPtr;

            switch (evt->type) {
                case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN when evt->button.windowID == SDL.SDL_GetWindowID(UI.Game.Window.Handle):
                    MouseButtonsSubFrame |= 1 << evt->button.button;
                    break;

                case SDL.SDL_EventType.SDL_MOUSEBUTTONUP when evt->button.windowID == SDL.SDL_GetWindowID(UI.Game.Window.Handle):
                    if ((MouseButtonsSubFrame & (1 << evt->button.button)) != 0) {
                        MouseButtonsSubFrame &= ~(1 << evt->button.button);
                        MouseState mouseReal = Microsoft.Xna.Framework.Input.Mouse.GetState();
                        OnFastClick?.Invoke(mouseReal.X, mouseReal.Y, (MouseButtons) evt->button.button);
                    }
                    break;
                case SDL.SDL_EventType.SDL_KEYDOWN when evt->button.windowID == SDL.SDL_GetWindowID(UI.Game.Window.Handle):
                    KeyboardRepeatedNext.Add(FNAHooks.ToXNAKey(ref evt->key.keysym));
                    break;
            }

            return EventFilterPrev?.Invoke(userdata, evtPtr) ?? 1;
        };

        public static void Update() {
            MouseButtonsSubFrame = 0;

            KeyboardPrev = Keyboard;
            Keyboard = Microsoft.Xna.Framework.Input.Keyboard.GetState();
            MousePrev = Mouse;
            MouseState mouseReal = Microsoft.Xna.Framework.Input.Mouse.GetState();
            KeyboardRepeated.Clear();
            foreach (var key in KeyboardRepeatedNext)
                KeyboardRepeated.Add(key);
            KeyboardRepeatedNext.Clear();

            Point mouseOffs = UI.Native.MouseOffset;
            Mouse = new(
                mouseReal.X + mouseOffs.X,
                mouseReal.Y + mouseOffs.Y,
                mouseReal.ScrollWheelValue,
                mouseReal.LeftButton,
                mouseReal.MiddleButton,
                mouseReal.RightButton,
                mouseReal.XButton1,
                mouseReal.XButton2
            );

            MouseFocus = UI.Native.CaptureMouse || UI.Native.IsMouseFocus || MousePresses > 0;
            if (!MouseFocus) {
                Mouse = new(
                    Mouse.X,
                    Mouse.Y,
                    Mouse.ScrollWheelValue,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released
                );
            }

            if (!PrevSet) {
                PrevSet = true;
                KeyboardPrev = Keyboard;
                MousePrev = Mouse;
            }

            int presses = MousePresses;
            for (MouseButtons btn = MouseButtons.First; btn <= MouseButtons.Last; btn = (MouseButtons) ((int) btn << 1)) {
                if (Pressed(btn)) {
                    presses++;
                    if (presses == 1)
                        UI.Native.CaptureMouse = true;

                } else if (Released(btn)) {
                    presses--;
                    if (presses == 0)
                        UI.Native.CaptureMouse = false;
                }
            }

            MouseFocus = MouseFocus || presses > 0;
            MousePresses = presses;
        }

        public static bool Down(Keys key)
            => Keyboard.IsKeyDown(key);

        public static bool Up(Keys key)
            => !Keyboard.IsKeyDown(key);

        public static bool Pressed(Keys key)
            => !KeyboardPrev.IsKeyDown(key) && Keyboard.IsKeyDown(key);
        
        public static bool PressedWithRepeat(Keys key)
            => (!KeyboardPrev.IsKeyDown(key) && Keyboard.IsKeyDown(key)) || KeyboardRepeated.Contains(key);

        public static bool Released(Keys key)
            => KeyboardPrev.IsKeyDown(key) && !Keyboard.IsKeyDown(key);

        public static bool IsButtonDown(this MouseState state, MouseButtons button) =>
            button switch {
                MouseButtons.Left => state.LeftButton,
                MouseButtons.Right => state.RightButton,
                MouseButtons.Middle => state.MiddleButton,
                MouseButtons.X1 => state.XButton1,
                MouseButtons.X2 => state.XButton2,
                _ => ButtonState.Released,
            } == ButtonState.Pressed;

        public static bool Down(MouseButtons button)
            => Mouse.IsButtonDown(button);

        public static bool Up(MouseButtons button)
            => !Mouse.IsButtonDown(button);

        public static bool Pressed(MouseButtons button)
            => !MousePrev.IsButtonDown(button) && Mouse.IsButtonDown(button);

        public static bool Released(MouseButtons button)
            => MousePrev.IsButtonDown(button) && !Mouse.IsButtonDown(button);

    }

    public enum MouseButtons {
        Left    = 0b00001,
        Right   = 0b00010,
        Middle  = 0b00100,
        X1      = 0b01000,
        X2      = 0b10000,

        First   = 0b00001,
        Last    = 0b10000,
    }
}
