using Microsoft.Xna.Framework;
using OlympUI;
using Olympus.NativeImpls;
using System;
using System.IO;

namespace Olympus {
    public class ConfigurationScene : Scene {

        public override bool Alert => true;

        public override Element Generate()
            => new ScrollBox() {
                Cached = false,
                Clip = false,
                Layout = {
                    Layouts.Fill()
                },
                Content = new Group() {
                    Style = {
                        { Group.StyleKeys.Padding, 8 },
                        { Group.StyleKeys.Spacing, 32 }
                    },
                    Layout = {
                        Layouts.Fill(1, 0),
                        Layouts.Column()
                    },
                    Children = {

                        new Group() {
                            Style = {
                                { Group.StyleKeys.Spacing, 8 }
                            },
                            Layout = {
                                Layouts.Fill(1, 0),
                                Layouts.Column()
                            },
                            Children = {
                                new HeaderMedium("Options"),
                                new Group() {
                                    Style = {
                                        { Group.StyleKeys.Spacing, 8 }
                                    },
                                    Layout = {
                                        Layouts.Fill(1, 0),
                                        Layouts.Column()
                                    },
                                    Children = {
                                        new Label("TODO"),
                                        new Label("TODO"),
                                        new Label("TODO"),
                                        new Label("TODO"),
                                        new Button("Switch between theme", b => {
                                            foreach (IGameComponent? component in App.Components) {
                                                if (component is not MainComponent mainComponent) continue;
                                                UI.Run(() => {
                                                    mainComponent.Native.DarkMode = !mainComponent.Native.DarkMode;
                                                });
                                                break;

                                            }
                                        }),
                                    }
                                },
                            }
                        },

                        new Group() {
                            Style = {
                                { Group.StyleKeys.Spacing, 8 }
                            },
                            Layout = {
                                Layouts.Fill(1, 0),
                                Layouts.Column()
                            },
                            Children = {
                                new HeaderMedium("About Olympus"),
                                new Group() {
                                    Style = {
                                        { Group.StyleKeys.Spacing, 8 }
                                    },
                                    Layout = {
                                        Layouts.Fill(1, 0),
                                        Layouts.Column()
                                    },
                                    Children = {
                                        new Label($"Version: {App.Version} @ {Path.GetDirectoryName(typeof(App).Assembly.Location)}"),
                                        new Label($".NET Runtime: {Environment.Version} @ {Path.GetDirectoryName(typeof(object).Assembly.Location)}"),
                                        new Label($"System: {Environment.OSVersion} (using {NativeImpl.Native.GetType().Name})"),
                                        new Label($"Renderer: {FNAHooks.FNA3DDevice}"),
                                    }
                                },
                            }
                        },

                    },
                }
            };

    }

}
