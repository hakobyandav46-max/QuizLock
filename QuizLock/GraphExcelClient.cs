using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace QuizLock
{
    /// <summary>
    /// Signs in to a Microsoft account (via MSAL) and appends quiz-result
    /// rows directly into an Excel workbook stored on OneDrive, using the
    /// Microsoft Graph Excel API. The workbook is identified by the normal
    /// sharing link you get from Excel Online / OneDrive (e.g. an
    /// excel.cloud.microsoft/open/onedrive/?... URL) - Graph resolves that
    /// link to the underlying drive item, so there's no manual ID lookup.
    /// </summary>
    internal sealed class GraphExcelClient
    {
        // "common" so this works with both personal Microsoft accounts and
        // work/school accounts, matching an app registered for "Accounts in
        // any organizational directory and personal Microsoft accounts".
        private const string Authority = "https://login.microsoftonline.com/common";
        private static readonly string[] Scopes = { "Files.ReadWrite" };

        private readonly string _clientId;
        private readonly HttpClient _http = new();
        private IPublicClientApplication? _app;
        private IAccount? _account;

        private string? _driveId;
        private string? _itemId;
        private string? _worksheetName;

        public GraphExcelClient(string clientId)
        {
            _clientId = clientId;
        }

        private async Task<IPublicClientApplication> GetAppAsync()
        {
            if (_app is not null) return _app;

            var app = PublicClientApplicationBuilder.Create(_clientId)
                .WithAuthority(Authority)
                .WithRedirectUri("http://localhost")
                .Build();

            // Persist the token cache to disk (DPAPI-protected) so the
            // person doesn't have to sign in again every single run.
            try
            {
                var storageProps = new StorageCreationPropertiesBuilder(
                        "quizlock.msalcache.bin",
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuizLock"))
                    .Build();
                var cacheHelper = await MsalCacheHelper.CreateAsync(storageProps);
                cacheHelper.RegisterCache(app.UserTokenCache);
            }
            catch
            {
                // Cache persistence is a nice-to-have; fall back to an
                // in-memory-only cache (means signing in every run) if the
                // machine doesn't support it for some reason.
            }

            _app = app;
            return _app;
        }

        /// <summary>
        /// Signs in, reusing a cached session if one exists. The first time
        /// this runs it opens a normal Microsoft sign-in window; after that
        /// it's silent.
        /// </summary>
        public async Task SignInAsync()
        {
            var app = await GetAppAsync();
            var accounts = await app.GetAccountsAsync();
            _account = accounts.FirstOrDefault();

            AuthenticationResult result;
            try
            {
                result = await app.AcquireTokenSilent(Scopes, _account).ExecuteAsync();
            }
            catch (MsalUiRequiredException)
            {
                result = await app.AcquireTokenInteractive(Scopes).ExecuteAsync();
            }

            _account = result.Account;
            ApplyToken(result.AccessToken);
        }

        private async Task RefreshTokenAsync()
        {
            var app = await GetAppAsync();
            var result = await app.AcquireTokenSilent(Scopes, _account).ExecuteAsync();
            ApplyToken(result.AccessToken);
        }

        private void ApplyToken(string accessToken)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        /// <summary>
        /// Resolves a normal OneDrive/Excel sharing link into the drive +
        /// item IDs the Graph Excel API needs, then finds the workbook's
        /// first worksheet.
        /// </summary>
        public async Task ResolveWorkbookAsync(string shareUrl)
        {
            string encoded = EncodeSharingUrl(shareUrl);

            var driveItem = await GetJsonAsync(
                $"https://graph.microsoft.com/v1.0/shares/{encoded}/driveItem?$select=id,parentReference");
            _itemId = driveItem.GetProperty("id").GetString();
            _driveId = driveItem.GetProperty("parentReference").GetProperty("driveId").GetString();

            var worksheets = await GetJsonAsync(
                $"https://graph.microsoft.com/v1.0/drives/{_driveId}/items/{_itemId}/workbook/worksheets?$select=name");
            var first = worksheets.GetProperty("value").EnumerateArray().FirstOrDefault();
            _worksheetName = first.ValueKind == JsonValueKind.Undefined ? "Sheet1" : first.GetProperty("name").GetString();
        }

        // Per Microsoft Graph docs: "Access DriveItems by sharing URL" -
        // base64-encode the URL, strip padding, swap URL-unsafe characters,
        // and prefix with "u!". Works for any OneDrive/SharePoint sharing
        // link regardless of its own internal ID format.
        private static string EncodeSharingUrl(string url)
        {
            var bytes = Encoding.UTF8.GetBytes(url);
            var base64 = Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('/', '_')
                .Replace('+', '-');
            return "u!" + base64;
        }

        /// <summary>
        /// Appends one row (Name, Score, Date/Time) to the resolved
        /// worksheet, writing a header row first if the sheet is empty.
        /// </summary>
        public async Task AppendRowAsync(string name, string score)
        {
            if (_driveId is null || _itemId is null || _worksheetName is null)
            {
                throw new InvalidOperationException("Workbook not resolved yet.");
            }

            await RefreshTokenAsync();

            string baseUrl =
                $"https://graph.microsoft.com/v1.0/drives/{_driveId}/items/{_itemId}/workbook/worksheets('{Uri.EscapeDataString(_worksheetName)}')";

            int nextRow = 1;
            var usedRangeResp = await _http.GetAsync($"{baseUrl}/usedRange(valuesOnly=true)?$select=rowCount");
            if (usedRangeResp.IsSuccessStatusCode)
            {
                var used = await usedRangeResp.Content.ReadFromJsonAsync<JsonElement>();
                if (used.TryGetProperty("rowCount", out var rowCountProp))
                {
                    nextRow = rowCountProp.GetInt32() + 1;
                }
            }

            if (nextRow == 1)
            {
                await WriteRowAsync(baseUrl, 1, "Name", "Score", "Date/Time");
                nextRow = 2;
            }

            await WriteRowAsync(baseUrl, nextRow, name, score, DateTime.Now.ToString("g"));
        }

        private async Task WriteRowAsync(string baseUrl, int row, string col1, string col2, string col3)
        {
            string address = $"A{row}:C{row}";
            var body = new { values = new[] { new object[] { col1, col2, col3 } } };

            var resp = await _http.PatchAsJsonAsync($"{baseUrl}/range(address='{address}')", body);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Graph API error ({(int)resp.StatusCode}): {detail}");
            }
        }

        private async Task<JsonElement> GetJsonAsync(string url)
        {
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Graph API error ({(int)resp.StatusCode}): {detail}");
            }
            return await resp.Content.ReadFromJsonAsync<JsonElement>();
        }
    }
}
