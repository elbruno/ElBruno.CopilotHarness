// =============================================================================
//  ProxiesAppHost — Aspire orchestrator for the three BYOK proxy samples.
//
//  WHAT THIS DOES
//  --------------
//  Starts OllamaProxy, FoundryProxy, and FoundryLocalProxy together and wires
//  them into the Aspire dashboard so you can see all their logs, health, and
//  request timings in one place — without juggling three terminal windows.
//
//  HOW TO RUN
//  ----------
//    cd proxies/AppHost
//    dotnet run
//
//  Then open the Aspire dashboard URL printed in the console.
//
//  PORTS (fixed — match VS Code BYOK settings)
//  ─────────────────────────────────────────────
//    OllamaProxy       → http://localhost:5099
//    FoundryProxy      → http://localhost:5100
//    FoundryLocalProxy → http://localhost:5101
//
//  PRE-REQUISITES
//  --------------
//    OllamaProxy       — Ollama running locally (https://ollama.com)
//    FoundryProxy      — dotnet user-secrets already set in FoundryProxy project
//                        (Foundry:Endpoint, Foundry:ApiKey, Foundry:Deployment)
//    FoundryLocalProxy — nothing extra; SDK downloads the model on first run
//
//  NOTES
//  -----
//  • FoundryLocalProxy may take several minutes on first run while it downloads
//    the phi-4-mini model weights (~2.5 GB). The Aspire dashboard will show it as
//    "Starting" until the download completes and the proxy begins accepting requests.
//  • FoundryProxy reads its secrets from the FoundryProxy project's user-secrets
//    store (not from this AppHost). Run `cd ../FoundryProxy && dotnet user-secrets set ...`
//    before starting the AppHost if you want FoundryProxy to work.
//  • You can run individual proxies with `dotnet run` in their own folder without
//    the AppHost — the AppHost is purely an optional observability convenience.
// =============================================================================

var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// OllamaProxy — local Ollama models via OpenAI-compatible API
//
//  Requires a running Ollama instance (http://localhost:11434 by default).
//  Configure the default model and Ollama URL in OllamaProxy/appsettings.json.
// ---------------------------------------------------------------------------
var ollamaProxy = builder.AddProject<Projects.OllamaProxy>("ollama-proxy")
    .WithHttpEndpoint(port: 5099, name: "http");

// ---------------------------------------------------------------------------
// FoundryProxy — Azure OpenAI / Foundry cloud models
//
//  Reads credentials from the FoundryProxy project's dotnet user-secrets store.
//  The AppHost itself does not manage those secrets.
//  Set them once with:
//    cd ../FoundryProxy
//    dotnet user-secrets set "Foundry:Endpoint"   "https://your-resource.openai.azure.com"
//    dotnet user-secrets set "Foundry:ApiKey"     "your-api-key"
//    dotnet user-secrets set "Foundry:Deployment" "gpt-4o-mini"
// ---------------------------------------------------------------------------
var foundryProxy = builder.AddProject<Projects.FoundryProxy>("foundry-proxy")
    .WithHttpEndpoint(port: 5100, name: "http");

// ---------------------------------------------------------------------------
// FoundryLocalProxy — Microsoft Foundry Local (phi-4-mini, fully offline)
//
//  The Foundry Local C# SDK manages the daemon and model download automatically.
//  On FIRST RUN: expects an internet connection; downloads ~2.5 GB (cached after).
//  On subsequent runs: near-instant startup from the local cache.
// ---------------------------------------------------------------------------
var foundryLocalProxy = builder.AddProject<Projects.FoundryLocalProxy>("foundry-local-proxy")
    .WithHttpEndpoint(port: 5101, name: "http");

// ---------------------------------------------------------------------------
// ProxiesTestApp — Blazor Server UI for testing all three proxies
//
//  Pages:
//    /          — health dashboard (all 3 proxies, auto-refresh)
//    /chat      — streaming chat with one proxy, model selector, system prompt
//    /compare   — side-by-side: same prompt sent to all 3 proxies simultaneously
//
//  WithEnvironment injects the proxy base URLs so the app works with Aspire
//  service discovery without needing the Microsoft.Extensions.ServiceDiscovery
//  package. Falls back to appsettings.json when running standalone.
// ---------------------------------------------------------------------------
builder.AddProject<Projects.ProxiesTestApp>("proxies-test-app")
    .WithEnvironment("ProxyUrls__Ollama",       ollamaProxy.GetEndpoint("http"))
    .WithEnvironment("ProxyUrls__Foundry",      foundryProxy.GetEndpoint("http"))
    .WithEnvironment("ProxyUrls__FoundryLocal", foundryLocalProxy.GetEndpoint("http"))
    .WaitFor(ollamaProxy)
    .WaitFor(foundryLocalProxy);

builder.Build().Run();
