using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace MyApp.Mobile;

/// <summary>Error to show as-is to the user (already in French).</summary>
public sealed class ApiException(string title, string? detail = null) : Exception(title)
{
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
}

/// <summary>
/// Calls the gateway on the same origin. A linked phone renews its access token by itself; when that is
/// refused (logged out, removed in MyProduct) it returns to the login page.
/// </summary>
public sealed class Api(HttpClient http, Session session, NavigationManager nav)
{
    private const int PageSize = 30;

    private Task<bool>? _renewal;

    /// <summary>Links this phone with the code shown in MyProduct.</summary>
    public async Task PairAsync(string code, string deviceName)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("api/auth/pair", new { code, deviceName });
        }
        catch (HttpRequestException)
        {
            throw Unreachable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                await session.SignInAsync((await response.Content.ReadFromJsonAsync<LoginResponse>(JsonSerializerOptions.Web))!);
                return;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new ApiException("Trop de tentatives.", "Réessayez dans une minute.");

            var problem = await ReadProblemAsync(response);
            throw new ApiException(problem?.Title ?? "Liaison impossible.", problem?.Detail ?? $"Erreur {(int)response.StatusCode}.");
        }
    }

    /// <summary>Unlinks this phone on the server too, so its saved token cannot be reused.</summary>
    public async Task LogoutAsync()
    {
        try
        {
            if (session.HasValidToken || await RenewAsync())
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Current!.AccessToken);
                using var _ = await http.SendAsync(request);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException)
        {
            // Offline: the phone still forgets its token.
        }

        await session.SignOutAsync();
    }

    public async Task<LoginResponse> LoginAsync(string userName, string password)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("api/auth/login", new { userName, password });
        }
        catch (HttpRequestException)
        {
            throw Unreachable();
        }

        return response.StatusCode switch
        {
            HttpStatusCode.OK => (await response.Content.ReadFromJsonAsync<LoginResponse>(JsonSerializerOptions.Web))!,
            HttpStatusCode.Unauthorized => throw new ApiException("Nom d'utilisateur ou mot de passe incorrect."),
            HttpStatusCode.TooManyRequests => throw new ApiException("Trop de tentatives.", "Réessayez dans une minute."),
            _ => throw new ApiException("Connexion impossible.", $"Erreur {(int)response.StatusCode}.")
        };
    }

    public Task<OfficineStatus> StatusAsync() => GetAsync<OfficineStatus>("api/officine/status");

    public Task<AgentInfo> InfoAsync() => GetAsync<AgentInfo>("api/officine/info");

    public Task<Dashboard> DashboardAsync(Period period) =>
        GetAsync<Dashboard>($"api/officine/dashboard?{period.Query}");

    public Task<List<Facture>> RecentFacturesAsync(int take = 50) =>
        GetAsync<List<Facture>>($"api/officine/factures/recent?take={take}");

    public Task<Page<Rejet>> RejetsAsync(Period period, int skip) => PageAsync<Rejet>("rejets", period, skip);

    public Task<Page<Bordereau>> BordereauxAsync(Period period, int skip) => PageAsync<Bordereau>("bordereaux", period, skip);

    public Task<Page<Psychotrope>> PsychotropesAsync(Period period, int skip) => PageAsync<Psychotrope>("psychotropes", period, skip);

    public Task<FactureFull?> FactureAsync(string numFact) =>
        GetAsync<FactureFull?>($"api/officine/factures/{Uri.EscapeDataString(numFact)}");

    public Task<Page<PatientRow>> PatientsAsync(string? search, int skip) =>
        GetAsync<Page<PatientRow>>($"api/officine/patients?q={Uri.EscapeDataString(search ?? "")}&skip={skip}&take={PageSize}");

    public Task<List<Reminder>> RemindersAsync() => GetAsync<List<Reminder>>("api/officine/patients/relance");

    public Task<PatientDetail?> PatientAsync(string numAssure, string rang) =>
        GetAsync<PatientDetail?>($"api/officine/patients/{Uri.EscapeDataString(numAssure)}/{Uri.EscapeDataString(rang)}");

    public Task<Page<MedicRow>> MedicamentsAsync(string? search, Period period, int skip) =>
        GetAsync<Page<MedicRow>>($"api/officine/medicaments?{period.Query}&q={Uri.EscapeDataString(search ?? "")}&skip={skip}&take={PageSize}");

    public Task<MedicDetail?> MedicamentAsync(string numEnr, Period period) =>
        GetAsync<MedicDetail?>($"api/officine/medicaments/{Uri.EscapeDataString(numEnr)}?{period.Query}");

    public Task<List<DayStat>> RecentDaysAsync() => GetAsync<List<DayStat>>("api/officine/stats/recent");

    public Task<Stats> StatsAsync(string kind, Period period) => GetAsync<Stats>($"api/officine/stats/{kind}?{period.Query}");

    public async Task<string> PushKeyAsync() => (await GetAsync<PushKey>("api/push/key")).PublicKey;

    public Task SubscribePushAsync(PushSubscriptionInfo subscription) => PostAsync("api/push/subscribe", subscription);

    public Task UnsubscribePushAsync(string endpoint) => PostAsync("api/push/unsubscribe", new { endpoint });

    public Task TestPushAsync(string endpoint) => PostAsync("api/push/test", new { endpoint });

    private Task<Page<T>> PageAsync<T>(string route, Period period, int skip) =>
        GetAsync<Page<T>>($"api/officine/{route}?{period.Query}&skip={skip}&take={PageSize}");

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await SendAsync(HttpMethod.Get, url, null);
        return (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web))!;
    }

    private async Task PostAsync(string url, object body)
    {
        using var _ = await SendAsync(HttpMethod.Post, url, body);
    }

    /// <summary>A successful response; anything else becomes an <see cref="ApiException"/>.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body)
    {
        if (!session.HasValidToken && !await RenewAsync())
            throw await SignedOutAsync();

        var response = await SendOnceAsync(method, url, body);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            if (!await RenewAsync())
                throw await SignedOutAsync();
            response = await SendOnceAsync(method, url, body);
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw await SignedOutAsync();

            // The gateway answers agent problems (offline, timeout, error) with French ProblemDetails.
            var problem = await ReadProblemAsync(response);
            throw new ApiException(problem?.Title ?? "Liaison impossible.", problem?.Detail ?? $"Erreur {(int)response.StatusCode}. Réessayez.");
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string url, object? body)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Current!.AccessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonSerializerOptions.Web);
        try
        {
            return await http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw Unreachable();
        }
    }

    /// <summary>
    /// New access token from the device token. False when the phone is no longer linked. Pages load several
    /// things at once: they share one renewal.
    /// </summary>
    private async Task<bool> RenewAsync()
    {
        _renewal ??= RenewOnceAsync();
        try
        {
            return await _renewal;
        }
        finally
        {
            _renewal = null;
        }
    }

    private async Task<bool> RenewOnceAsync()
    {
        if (session.DeviceToken is not { } deviceToken)
            return false;

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("api/auth/device", new { deviceToken });
        }
        catch (HttpRequestException)
        {
            throw Unreachable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return false;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new ApiException("Trop de tentatives.", "Réessayez dans une minute.");
            if (response.StatusCode != HttpStatusCode.OK)
                throw new ApiException("Serveur indisponible.", $"Erreur {(int)response.StatusCode}. Réessayez plus tard.");

            await session.RenewAsync((await response.Content.ReadFromJsonAsync<LoginResponse>(JsonSerializerOptions.Web))!);
            return true;
        }
    }

    private async Task<ApiException> SignedOutAsync()
    {
        var wasLinked = session.DeviceToken is not null;
        await session.SignOutAsync();
        nav.NavigateTo("login");
        return wasLinked
            ? new ApiException("Ce téléphone n'est plus lié.", "Saisissez un nouveau code affiché dans MyProduct.")
            : new ApiException("Session expirée.", "Reconnectez-vous.");
    }

    private static ApiException Unreachable() =>
        new("Serveur injoignable.", "Vérifiez la connexion internet du téléphone.");

    private static async Task<Problem?> ReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Problem>(JsonSerializerOptions.Web);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record Problem(string? Title, string? Detail);
}

/// <summary>A date range picked on screen, sent as whole days.</summary>
public sealed record Period(string Label, DateTime From, DateTime? To = null)
{
    public string Query => To is { } to
        ? $"from={From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        : $"from={From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    public static IReadOnlyList<Period> Choices()
    {
        var today = DateTime.Today;
        return
        [
            new("Aujourd'hui", today),
            new("7 jours", today.AddDays(-6)),
            new("Ce mois", new DateTime(today.Year, today.Month, 1)),
            new("3 mois", today.AddMonths(-3)),
            new("Cette année", new DateTime(today.Year, 1, 1)),
            new("2 ans", today.AddYears(-2))
        ];
    }
}
