using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using Olympus.NativeImpls;
using SDL2;
using System;

namespace Olympus {
    public partial class MetaDebugScene : Scene {


        public bool Real = true;

        public override Element Generate()
            => new Group() {
                ID = "MetaDebugScene",
                Layout = {
                    Layouts.Right(),
                    Layouts.Column(OrdererBehavior.None)
                },
                Children = {
                    // new Button("EXIT") {
                    //     Callback = _ => App.Instance.Exit()
                    // },
                    // new Button("EXIT") {
                    //     Callback = _ => App.Instance.Exit()
                    // },
                    // new Button("EXIT") {
                    //     Callback = _ => App.Instance.Exit()
                    // },
                    // new Button("EXIT") {
                    //     Callback = _ => App.Instance.Exit()
                    // },
                }
            };

    }
}
