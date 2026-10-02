# Copilot Instructions

## General Guidelines
- User prefers responses in English instead of French.
- All questions and answers must be in English, consistent with .github/copilot-instructions.md.
- Prefer an incremental, collaborative implementation process: first understand the existing system and API contracts, then make small verified changes.
- Remove redundant UI controls when equivalent functionality exists elsewhere.
- Strongly prefer calendar/date-picker controls over manually typing dates in calendar format.
- When iterating on the top-bar design, preserve the refreshed in-app navigation bar the user liked; add the Windows-style custom window title bar above it rather than reverting the navigation redesign.
- Do not push changes to the remote unless the user explicitly asks; when preparing a local executable, build/publish locally only and do not commit or push.

## Project-Specific Rules
- For backend integration, reuse the existing Football Hero pattern which includes persistent bearer-token storage under ApplicationData, a shared static HttpClient with a configured BaseAddress, generic GET/Post/SecurePost methods, Authorization headers, and token deletion/navigation to login on HTTP 401 when adapting FAAH if requested.
- The backend now supports comprehensive data-source filtering, sorting, date ranges, and pagination. Before implementation, discuss and agree on which filters belong in the desktop news UI rather than immediately adding every backend parameter. Filters should be explicitly labeled and placed in a side-panel/table-style area to enhance usability and clarity, rather than relying on compact placeholder-only controls in the toolbar. The sidebar should begin with Search and contain the remaining filters.
- For news UI, keep the Read Article action but reposition the button to improve layout; avoid large per-item buttons and focus on displaying source-level classification/analysis related to each news item while maintaining a compact layout for the main content area.
- For the news source detail UI, show a compact source metadata section (original title, URL, published date), a limited source-content excerpt, classification data (importance, sentiment, reason, assets, niches), and a large analysis section with summary, direction, market sentiment, confidence/risk/timeframe, and other non-asset analysis fields. Signals should be handled on a separate page, not this news detail page.
- The published date pickers must be compact controls in the top news header, immediately to the left of the Refresh button; they must not occupy width inside the right filter sidebar.
- For portfolio editing, reuse the existing create portfolio view: when it receives an existing portfolio ID/model, initialize values and PATCH only changed fields; when no portfolio is supplied, create a new portfolio.
- For the live_market_worker health UI, derive the worker indicator only from its own healthy/status liveness and Yahoo websocket connection, never from live/delayed/unavailable price counts. Price counts may vary while the worker is healthy, and a non-null operational error such as Redis trouble may coexist with healthy Yahoo connectivity.