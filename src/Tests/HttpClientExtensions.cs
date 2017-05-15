namespace WebHooks
{
    using System;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using WebHooks.Publisher;

    internal static class HttpClientExtensions
    {
        public static Task<HttpResponseMessage> PostAsJson<T>(this HttpClient client, T request, string requestUri)
        {
            var json = JsonConvert.SerializeObject(request, WebHookPublisher.SerializerSettings);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return client.PostAsync(requestUri, content);
        }

        public static Task<HttpResponseMessage> PostAsJson<T>(this HttpClient client, T request, Uri requestUri)
        {
            var json = JsonConvert.SerializeObject(request, WebHookPublisher.SerializerSettings);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return client.PostAsync(requestUri, content);
        }

        public static async Task<T> ReadAs<T>(this HttpContent content)
        {
            var body = await content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(body, WebHookPublisher.SerializerSettings);
        }
    }
}