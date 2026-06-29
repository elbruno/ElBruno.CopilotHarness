import * as vscode from 'vscode';

type ExplanationRequest = {
  prompt: string;
  systemMessage?: string;
  requestedProfile?: string;
  stream: boolean;
};

type PlaygroundResponse = {
  profile: string;
  deployment: string;
  reason: string;
  promptCharacters: number;
  routedRequest: unknown;
};

type TraceResponse = {
  traceId: string;
  createdAtUtc: string;
  workflowEngine: string;
  classification: {
    intent: string;
    complexity: string;
    confidence: number;
    reasoning: string;
  };
  ruleAdvisor: {
    suggestedProfile?: string | null;
    rationale: string;
  };
  decision: {
    profile: string;
    deployment: string;
    reason: string;
  };
  context: Array<{ key: string; value: string }>;
  steps: Array<{ name: string; outcome: string }>;
};

type DashboardSnapshot = {
  connectedClients: Array<{
    client: string;
    isConnected: boolean;
    activeRequests: number;
    requestsLastFiveMinutes: number;
    lastSeenAtUtc?: string | null;
  }>;
  liveRequests: Array<{
    requestId: string;
    endpoint: string;
    client: string;
    stream: boolean;
    requestedModel?: string | null;
    selectedProfile?: string | null;
    selectedDeployment?: string | null;
    traceId?: string | null;
    startedAtUtc: string;
    elapsedMs: number;
  }>;
  generatedAtUtc: string;
};

type RoutedRequestView = {
  traceId: string;
  createdAtUtc: string;
  clientId: string;
  clientDisplayName: string;
  endpoint: string;
  stream: boolean;
  requestedModel?: string | null;
  selectedModel: string;
  deployment: string;
  matchedRuleName?: string | null;
  reason: string;
  explanation: string;
  promptPreview?: string | null;
  promptCharacters: number;
  classificationIntent: string;
  classificationComplexity: string;
  classifierSource?: string;
  processorModel?: string | null;
  classificationConfidence?: number;
  totalPromptCharacters?: number;
  hasSystemMessage?: boolean;
};

type RoutingFeedResponse = {
  generatedAtUtc: string;
  promptCaptureEnabled: boolean;
  requests: RoutedRequestView[];
};

const statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
let statusPanel: vscode.WebviewPanel | undefined;
let livePanel: vscode.WebviewPanel | undefined;

export function activate(context: vscode.ExtensionContext) {
  statusBarItem.text = 'Harness: Ready';
  statusBarItem.command = 'harness.showLiveRouting';
  statusBarItem.tooltip = 'Show Harness live routing (prompt → model → rule)';
  statusBarItem.show();
  context.subscriptions.push(statusBarItem);

  context.subscriptions.push(
    vscode.commands.registerCommand('harness.showStatusPanel', () => showStatusPanel(context)),
    vscode.commands.registerCommand('harness.explainRouting', () => explainRouting(context)),
    vscode.commands.registerCommand('harness.openDashboard', () => openDashboard(context)),
    vscode.commands.registerCommand('harness.openTrace', () => openTrace(context)),
    vscode.commands.registerCommand('harness.showLiveRouting', () => showLiveRouting(context))
  );

  // Keep the status bar showing the most recent routed model.
  void refreshStatusBar(context);
  const statusBarTimer = setInterval(() => void refreshStatusBar(context), 5000);
  context.subscriptions.push({ dispose: () => clearInterval(statusBarTimer) });

  const chatApi = (vscode as any).chat;
  if (chatApi?.createChatParticipant) {
    const participant = chatApi.createChatParticipant('harness', async (request: any, _chatContext: any, stream: any, token: vscode.CancellationToken) => {
      const prompt = String(request?.prompt ?? '').trim();
      if (!prompt) {
        stream.markdown('Ask me to explain routing, open the dashboard, or inspect a trace.');
        return;
      }

      if (/dashboard/i.test(prompt)) {
        const dashboardUrl = getDashboardUrl();
        stream.markdown(`Open the [dashboard](${dashboardUrl}) or run **Harness: Open Dashboard**.`);
        return;
      }

      const traceMatch = prompt.match(/trace[:\s]+([A-Za-z0-9_-]+)/i);
      if (traceMatch?.[1]) {
        const trace = await fetchTrace(context, traceMatch[1], token);
        stream.markdown(renderTraceMarkdown(trace));
        return;
      }

      const response = await fetchRoutingExplanation(context, {
        prompt,
        stream: false
      }, token);
      stream.markdown(renderExplanationMarkdown(response));
    });

    context.subscriptions.push(participant);
  }

  const lmApi = (vscode as any).lm;
  if (lmApi?.registerTool) {
    context.subscriptions.push(
      lmApi.registerTool('harness.explainRouting', {
        description: 'Explain routing by calling the router playground endpoint.',
        inputSchema: {
          type: 'object',
          properties: {
            prompt: { type: 'string' },
            systemMessage: { type: 'string' },
            requestedProfile: { type: 'string' },
            stream: { type: 'boolean' }
          },
          required: ['prompt']
        },
        invoke: async (input: ExplanationRequest, token: vscode.CancellationToken) =>
          fetchRoutingExplanation(context, input, token)
      }),
      lmApi.registerTool('harness.openDashboard', {
        description: 'Open the Admin dashboard in the browser.',
        inputSchema: { type: 'object', properties: {} },
        invoke: async () => {
          await openDashboard(context);
          return { opened: true, url: getDashboardUrl() };
        }
      }),
      lmApi.registerTool('harness.openTrace', {
        description: 'Open a routing trace in the browser.',
        inputSchema: {
          type: 'object',
          properties: {
            traceId: { type: 'string' }
          },
          required: ['traceId']
        },
        invoke: async (input: { traceId: string }) => {
          await openTrace(context, input.traceId);
          return { opened: true, url: getTraceUrl(input.traceId) };
        }
      })
    );
  }
}

async function showStatusPanel(context: vscode.ExtensionContext) {
  const panel = getOrCreatePanel(context);
  panel.webview.html = await buildStatusHtml(context, panel.webview);
  panel.reveal(vscode.ViewColumn.One);
}

async function openDashboard(context: vscode.ExtensionContext) {
  await vscode.env.openExternal(vscode.Uri.parse(getDashboardUrl()));
}

async function openTrace(context: vscode.ExtensionContext, traceId?: string) {
  const value = traceId ?? await vscode.window.showInputBox({
    title: 'Open routing trace',
    prompt: 'Trace identifier from the router or dashboard'
  });

  if (!value) {
    return;
  }

  await vscode.env.openExternal(vscode.Uri.parse(getTraceUrl(value)));
}

async function explainRouting(context: vscode.ExtensionContext) {
  const prompt = await vscode.window.showInputBox({
    title: 'Explain routing',
    prompt: 'Prompt to explain'
  });

  if (!prompt) {
    return;
  }

  const response = await fetchRoutingExplanation(context, { prompt, stream: false }, new vscode.CancellationTokenSource().token);
  const panel = getOrCreatePanel(context);
  panel.webview.html = renderExplanationPanelHtml(response);
  panel.reveal(vscode.ViewColumn.One);
}

async function fetchRoutingExplanation(
  context: vscode.ExtensionContext,
  request: ExplanationRequest,
  token: vscode.CancellationToken
): Promise<PlaygroundResponse> {
  const adminBaseUrl = getAdminBaseUrl();
  const response = await fetchJson<PlaygroundResponse>(`${adminBaseUrl}/admin/playground/evaluate`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...adminAuthHeaders()
    },
    body: JSON.stringify(request)
  });

  return response;
}

async function fetchTrace(context: vscode.ExtensionContext, traceId: string, token: vscode.CancellationToken): Promise<TraceResponse> {
  return await fetchJson<TraceResponse>(`${getAdminBaseUrl()}/admin/traces/${encodeURIComponent(traceId)}`, {
    method: 'GET',
    headers: adminAuthHeaders()
  });
}

async function fetchSnapshot(context: vscode.ExtensionContext, token: vscode.CancellationToken): Promise<DashboardSnapshot> {
  return await fetchJson<DashboardSnapshot>(`${getAdminBaseUrl()}/admin/dashboard/snapshot`, {
    method: 'GET',
    headers: adminAuthHeaders()
  });
}

async function fetchRoutingFeed(context: vscode.ExtensionContext, limit = 50): Promise<RoutingFeedResponse> {
  return await fetchJson<RoutingFeedResponse>(`${getAdminBaseUrl()}/admin/telemetry/feed?limit=${limit}`, {
    method: 'GET',
    headers: adminAuthHeaders()
  });
}

async function refreshStatusBar(context: vscode.ExtensionContext) {
  try {
    const feed = await fetchRoutingFeed(context, 1);
    const latest = feed.requests[0];
    statusBarItem.text = latest
      ? `Harness: ${latest.selectedModel}`
      : 'Harness: Ready';
    statusBarItem.tooltip = latest
      ? `Last routed to ${latest.selectedModel}${latest.matchedRuleName ? ` via rule '${latest.matchedRuleName}'` : ''}. Click for live routing.`
      : 'Show Harness live routing (prompt → model → rule)';
  } catch {
    statusBarItem.text = 'Harness: Offline';
  }
}

async function showLiveRouting(context: vscode.ExtensionContext) {
  const panel = getOrCreateLivePanel(context);
  await renderLivePanel(context, panel);
  panel.reveal(vscode.ViewColumn.One);
}

function getOrCreateLivePanel(context: vscode.ExtensionContext) {
  if (livePanel) {
    return livePanel;
  }

  livePanel = vscode.window.createWebviewPanel(
    'harnessLiveRouting',
    'Harness Live Routing',
    vscode.ViewColumn.One,
    { enableScripts: true }
  );

  livePanel.onDidDispose(() => {
    livePanel = undefined;
  }, undefined, context.subscriptions);

  livePanel.webview.onDidReceiveMessage(async message => {
    if (message?.command === 'refresh' && livePanel) {
      await renderLivePanel(context, livePanel);
    } else if (message?.command === 'openTrace' && typeof message.traceId === 'string') {
      await openTrace(context, message.traceId);
    }
  }, undefined, context.subscriptions);

  return livePanel;
}

async function renderLivePanel(context: vscode.ExtensionContext, panel: vscode.WebviewPanel) {
  let feed: RoutingFeedResponse;
  try {
    feed = await fetchRoutingFeed(context, 50);
  } catch (error) {
    feed = { generatedAtUtc: new Date().toISOString(), promptCaptureEnabled: false, requests: [] };
  }

  panel.webview.html = renderLiveRoutingHtml(feed);
}

function renderLiveRoutingHtml(feed: RoutingFeedResponse): string {
  const nonce = createNonce();
  const rows = feed.requests.map(request => {
    const prompt = request.promptPreview
      ? escapeHtml(request.promptPreview.length > 60 ? `${request.promptPreview.slice(0, 60)}…` : request.promptPreview)
      : `<span class="muted">(${request.promptCharacters} chars)</span>`;
    const total = request.totalPromptCharacters ?? request.promptCharacters;
    const contextNote = (request.hasSystemMessage || total > request.promptCharacters)
      ? `<br/><span class="ctx" title="Routing uses your message only; Copilot's system preamble + history are ignored for routing.">📎 ${request.promptCharacters}/${total} ctx${request.hasSystemMessage ? ' · system preamble' : ''}</span>`
      : '';
    const intent = request.classificationIntent ? escapeHtml(request.classificationIntent) : 'unknown';
    const sourceIcon = request.classifierSource === 'processor-model' ? '🧠' : '⚙️';
    const confidence = typeof request.classificationConfidence === 'number'
      ? ` (${request.classificationConfidence.toFixed(2)})`
      : '';
    const intentCell = `<span class="badge badge-${intent}">${intent}</span> <span class="muted">${sourceIcon}${escapeHtml(confidence)}</span>`;
    return `<tr>
      <td>${escapeHtml(new Date(request.createdAtUtc).toLocaleTimeString())}</td>
      <td>${prompt}${contextNote}<br/><span class="muted">${escapeHtml(request.clientDisplayName)}</span></td>
      <td>${intentCell}</td>
      <td>${escapeHtml(request.matchedRuleName ?? '-')}</td>
      <td><strong>${escapeHtml(request.selectedModel)}</strong></td>
      <td>${escapeHtml(request.explanation)}</td>
      <td><a href="#" data-trace="${escapeHtml(request.traceId)}">${escapeHtml(request.traceId)}</a></td>
    </tr>`;
  }).join('');

  const captureNote = feed.promptCaptureEnabled
    ? ''
    : '<p class="muted">Prompt text capture is disabled on the router. Set <code>Telemetry__CapturePromptText=true</code> to show prompt previews.</p>';

  return `<!DOCTYPE html>
  <html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <style nonce="${nonce}">
      body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); padding: 1rem; }
      table { border-collapse: collapse; width: 100%; }
      th, td { border-bottom: 1px solid var(--vscode-panel-border); padding: 6px 8px; text-align: left; vertical-align: top; font-size: 12px; }
      th { color: var(--vscode-descriptionForeground); }
      .muted { color: var(--vscode-descriptionForeground); }
      .ctx { color: #6a4c00; background: #fff3cd; border-radius: 4px; padding: 0 4px; font-size: 11px; }
      button { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; border-radius: 4px; padding: 6px 10px; cursor: pointer; margin-bottom: 8px; }
      a { color: var(--vscode-textLink-foreground); }
      .badge { display: inline-block; padding: 1px 6px; border-radius: 999px; font-size: 11px; font-weight: 600; color: #fff; background: #57606a; }
      .badge-simple-chat { background: #1f883d; }
      .badge-github-actions { background: #8250df; }
      .badge-launch-app { background: #0969da; }
      .badge-code-task { background: #bc4c00; }
      .badge-long-form { background: #cf222e; }
    </style>
  </head>
  <body>
    <h2>Live Routing</h2>
    <p class="muted">Prompt → processor classifies intent → rule → model. Snapshot at ${escapeHtml(feed.generatedAtUtc)}.</p>
    ${captureNote}
    <button id="refresh">Refresh</button>
    <table>
      <thead><tr><th>Time</th><th>Prompt</th><th>Intent</th><th>Rule</th><th>Model</th><th>Explanation</th><th>Trace</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="7" class="muted">No routed requests yet.</td></tr>'}</tbody>
    </table>
    <script nonce="${nonce}">
      const vscode = acquireVsCodeApi();
      document.getElementById('refresh').addEventListener('click', () => vscode.postMessage({ command: 'refresh' }));
      document.querySelectorAll('a[data-trace]').forEach(link => {
        link.addEventListener('click', event => {
          event.preventDefault();
          vscode.postMessage({ command: 'openTrace', traceId: link.getAttribute('data-trace') });
        });
      });
    </script>
  </body>
  </html>`;
}

async function buildStatusHtml(context: vscode.ExtensionContext, webview: vscode.Webview): Promise<string> {
  const status = await getStatusSummary(context);
  const nonce = createNonce();
  const dashboardUrl = getDashboardUrl();
  const routerUrl = getRouterUrl();

  return `<!DOCTYPE html>
  <html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <style nonce="${nonce}">
      body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); padding: 1rem; }
      .card { border: 1px solid var(--vscode-panel-border); border-radius: 8px; padding: 12px; margin-bottom: 12px; }
      .row { display: flex; gap: 8px; flex-wrap: wrap; }
      button, a { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; border-radius: 4px; padding: 8px 12px; text-decoration: none; cursor: pointer; }
      button:hover, a:hover { background: var(--vscode-button-hoverBackground); }
      code { font-family: var(--vscode-editor-font-family); }
      .muted { color: var(--vscode-descriptionForeground); }
    </style>
  </head>
  <body>
    <h2>Harness Status</h2>
    <div class="card">
      <div><strong>Router</strong>: ${escapeHtml(status.router)}</div>
      <div><strong>Admin</strong>: ${escapeHtml(status.admin)}</div>
      <div><strong>Dashboard</strong>: ${escapeHtml(dashboardUrl)}</div>
      <div class="muted">${escapeHtml(status.detail)}</div>
    </div>
    <div class="row">
      <button onclick="command('harness.openDashboard')">Open Dashboard</button>
      <button onclick="command('harness.explainRouting')">Explain Routing</button>
      <button onclick="command('harness.openTrace')">Open Trace</button>
      <button onclick="command('harness.showStatusPanel')">Refresh</button>
    </div>
    <p class="muted">Router URL: <code>${escapeHtml(routerUrl)}</code></p>
    <script nonce="${nonce}">
      const vscode = acquireVsCodeApi();
      function command(id) { vscode.postMessage({ command: id }); }
      window.addEventListener('message', event => console.log(event.data));
    </script>
  </body>
  </html>`;
}

function renderExplanationPanelHtml(response: PlaygroundResponse): string {
  const nonce = createNonce();
  return `<!DOCTYPE html>
  <html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <style nonce="${nonce}">
      body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); padding: 1rem; }
      pre { white-space: pre-wrap; background: var(--vscode-editor-background); padding: 12px; border-radius: 8px; }
      a { color: var(--vscode-textLink-foreground); }
    </style>
  </head>
  <body>
    <h2>Routing Explanation</h2>
    <div class="card">
      <div><strong>Profile</strong>: ${escapeHtml(response.profile)}</div>
      <div><strong>Deployment</strong>: ${escapeHtml(response.deployment)}</div>
      <div><strong>Reason</strong>: ${escapeHtml(response.reason)}</div>
      <div><strong>Prompt characters</strong>: ${response.promptCharacters}</div>
    </div>
    <pre>${escapeHtml(JSON.stringify(response.routedRequest, null, 2) ?? '')}</pre>
  </body>
  </html>`;
}

async function getStatusSummary(context: vscode.ExtensionContext) {
  try {
    const snapshot = await fetchSnapshot(context, new vscode.CancellationTokenSource().token);
    return {
      router: 'Reachable',
      admin: `Snapshot at ${snapshot.generatedAtUtc}`,
      detail: `${snapshot.connectedClients.length} client(s), ${snapshot.liveRequests.length} live request(s).`
    };
  } catch (error) {
    return {
      router: 'Unavailable',
      admin: 'Unavailable',
      detail: error instanceof Error ? error.message : 'Unable to connect to the Router/Admin endpoints.'
    };
  }
}

function getRouterUrl() {
  return getConfiguration().get<string>('routerBaseUrl') ?? 'http://localhost:5117';
}

function getAdminBaseUrl() {
  return getConfiguration().get<string>('adminBaseUrl') ?? 'http://localhost:5117';
}

function getDashboardUrl() {
  return `${getAdminBaseUrl()}/`;
}

function getTraceUrl(traceId: string) {
  return `${getAdminBaseUrl()}/admin/traces/${encodeURIComponent(traceId)}`;
}

function getConfiguration() {
  return vscode.workspace.getConfiguration('harness');
}

function getAdminApiKey() {
  return getConfiguration().get<string>('adminApiKey')?.trim() ?? '';
}

function adminAuthHeaders(): Record<string, string> {
  const key = getAdminApiKey();
  return key ? { Authorization: `Bearer ${key}` } : {};
}

async function fetchJson<T>(url: string, init: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    throw new Error(`Request to ${url} failed with ${response.status} ${response.statusText}`);
  }

  return await response.json() as T;
}

function getOrCreatePanel(context: vscode.ExtensionContext) {
  if (statusPanel) {
    return statusPanel;
  }

  statusPanel = vscode.window.createWebviewPanel(
    'harnessStatus',
    'Harness Status',
    vscode.ViewColumn.One,
    { enableScripts: true }
  );

  statusPanel.onDidDispose(() => {
    statusPanel = undefined;
  }, undefined, context.subscriptions);

  statusPanel.webview.onDidReceiveMessage(async message => {
    if (typeof message?.command !== 'string') {
      return;
    }

    await vscode.commands.executeCommand(message.command);
  }, undefined, context.subscriptions);

  return statusPanel;
}

function renderExplanationMarkdown(response: PlaygroundResponse) {
  return [
    `**Profile:** ${escapeMarkdown(response.profile)}`,
    `**Deployment:** ${escapeMarkdown(response.deployment)}`,
    `**Reason:** ${escapeMarkdown(response.reason)}`,
    `**Prompt characters:** ${response.promptCharacters}`,
    '',
    'Use the dashboard links to inspect the routed request and recent traces.'
  ].join('\n\n');
}

function renderTraceMarkdown(trace: TraceResponse) {
  return [
    `**Trace:** ${escapeMarkdown(trace.traceId)}`,
    `**Decision:** ${escapeMarkdown(trace.decision.profile)} -> ${escapeMarkdown(trace.decision.deployment)}`,
    `**Reason:** ${escapeMarkdown(trace.decision.reason)}`,
    `**Workflow:** ${escapeMarkdown(trace.workflowEngine)}`,
    `**Classification:** ${escapeMarkdown(trace.classification.intent)} / ${escapeMarkdown(trace.classification.complexity)}`,
    '',
    'Context:',
    ...trace.context.map(item => `- ${escapeMarkdown(item.key)} = ${escapeMarkdown(item.value)}`),
    '',
    'Steps:',
    ...trace.steps.map(step => `- ${escapeMarkdown(step.name)}: ${escapeMarkdown(step.outcome)}`)
  ].join('\n');
}

function escapeHtml(value: string) {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function escapeMarkdown(value: string) {
  return value.replaceAll('_', '\\_').replaceAll('*', '\\*').replaceAll('`', '\\`');
}

function createNonce() {
  const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  return Array.from({ length: 32 }, () => possible[Math.floor(Math.random() * possible.length)]).join('');
}
