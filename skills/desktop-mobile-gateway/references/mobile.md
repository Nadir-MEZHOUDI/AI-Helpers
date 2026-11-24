# Phone app (MyApp.Mobile, Blazor WebAssembly PWA)

Ready-to-adapt files are in `assets/mobile/` (copied from SmartCHIFA, `SmartCHIFA` → `MyProduct`, JS namespace
`window.chifa` → `window.app`). Copy them into the new project, then rename models/routes/texts. Domain pages
(`Rejets.razor`, `Statistiques.razor`) are examples of the patterns, not files to keep.

## Project

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <!-- Phone app (installable PWA). Served by MyApp.Gateway, so it calls the API on the same origin. -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <InvariantGlobalization>true</InvariantGlobalization>
    <ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <ServiceWorker Include="wwwroot\service-worker.js" PublishedContent="wwwroot\service-worker.published.js" />
  </ItemGroup>
</Project>
```

- No reference to `MyApp.Contract`: the phone has its own small models (`Models.cs`) with only the fields it shows.
  JSON is read with `JsonSerializerOptions.Web`; missing fields are fine.
- `Program.cs`: `HttpClient` with `BaseAddress = HostEnvironment.BaseAddress`, `Session` singleton, `Api` scoped,
  `await Session.LoadAsync()` before `RunAsync()`.
- `App.razor`: `Router` with `NotFoundPage`, default layout `MainLayout`; `Login` uses `@layout EmptyLayout`.

## Files and what each one does

| File | Role |
|---|---|
| `Session.cs` | `LoginResponse` in `localStorage` (`myapp.session`). `HasValidToken` (1 min margin), `DeviceToken`, `IsSignedIn`, `SignInAsync`, `RenewAsync` (keeps the device token), `SignOutAsync`. |
| `Api.cs` | All gateway calls. `ApiException(Title, Detail)` = message shown as is. Renews the access token from the device token before a call or after a 401 (one shared renewal task for parallel calls). Not renewable → sign out, go to `login`, "this phone is no longer linked". Errors read the gateway's ProblemDetails `title`/`detail`. `HttpRequestException` → "server unreachable, check the phone's internet". |
| `Period` (in `Api.cs`) | `record Period(Label, From, To?)` with `Query` = `from=yyyy-MM-dd[&to=...]` (invariant) and `Choices()` presets. |
| `Fmt.cs` | Number/money/date formatting **without ICU**: own `NumberFormatInfo` (group separator U+00A0, decimal `,`), `Money`, `Number`, `Percent`, `Compact` (950, 12 k, 1,4 M), `Day`/`LongDay` names, `Date`, `Stamp`, `Ago`. |
| `Components/PagedPage.cs` | Base class for "list for a period with Afficher plus": `Items`, `Total`, `Loading`, `Error`, `FetchAsync(period, skip)`. |
| `Components/ListFooter.razor` | Error/loading/empty/"more (n/total)"/"n results" footer for paged lists. |
| `Components/ErrorBox.razor` | Shows `ApiException` title + detail + optional Retry. |
| `Components/PeriodChips.razor` | Period presets as chips; `Options` overrides the list. |
| `Components/BarChart.razor` | Pure-CSS vertical bars: `Bar(Label, Title, Values, Top)`, `Colorful` (palette `c0..c7`), stacked when `Values.Count > 1` (`s0`/`s1`), `Scroll` (fixed-width bars, opens on the newest), `Selected`/`OnSelect`. |
| `Components/BackButton.razor` | `history.back()` when the user navigated inside the app, else a `Fallback` route (page opened from a notification). |
| `Components/SearchBox.razor` | Debounced search input. |
| `Layout/NavItems.cs` | Single list of pages: `NavItem(Href, Label, IconPath, InTabBar, Short)`. Drawer shows all, tab bar the `InTabBar` ones (max 5). |
| `Layout/MainLayout.razor` | Redirects to `login` when signed out. Drawer (always visible ≥1024 px, hamburger below), top bar with tenant name and **online badge** refreshed every 30 s from `/status`, bottom tab bar on phones, install button (PWA), sign-out with confirm (also forgets the push subscription). |
| `Pages/Login.razor` | Code input auto-formatted `ABCD-2345`; `?code=` from the QR code links at once; optional username/password form; device name from the user agent. |
| `Pages/Notifications.razor` | Push state (on/off/denied/ios-install/unsupported), enable (key → subscribe → server → test push), test, disable. |
| `wwwroot/index.html` | Boot spinner, error UI, and `window.app` JS helpers: `userAgent`, `confirm`, `push.{state,subscribe,current,unsubscribe,forget}`, `install.{watch,unwatch,run}` (captures `beforeinstallprompt` before Blazor boots; iOS shows "Share → Add to Home Screen"). Registers the service worker with `updateViaCache: 'none'`. |
| `wwwroot/service-worker(.published).js` | Dev: network only. Published: offline cache of the app shell from `service-worker-assets.js`. Both: `push` → `showNotification(title, {body, tag, icon, data:{url}})`, `notificationclick` → focus an open window and navigate, or open one. |
| `wwwroot/manifest.webmanifest` | `display: standalone`, `id/start_url/scope: ./`, theme/background colors, `shortcuts` to main pages, icons 192/512 `any` + `maskable`. |
| `wwwroot/css/app.css` | Whole design system (see below). |

## Page patterns

**Paged list** (`Rejets.razor`):
```razor
@page "/orders"
@inherits PagedPage<Order>

<div class="page-head"><h1>Commandes</h1></div>
<PeriodChips Selected="Period" SelectedChanged="ChangePeriodAsync" />
@foreach (var o in Items)
{
    <a class="card item link" href="orders/@Uri.EscapeDataString(o.Id)">
        <div class="item-head"><strong>N° @o.Number</strong><strong>@Fmt.Money(o.Total)</strong></div>
        <div class="item-sub"><span>@o.Customer</span><span>@Fmt.Date(o.Date)</span></div>
    </a>
}
<ListFooter Count="Items.Count" Total="Total" Loading="Loading" Error="Error" More="LoadMoreAsync" />

@code {
    protected override Task<Page<Order>> FetchAsync(Period period, int skip) => Api.OrdersAsync(period, skip);
}
```

**Detail page**: `@page "/orders/{Id}"`, `<BackButton Fallback="orders" />`, load in `OnParametersSetAsync`, show
`<Loading />` / `<ErrorBox Error="_error" Retry="LoadAsync" />` / content; a `null` answer = "not found" message.

**Dashboard / stats**: hero card (big amount + label), `.kpis` grid of `.card.kpi`, `BarChart` with metric chips, tiles with
▲/▼ % change vs previous period, a "Statistiques →" link to the full page. The full stats page takes `?type=` in the URL
(`[SupplyParameterFromQuery]`) so each tab is linkable, and loads again when kind or period changes.

**Keep what exists**: when adding to the home page, add sections above/below; never remove what the user already has.

## Design system (app.css)

- CSS variables in `:root` for palette (`--primary`, `--ok/--warn/--bad` + `-soft`), surfaces, radius, shadow, fonts
  (`--f-display`, `--f-body`), `--tabbar` height; dark mode via `prefers-color-scheme`. **Change the palette and fonts to the
  product's website**; keep the structure.
- Fonts self-hosted as `woff2` in `wwwroot/fonts`, preloaded in `index.html` (no Google Fonts request).
- Building blocks: `.card`, `.hero`, `.kpis/.kpi`, `.item/.item-head/.item-sub`, `.figures`, `.tag.ok|warn|bad`, `.chips/.chip.active`,
  `.chips.small`, `.button(.secondary|.wide|.small)`, `.error-box`, `.empty`, `.list-end`, `.muted`, `.small`, `.hint`,
  chart classes (`.chart`, `.col`, `.col-plot`, `.col-stack`, `.seg`, `.col-label`, `.legend`), `.tiles/.tile/.delta.up|down`, `.duo`.
- Layout: phone-first; ≥720 px tiles auto-fit; ≥1024 px permanent drawer, `.duo` two columns, no tab bar.
- Safe areas: `env(safe-area-inset-*)` on top bar and tab bar (`viewport-fit=cover`).
- Icons are inline SVG paths (24×24, stroke), no icon font.

## Icons and branding

Generate `icon-192.png`, `icon-512.png`, `maskable-192.png`, `maskable-512.png` (maskable = logo inside the 80 % safe zone
on the brand background) from the product logo. Set `theme-color` in `index.html` and the manifest to the top-bar color.

## Push (client side)

Enable flow: `Api.PushKeyAsync()` → `app.push.subscribe(key)` (asks permission; resubscribes when the stored subscription
used another server key) → `Api.SubscribePushAsync({endpoint,p256dh,auth})` → `Api.TestPushAsync(endpoint)`.
iPhone: push works only once the PWA is installed to the home screen and opened from its icon (state `ios-install`).
