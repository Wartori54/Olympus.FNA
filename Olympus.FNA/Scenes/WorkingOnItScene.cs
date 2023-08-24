using Microsoft.Xna.Framework;
using OlympUI;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Olympus {

    public class WorkingOnItScene : Scene {
        public override bool Locked => true;

        private Task<IAsyncEnumerable<EverestInstaller.Status>>? job;

        private float v = 0f;

        private readonly Channel<string> logs = Channel.CreateBounded<string>(1024);

        private float buttPos = 0;

        public override Element Generate()
            => new Group() {
                ID = "base",
                Layout = {
                    Layouts.Row(),
                    Layouts.Fill(1,1),
                },
                Children = {
                    // new Group() {
                    //     Layout = {
                    //         Layouts.Fill(1,0),
                    //         Layouts.Row(),
                    //     },
                    //     Children = {
                    //         new Label("teeeeeeeeeeeeeeeeeeeest"),
                    //         new Button("exit", b => {
                    //             Scener.PopFront();
                    //             Scener.Set<HomeScene>();
                    //         }),
                    //         new SVGImage("installshapes/done.svg", () => v),
                    //         new ScrollBox() {
                    //             ID = "logs",
                    //             Layout = {
                    //                 Layouts.Column(),
                    //                 Layouts.Fill(0, 1),
                    //             },
                    //             Clip = true,
                    //             Children = {
                    //                 new Group() {
                    //                     ID = "content",
                    //                     Layout = {
                    //                         Layouts.Column(),
                    //                     }
                    //                 }
                    //             }
                    //         },
                    //     }
                    // },
                    new Canvas() {
                        Init = RegisterRefresh<Canvas>(async canvas => {
                            int count = 5;
                            string path = "installshapes/download_rot.svg";
                            SVGObject obj = new SVGObject(Encoding.Default.GetString(OlympUI.Assets.OpenData(path)
                                ?? throw new FileNotFoundException($"Couldn't find asset: {path}")));
                            Point imgTargetSize = new(100, 100);
                            await UI.Run(() => {
                                for (int i = 0; i < count; i++) {
                                    SVGImage img = new(obj, (img, dt) => {
                                        float p = img.RealProgress + 0.1f * dt;
                                        if (p > 1) {
                                            p = 0;
                                        }
                                        return ((p+0.5f)%1, p);
                                    });

                                    int iCaptured = i;
                                    canvas.Content.Add((img, (dt, size, el) => {
                                        SVGImage img = (SVGImage) el;
                                        Vector2 pos = img.XY;
                                        const float speed = 000;
                                        if (pos.Y > size.Y + img.H / 4f) {
                                            pos.Y = -img.H * 5 / 4f;
                                        }

                                        img.AutoW = imgTargetSize.X;
                                        if (size.X / count < img.W) {
                                            img.AutoW = size.X / count;
                                        }

                                        return (new Vector2(size.X / (float) count * iCaptured, pos.Y + speed*dt),
                                            new Point(-1, -1));
                                    }));

                                }
                            });

                        }),
                        Content = {
                            
                        }
                    }
                }
            };

        // public override Element PostGenerate(Element root) {
        //     Task.Run(async () => {
        //         await foreach (string msg in logs.Reader.ReadAllAsync()) {
        //             UI.Run(() =>
        //                 Root.GetChild<Group>().GetChild<ScrollBox>("logs").Content.Add(new Label(msg))
        //             );
        //             Console.WriteLine(msg);
        //         }
        //     });
        //     return base.PostGenerate(root);
        // }

        // public override void Enter(params object[] args) {
        //     Root.GetChild<Group>().GetChild<ScrollBox>("logs").Content.Clear();
        //     
        //     if (args.Length != 1)
        //         throw new ApplicationException("WorkingOnItScene entered with a invalid set of arguments!");
        //     if (args[0] is not Task<IAsyncEnumerable<EverestInstaller.Status>> task) {
        //         throw new ApplicationException("WorkingOnItScene entered without a valid job!");
        //     }
        //
        //     if (job != null && !job.IsCompleted)
        //         throw new ApplicationException("WorkingOnItScene reentered before old job was finished!");
        //
        //     job = task;
        //
        //     Task.Run(async () => {
        //         await foreach (EverestInstaller.Status status in await job) {
        //             v = status.Progress;
        //             await logs.Writer.WriteAsync(status.Text);
        //             if (status.CurrentStage != EverestInstaller.Status.Stage.InProgress) {
        //                 UI.Run(() => {
        //                     Root.Add(new Button("Done. Exit?", b => {
        //                         Scener.Set<HomeScene>();
        //                     }));
        //                 });
        //             }
        //         }
        //     });
        //     
        //     job.Start();
        //
        //     base.Enter(args);
        // }


        public static async IAsyncEnumerable<EverestInstaller.Status> DummyJob() {
            await Task.Delay(1000);
            for (int i = 0; i < 1000; i++) {
                yield return new EverestInstaller.Status($"step {i}", i/1000f, EverestInstaller.Status.Stage.InProgress);
                await Task.Delay(2);
            }
            
            yield return new EverestInstaller.Status($"step {69696969}", 1f, EverestInstaller.Status.Stage.Fail);
        }
    }
}