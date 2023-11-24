using OlympUI;
using System;
using System.Collections.Generic;

namespace Olympus {
    public static class Scener {

        private static readonly Dictionary<Type, Scene> Generated = new();

        public static readonly Stack<Scene> Scenes = new();
        public static Scene? Scene => Scenes.Count == 0 ? null : Scenes.Peek();

        public static readonly Group SceneContainer = new() {
            Layout = {
                Layouts.Fill()
            }
        };

        public static readonly Stack<Scene> Alerts = new();
        public static Scene? Alert => Alerts.Count == 0 ? null : Alerts.Peek();

        public static readonly Group AlertContainer = new() {
            Layout = {
                Layouts.Fill()
            }
        };

        public static Scene? Front => Alert ?? Scene;

        public static event Action<Scene?, Scene?>? SceneChanged;
        public static event Action<Scene?, Scene?>? AlertChanged;
        public static event Action<Scene?, Scene?>? FrontChanged;

        public static Func<Element, Element>? AlertWrap;

        public static void Update(float dt) {
            foreach (Scene scene in Scenes) {
                scene.Update(dt);
                if (!scene.Alert)
                    break;
            }
        }

        public static T Get<T>() where T : Scene, new() {
            if (!Generated.TryGetValue(typeof(T), out Scene? scene))
                Generated[typeof(T)] = scene = new T();
            return (T) scene;
        }

        public static T Set<T>(params object[] args) where T : Scene, new()
            => (T) Set(Get<T>(), args);

        public static Scene Set(Scene scene, params object[] args) {
            Stack<Scene> stack;
            Group container;
            Action<Scene?, Scene?>? listeners;
            Func<Element, Element>? wrap;

            if (scene.Alert) {
                stack = Alerts;
                container = AlertContainer;
                listeners = AlertChanged;
                wrap = AlertWrap;
                container.Children.Clear();
            } else {
                stack = Scenes;
                container = SceneContainer;
                listeners = SceneChanged;
                wrap = null;
                container.Children.Clear();
            }

            Scene? prevFront = Front;
            Scene? prev = stack.Count == 0 ? null : stack.Peek();

            Element el = scene.Root;
            if (wrap is not null)
                el = wrap(el);
            container.Children.Add(el);
            UI.Root.InvalidateForce();

            foreach (Scene old in stack)
                old.Leave();
            stack.Clear();
            stack.Push(scene);
            scene.Enter(args);

            listeners?.Invoke(prev, scene);
            FrontChanged?.Invoke(prev, scene);

            return scene;
        }

        public static T Push<T>(params object[] args) where T : Scene, new()
            => (T) Push(Get<T>(), args);

        public static Scene Push(Scene scene, params object[] args) {
            Stack<Scene> stack;
            Group container;
            Action<Scene?, Scene?>? listeners;
            Func<Element, Element>? wrap;

            if (scene.Alert) {
                stack = Alerts;
                container = AlertContainer;
                listeners = AlertChanged;
                wrap = AlertWrap;
            } else {
                stack = Scenes;
                container = SceneContainer;
                listeners = SceneChanged;
                wrap = null;
                container.Children.Clear();
            }

            Scene? prevFront = Front;
            Scene? prev = stack.Count == 0 ? null : stack.Peek();

            Element el = scene.Root;
            if (wrap is not null)
                el = wrap(el);
            container.Children.Add(el);
            UI.Root.InvalidateForce();

            prev?.Leave();
            stack.Push(scene);
            scene.Enter(args);

            listeners?.Invoke(prev, scene);
            FrontChanged?.Invoke(prev, scene);

            return scene;
        }

        public static Scene? PopScene() {
            if (Scenes.Count <= 1)
                return null;

            Stack<Scene> stack = Scenes;
            Group container = SceneContainer;
            Action<Scene?, Scene?>? listeners = SceneChanged;
            container.Children.Clear();

            Scene? prevFront = Front;
            Scene prev = stack.Pop();
            Scene scene = stack.Peek();

            container.Children.Add(scene.Root);
            UI.Root.InvalidateForce();

            prev?.Leave();
            scene.Enter();

            listeners?.Invoke(prev, scene);
            FrontChanged?.Invoke(prev, scene);

            return scene;
        }

        public static Scene? PopAlert() {
            if (Scenes.Count <= 0)
                return null;

            Stack<Scene> stack = Alerts;
            Group container = AlertContainer;
            Action<Scene?, Scene?>? listeners = AlertChanged;
            container.Children.RemoveAt(container.Children.Count - 1);

            Scene? prevFront = Front;
            Scene prev = stack.Pop();
            Scene? scene = stack.Count == 0 ? null : stack.Peek();

            UI.Root.InvalidateForce();

            prev?.Leave();
            scene?.Enter();

            listeners?.Invoke(prev, scene);
            FrontChanged?.Invoke(prev, scene);

            return scene;
        }

        public static Scene? PopFront() {
            Scene? front = Front;
            if (front is null)
                return null;
            if (front.Alert)
                return PopAlert();
            return PopScene();
        }
        
#if DEBUG
        public static Scene Regenerate(Type sceneType) {
            Generated[sceneType] = Activator.CreateInstance(sceneType) as Scene 
                                   ?? throw new ArgumentException($"Couldn't instantiate {sceneType}, is it a scene?");
            return Generated[sceneType];
        }
        
        public static void RefreshAll() {
            foreach (var sceneType in Generated.Keys) {
                Regenerate(sceneType).Refresh();
            }
        }
#endif
    }
}
