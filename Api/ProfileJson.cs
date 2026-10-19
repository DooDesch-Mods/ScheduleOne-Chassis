using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chassis.Api
{
    /// <summary>JSON settings for profiles: vectors as [x, y, z], readable indentation.</summary>
    internal static class ProfileJson
    {
        internal static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new Vector3Converter() },
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        /// <summary>
        /// Vector3 has computed properties (normalized, magnitude) that point back at a Vector3; the default contract
        /// would loop. Written as a three-number array, read from an array or from {x, y, z}.
        /// </summary>
        private sealed class Vector3Converter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(Vector3);

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var v = (Vector3)value;
                writer.WriteStartArray();
                writer.WriteValue(Math.Round(v.x, 4));
                writer.WriteValue(Math.Round(v.y, 4));
                writer.WriteValue(Math.Round(v.z, 4));
                writer.WriteEndArray();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var token = JToken.Load(reader);
                if (token is JArray a && a.Count == 3)
                    return new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
                if (token is JObject o)
                    return new Vector3(o.Value<float>("x"), o.Value<float>("y"), o.Value<float>("z"));
                throw new JsonSerializationException("a vector must be [x, y, z], got " + token.ToString(Formatting.None));
            }
        }

        internal static string F(Vector3 v) =>
            "(" + v.x.ToString("F3", CultureInfo.InvariantCulture) + ", " + v.y.ToString("F3", CultureInfo.InvariantCulture) + ", " + v.z.ToString("F3", CultureInfo.InvariantCulture) + ")";
    }
}
