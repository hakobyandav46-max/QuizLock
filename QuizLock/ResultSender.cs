using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace QuizLock
{
    /// <summary>
    /// Used by a Quiz Station laptop to send a completed result to a
    /// Collector laptop over the local network. Best-effort: if the
    /// collector can't be reached (wrong address, firewall, it's offline),
    /// this returns false so the caller can fall back to saving locally.
    /// </summary>
    internal static class ResultSender
    {
        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };

        public static async Task<bool> TrySendAsync(string collectorAddress, string name, string score, string quizUrl)
        {
            try
            {
                var url = collectorAddress.Contains("://") ? collectorAddress : $"http://{collectorAddress}";
                url = url.TrimEnd('/');
                if (!url.EndsWith("/results", StringComparison.OrdinalIgnoreCase))
                {
                    url += "/results";
                }

                var payload = new ResultPayload
                {
                    Name = name,
                    Score = score,
                    QuizUrl = quizUrl,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                var json = JsonSerializer.Serialize(payload);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await Client.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
