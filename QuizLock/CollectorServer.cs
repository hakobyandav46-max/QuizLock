using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuizLock
{
    /// <summary>
    /// Runs on the "collector" laptop only. Listens for POST /results requests
    /// from other laptops on the same local network running QuizLock in
    /// Quiz Station mode, and raises <see cref="ResultReceived"/> for each one.
    ///
    /// Note: ResultReceived fires on a background thread, not the UI thread -
    /// callers must marshal back to the UI thread (e.g. via Control.Invoke)
    /// before touching any WinForms controls.
    /// </summary>
    internal sealed class CollectorServer : IDisposable
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;

        public bool IsRunning => _listener is not null;

        public event Action<ResultPayload>? ResultReceived;
        public event Action<Exception>? ListenerFaulted;

        public void Start(int port)
        {
            if (_listener is not null) return;

            _listener = new HttpListener();
            // "+" binds all hostnames on this port - requires admin, which the
            // app manifest already requests, so this works without extra
            // "netsh http add urlacl" setup.
            _listener.Prefixes.Add($"http://+:{port}/results/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            _ = RunLoopAsync(_listener, _cts.Token);
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
                // best effort
            }
            finally
            {
                _listener = null;
                _cts = null;
            }
        }

        private async Task RunLoopAsync(HttpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    break; // listener was stopped/disposed
                }

                _ = HandleRequestAsync(context);
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    body = await reader.ReadToEndAsync();
                }

                var payload = JsonSerializer.Deserialize<ResultPayload>(body);
                if (payload is not null)
                {
                    ResultReceived?.Invoke(payload);
                    context.Response.StatusCode = 200;
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            }
            catch (Exception ex)
            {
                try { context.Response.StatusCode = 500; } catch { /* response may already be broken */ }
                ListenerFaulted?.Invoke(ex);
            }
            finally
            {
                try { context.Response.Close(); } catch { /* best effort */ }
            }
        }

        public void Dispose() => Stop();
    }
}
