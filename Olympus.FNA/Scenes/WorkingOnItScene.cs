using Microsoft.Xna.Framework;
using OlympUI;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Timers;
using Point = Microsoft.Xna.Framework.Point;

namespace Olympus {

    public class WorkingOnItScene : Scene {
        public override bool Locked => locked;
        private bool locked = true;
        public override bool Alert => true;

        private const float SpeedX = -0.05f;
        private const float SpeedY = 0.05f;
        
        private Vector2 offset = Vector2.Zero;
        private const float ImgSize = 1f;
        private string imageTarget = "";
        
        private readonly Label logLabel = new("") {
            Style = {
                OlympUI.Assets.FontMonoSpace,
            },
        }; // global because it needs to survive the panel regenerations
        
        private readonly Group accLogLabel = new() {
            Layout = {
                Layouts.Column(),
            }
            
        }; // global because it needs to survive the panel regenerations
        
        private bool expandedView = false;
        
        private Action? CentralPanelRefresh;

        private Timer? t;

        private Job? currJob;

        public void SetTimer() {
            if (t != null) return;
            t = new Timer(1000f/60);
            t.AutoReset = true;
            t.Enabled = true;
            t.Elapsed += (sender, args) => {
                if (sender is not Timer s) return;
                
                offset = keepInBounds(offset, ImgSize);
                
                offset += new Vector2(SpeedX * (float) s.Interval/1000f, SpeedY * (float) s.Interval/1000f);

            };
            t.Start();
        }

        private static Vector2 keepInBounds(Vector2 vec, float offset) {
            vec = new Vector2(vec.X, vec.Y);
            if (vec.Y > 1 + offset) {
                vec.Y = -offset;
            } else if (vec.Y < -offset) {
                 vec.Y = 1 + offset;
            }
            
            if (vec.X > 1 + offset) {
                vec.X = -offset;
            } else if (vec.X < -offset) {
                vec.X = 1 + offset;
            }

            return vec;
        }

        public override Element Generate()
            => new Group() {
                ID = "base",
                Layout = {
                    Layouts.Row(),
                    Layouts.Fill(1,1),
                },
                Children = {
                    new Canvas() {
                        Init = RegisterRefresh<Canvas>(async canvas => {
                            int countX = 1;
                            int countY = 1;
                            string path = $"installshapes/{(imageTarget == "" ? "test" : imageTarget)}.svg";
                            SVGObject obj = new SVGObject(Encoding.Default.GetString(OlympUI.Assets.OpenData(path)
                                ?? throw new FileNotFoundException($"Couldn't find asset: {path}")));
                            await UI.Run(() => {
                                
                                canvas.Content.Clear();
                                for (int i = -1; i < countX + 1; i++) {
                                    for (int j = -1; j < countY + 1; j++) {
                                        SVGImage img = new(obj, (img, dt) => {
                                            float p = img.RealProgress + 0.1f * dt;
                                            if (p > 1) {
                                                p = 0;
                                            }

                                            p = 1f;

                                            return ((p + 1f) % 1, p);
                                        });
                                        
                                        int iCaptured = i;
                                        int jCaptured = j;
                                        canvas.Content.Add((img, (dt, el) => {
                                            SVGImage img = (SVGImage) el;
                                            
                                            Vector2 initialPos = new(iCaptured/(float)countX + 0.5f,
                                                jCaptured/(float)countY);
                                            
                                            Vector2 calcPos = new(initialPos.X + offset.X, initialPos.Y + offset.Y);
                                            if (calcPos.X > 1 + ImgSize) {
                                                calcPos.X -= 1 + ImgSize * 2;
                                            } else if (calcPos.X < -ImgSize) {
                                                calcPos.X += 1 + ImgSize * 2;
                                            }
                                            if (calcPos.Y > 1 + ImgSize) {
                                                calcPos.Y -= 1 + ImgSize * 2;
                                            } else if (calcPos.Y < -ImgSize) {
                                                calcPos.Y += 1 + ImgSize * 2;
                                            }
                                            
                                            return (calcPos, 
                                                new Vector2(1f, 1f));
                                        }));

                                    }
                                }
                                SetTimer();
                                Panel panel = new() {
                                    Layout = { Layouts.Column(), },
                                };
                                CentralPanelRefresh = () => {
                                    Label progressLabel = new HeaderSmall("") {
                                        Layout = {
                                            Layouts.Left(0.5f, -0.5f),
                                        }
                                    };
                                    float animFloat = 0f;
                                    UI.Run(() => {
                                        locked = !currJob?.Done ?? true;
                                        panel.Clear();
                                        panel.Add(new Group() {
                                            Layout = {
                                                Layouts.Column(),
                                                Layouts.Left(0.5f, -0.5f),
                                            },
                                            Children = {
                                                new SVGImage(currJob?.Icon ?? SVGImage.ERROR,
                                                    (image, dt) => {
                                                        progressLabel.Text = MathF.Round(currJob?.Progress*100 ?? 0) + "%"; // ik its kinda jank, but it works
                                                        if (currJob?.Done ?? false) {
                                                            animFloat += (1 - animFloat) / 20;
                                                            return (0, animFloat);
                                                        }
                                                        return (0, currJob?.Progress ?? 0f);
                                                    }) {
                                                    WH = new (250, 250),
                                                },
                                                progressLabel,
                                            }
                                        });
                                        Task.Run(async () => {
                                                if (currJob == null) return;
                                                await foreach (string status in currJob.Logs.Reader.ReadAllAsync()) {
                                                    UI.Run(() => {
                                                        logLabel.Text = status;
                                                        accLogLabel.Children.Add(new Label(status) {
                                                            Style = { OlympUI.Assets.FontMonoSpace, }
                                                        });
                                                        if (accLogLabel.Children.Count > 100) {
                                                            accLogLabel.Children.RemoveAt(0);
                                                        }
                                                    });
                                                }
                                            }
                                        );
                                        panel.Add(new Panel() {
                                            Layout = {
                                                Layouts.Fill(1, expandedView ? 0.5f : 0f, panel.Style.GetCurrent<Padding>().W), // TODO: fix panels in panels
                                            },
                                            Children = {
                                                new Group() {
                                                    Clip = true,
                                                    Layout = {
                                                        Layouts.Fill(1, 1),
                                                    },
                                                    Children = {
                                                        expandedView ? new ScrollBox() {
                                                            BottomSticky = true,
                                                            Layout = {
                                                                Layouts.Fill(),
                                                            },
                                                            Content = accLogLabel,
                                                        } : logLabel,
                                                    }
                                                },
                                                new Button(expandedView ? "Reduce" : "Expand", b => {
                                                    expandedView = !expandedView;
                                                    CentralPanelRefresh?.Invoke();
                                                }) {
                                                    Layout = {
                                                        Layouts.Right(6 + 8), // 6: scroll handle width, 8: default panel spacing
                                                    },
                                                    Style = {
                                                        { Group.StyleKeys.Padding, new Padding(8, 4) },
                                                    },
                                                }
                                            }
                                        });

                                    });
                                };
                                CentralPanelRefresh.Invoke();
                                canvas.Content.Add((panel, (dt, el) => {
                                    float width = 0.7f;
                                    float height = 0.5f;
                                    Vector2 calcSize = canvas.GetSize(panel.WH);
                                    Vector2 finalSize = new(MathF.Max(width, /*calcSize.X*/width), MathF.Max(height, calcSize.Y)); // Only do for height since TODO: width is buggy
                                    return (new Vector2(0.5f - finalSize.X/2, 0.5f - finalSize.Y/2), finalSize);
                                }));
                            });

                        }),
                    },
                    
                }
            };


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

        public override void Enter(params object[] args) {
            if (args.Length != 1 && args.Length != 2)
                throw new ApplicationException("WorkingOnItScene entered with a invalid set of arguments!");
            if (args[0] is not Job job || job == null) {
                throw new ApplicationException("WorkingOnItScene entered without a valid job!");
            }

            if (args.Length != 1) {
                imageTarget = (string) args[1]; 
            }

            currJob = job;

            Task.Run(async () => {
                Console.WriteLine("Starting job");
                try {
                    await currJob.StartRoutine();
                } catch (Exception ex) {
                    Console.WriteLine("Job exception:");
                    Console.WriteLine(ex);
                    
                }

                Console.WriteLine("Finished job");
                CentralPanelRefresh?.Invoke();
            });
            logLabel.Text = "";
            accLogLabel.Children.Clear();
            
            base.Enter(args);
        }

        public override void Leave() {
            if (!currJob?.Done ?? true) {
                throw new InvalidOperationException("Exit attempt without finishing job");
            }
            currJob = null;
            base.Leave();
            t?.Close();
            t = null;
        }

        public static Job GetDummyJob() {
            async IAsyncEnumerable<EverestInstaller.Status> DummyJob() {
                await Task.Delay(1000);
                for (int i = 0; i < 50; i++) {
                    yield return new EverestInstaller.Status($"step {i}", i / 50f,
                        EverestInstaller.Status.Stage.InProgress);
                    await Task.Delay(100);
                }

                yield return new EverestInstaller.Status($"step {69696969}", 1f, EverestInstaller.Status.Stage.Success);
            }
            
            return new Job(DummyJob, new SVGObject(Encoding.Default.GetString(OlympUI.Assets.OpenData("installshapes/monomod2.svg") 
                            ?? throw new FileNotFoundException($"Couldn't find asset: installshapes/monomod2.svg"))));
        }

        public class Job {
            public readonly Func<IAsyncEnumerable<EverestInstaller.Status>> Routine;
            public SVGObject Icon { get; private set; }
            private bool started = false;
            
            public readonly Channel<string> Logs = Channel.CreateBounded<string>(1024);
            public bool Done { get; private set; }
            public float Progress { get; private set; }


            public Job(Func<IAsyncEnumerable<EverestInstaller.Status>> routine, string icon) 
                : this(routine, new SVGObject(
                    Encoding.Default.GetString(OlympUI.Assets.OpenData($"installshapes/{icon}.svg") 
                        ?? throw new FileNotFoundException($"Couldn't find asset: installshapes/{icon}.svg"))))
            {}

            public Job(Func<IAsyncEnumerable<EverestInstaller.Status>> routine, SVGObject icon) {
                Routine = routine;
                Icon = icon;
            }

            public async Task StartRoutine() {
                if (started) throw new InvalidOperationException("Job was started multiple times");
                started = true;
                await foreach (EverestInstaller.Status status in Routine.Invoke()) {
                    await Logs.Writer.WriteAsync(status.Text);
                    Console.WriteLine(status.Text);
                    Progress = status.Progress == -1 ? Progress : status.Progress;
                    if (status.CurrentStage != EverestInstaller.Status.Stage.InProgress) {
                        Done = true;
                        if (status.CurrentStage == EverestInstaller.Status.Stage.Success) {
                            Icon = SVGImage.DONE;
                        } else {
                            Icon = SVGImage.ERROR;
                        }
                    }
                }
            }

        }

    }
}