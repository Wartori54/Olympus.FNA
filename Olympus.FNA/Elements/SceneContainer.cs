using OlympUI;

namespace Olympus {

    public class SceneContainer : Element {
        private Scene scene = null!; // Fake CS8618 warning, it cannot happen

        public Scene Scene {
            get => scene;
            set {
                scene?.Leave();
                scene = value;
                UI.Root.InvalidateForce();
                Children.Clear();
                Children.Add(scene.Root);
                scene.Enter(/* no args */);
            }
        }

        protected override bool IsComposited => true;

        public SceneContainer(Scene scene) {
            Scene = scene;
        }



    }
}