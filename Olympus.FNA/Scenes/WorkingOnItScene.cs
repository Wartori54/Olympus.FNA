using Microsoft.Xna.Framework;
using OlympUI;
using System;
using System.Threading.Tasks;
using System.Timers;

namespace Olympus {

    public class WorkingOnItScene : Scene {
        public override bool Locked => true;

        private float v = 0f;
        private float vspeed = 0.01f;

        private Timer? timer = null;

        private Timer SetTimer() {
            Timer newTimer = new Timer(10);

            newTimer.Elapsed += (sender, args) => {
                vspeed += -0.0000999999f/2;
                v += vspeed;
                if (v > 1f || vspeed < 0f) {
                    v = 0f;
                    vspeed = 0.01f;
                }
            };
            newTimer.AutoReset = true;
            newTimer.Enabled = true;
            return newTimer;
        }

        public override Element Generate()
            => new Group() {
                ID = "base",
                Layout = {
                    Layouts.Row(),
                },
                Children = {
                    new Label("teeeeeeeeeeeeeeeeeeeest"),
                    new Button("exit", b => {
                        Scener.PopFront();
                        Scener.Set<HomeScene>();
                    }),
                    new SVGImage("installshapes/monomod2.svg", () => v) 
                }
            };

        public override Element PostGenerate(Element root) {
            timer = SetTimer();
            return base.PostGenerate(root);
        }
    }
}