using System;
using System.Collections.Generic;
using System.Reflection;

namespace OlympUI {
    // TODO: this has to go :D
    public static class UIReflection {

        private static List<Assembly>? _Assemblies;
        public static List<Assembly> Assemblies => _Assemblies ??= new List<Assembly>(AppDomain.CurrentDomain.GetAssemblies());

        private static List<Type>? _AllTypes;
        private static Dictionary<Type, List<Type>> _AllTypesPerType = new();

        private static List<Type> RebuildAllTypes() {
            _AllTypesPerType.Clear();

            List<Type> types = new();

            foreach (Assembly asm in Assemblies)
                types.AddRange(asm.GetTypes());

            return _AllTypes = types;
        }

        private static List<Type> GetAllTypes() {
            return _AllTypes ??= RebuildAllTypes();
        }
        
        public static List<Type> GetAllTypes(Type inherit) {
            if (_AllTypesPerType.TryGetValue(inherit, out List<Type>? types))
                return types;

            types = new();

            foreach (Type type in GetAllTypes())
                if (inherit.IsAssignableFrom(type))
                    types.Add(type);

            return _AllTypesPerType[inherit] = types;
        }

    }
}
