using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;

namespace Olympus {
    public static class JsonHelper {

        public static readonly JsonSerializer Serializer = new() {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public class ExistingCreationConverter<T> : CustomCreationConverter<T> {

            public T Value;

            public ExistingCreationConverter(T value) {
                Value = value;
            }

            public override T Create(Type objectType) {
                return Value;
            }

        }

    }
}
