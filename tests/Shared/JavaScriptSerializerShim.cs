using System.Text.Json;

namespace System.Web.Script.Serialization
{
    internal sealed class JavaScriptSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions();

        public string Serialize(object value)
        {
            return JsonSerializer.Serialize(value, Options);
        }

        public T Deserialize<T>(string input)
        {
            return JsonSerializer.Deserialize<T>(input, Options);
        }
    }
}
