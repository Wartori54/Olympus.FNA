using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace OlympUI {
    public sealed class Recorder {

        private static readonly MethodInfo m_ActionCmdListFactory =
            typeof(Recorder).GetMethod(nameof(ActionCmdListFactory), BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new Exception("Couldn't find ActionCmdListFactory");

        private static readonly MethodInfo m_DataCmdListFactory =
            typeof(Recorder).GetMethod(nameof(DataCmdListFactory), BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new Exception("Couldn't find DataCmdListFactory");

        private static readonly ActionCmdList ActionCmdListInstance = new();

        private readonly Dictionary<Type, CmdList> CmdLists;
        private readonly bool CmdListsShared;
        private readonly List<(Type dataType, int id)> Cmds = new();

        public Recorder() {
            CmdLists = new() {
                { typeof(void), ActionCmdListInstance }
            };
        }

        public Recorder(Recorder shared) {
            CmdLists = shared.CmdLists;
            CmdListsShared = true;
        }

        public void Add(Action a)
            => Add(typeof(void), GetList<ActionCmdList>(typeof(void), null).Add(a));

        public void Add<T>(in T data, Action<T> a) where T : struct
            => Add(typeof(T), GetList<ActionCmdList<T>>(typeof(T), m_ActionCmdListFactory).Add(data, a));

        public void Add<T>(in T data) where T : struct, IRecorderCmd
            => Add(typeof(T), GetList<DataCmdList<T>>(typeof(T), m_DataCmdListFactory).Add(data));

        public void Clear() {
            Cmds.Clear();

            if (!CmdListsShared) {
                foreach (CmdList list in CmdLists.Values)
                    list.Clear();
            }
        }

        public void Run() {
            foreach ((Type dataType, int id) in Cmds)
                CmdLists[dataType].Run(id);
        }

        private void Add(Type dataType, int id) {
            Cmds.Add((dataType, id));
        }

        private TList GetList<TList>(Type dataType, MethodInfo? factory) where TList : CmdList {
            if (CmdLists.TryGetValue(dataType, out CmdList? cmdList))
                return (TList) cmdList;

            if (dataType == typeof(void))
                return (TList) (CmdLists[dataType] = new ActionCmdList());

            Debug.Assert(factory is not null);
            return (TList) (CmdLists[dataType] = (CmdList) factory.MakeGenericMethod(dataType).Invoke(null, null)!);
        }

        private static CmdList ActionCmdListFactory<T>() where T : struct
            => new ActionCmdList<T>();

        private static CmdList DataCmdListFactory<T>() where T : struct, IRecorderCmd
            => new DataCmdList<T>();

        private readonly record struct Cmd(int Index, Action Action);
        private readonly record struct Cmd<T>(int Index, Action<T> Action, T Data);

        private abstract class CmdList {
            public abstract void Clear();
            public abstract void Run(int id);
        }

        private sealed class ActionCmdList : CmdList {
            private readonly List<Action> List = new();
#if DEBUG && false
            private readonly List<StackTrace> Traces = new();
#endif

            public int Add(Action a) {
                List.Add(a);
#if DEBUG && false
                Traces.Add(new StackTrace(true));
#endif
                return List.Count - 1;
            }

            public override void Clear() {
                List.Clear();
#if DEBUG && false
                Traces.Clear();
#endif
            }

            public override void Run(int id) {
                List[id]();
            }
        }

        private sealed class ActionCmdList<T> : CmdList where T : struct {
            private readonly List<(T data, Action<T> a)> List = new();
#if DEBUG && false
            private readonly List<StackTrace> Traces = new();
#endif


            public int Add(in T data, Action<T> a) {
                List.Add((data, a));
#if DEBUG && false
                Traces.Add(new StackTrace(true));
#endif
                return List.Count - 1;
            }

            public override void Clear() {
                List.Clear();
#if DEBUG && false
                Traces.Clear();
#endif
            }

            public override void Run(int id) {
                (T data, Action<T> a) = List[id];
                a(data);
            }
        }

        private sealed class DataCmdList<T> : CmdList where T : IRecorderCmd {
            private readonly List<T> List = new();
#if DEBUG && false
            private readonly List<StackTrace> Traces = new();
#endif

            public int Add(in T data) {
                List.Add(data);
#if DEBUG && false
                Traces.Add(new StackTrace(true));
#endif
                return List.Count - 1;
            }

            public override void Clear() {
                List.Clear();
                
#if DEBUG && false
                Traces.Clear();
#endif
            }

            public override void Run(int id) {
                List[id].Invoke();
            }
        }

    }

    public interface IRecorderCmd {
        void Invoke();
    }
}
