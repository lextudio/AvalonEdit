using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ICSharpCode.AvalonEdit.DevFlowIntegrationTests
{
	public sealed class DevFlowClient : IDisposable
	{
		readonly HttpClient http;

		public DevFlowClient(int port)
		{
			http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
			http.Timeout = TimeSpan.FromSeconds(60);
		}

		public static int? ResolvePortOrNull()
			=> int.TryParse(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT"), out int p) && p > 0
				? p
				: (int?)null;

		public async Task<bool> IsReachableAsync(CancellationToken ct = default)
		{
			try {
				using var resp = await http.GetAsync("/api/v1/agent/status", ct).ConfigureAwait(false);
				return resp.IsSuccessStatusCode;
			} catch {
				return false;
			}
		}

		public async Task<List<string>> ListActionsAsync(CancellationToken ct = default)
		{
			string raw = await GetStringAsync("/api/v1/invoke/actions", ct).ConfigureAwait(false);
			using var doc = JsonDocument.Parse(raw);
			var result = new List<string>();
			if (doc.RootElement.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement action in actions.EnumerateArray()) {
					if (action.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
						result.Add(name.GetString());
				}
			}
			return result;
		}

		public async Task<string> InvokeAsync(string action, params object[] args)
		{
			string body = JsonSerializer.Serialize(new { args = args ?? Array.Empty<object>() });
			using var content = new StringContent(body, Encoding.UTF8, "application/json");
			using var resp = await http.PostAsync($"/api/v1/invoke/actions/{action}", content).ConfigureAwait(false);
			string raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
			if (!resp.IsSuccessStatusCode)
				throw new HttpRequestException($"DevFlow action '{action}' returned {(int)resp.StatusCode}: {raw}");
			return ExtractResult(raw);
		}

		public async Task<JsonElement> InvokeJsonAsync(string action, params object[] args)
		{
			string raw = await InvokeAsync(action, args).ConfigureAwait(false);
			return JsonDocument.Parse(raw).RootElement.Clone();
		}

		public async Task<JsonElement> DragAsync(DragRequest request, CancellationToken ct = default)
		{
			string body = JsonSerializer.Serialize(request);
			using var content = new StringContent(body, Encoding.UTF8, "application/json");
			using var resp = await http.PostAsync("/api/v1/ui/actions/drag", content, ct).ConfigureAwait(false);
			string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
			if (!resp.IsSuccessStatusCode)
				throw new HttpRequestException($"DevFlow drag returned {(int)resp.StatusCode}: {raw}");
			return JsonDocument.Parse(raw).RootElement.Clone();
		}

		// Discrete pointer primitives. Unlike DragAsync (a single monolithic press→move→release),
		// these let a test drive one phase at a time so it can pin *which* native event is lost.
		public Task<JsonElement> PressAsync(double x, double y, CancellationToken ct = default)
			=> PostPointAsync("/api/v1/ui/actions/press", x, y, ct);

		public Task<JsonElement> DragMoveAsync(double x, double y, CancellationToken ct = default)
			=> PostPointAsync("/api/v1/ui/actions/drag-move", x, y, ct);

		public Task<JsonElement> ReleaseAsync(double x, double y, CancellationToken ct = default)
			=> PostPointAsync("/api/v1/ui/actions/release", x, y, ct);

		// Plain hover move with NO button held (distinct from drag-move).
		public Task<JsonElement> MoveAsync(double x, double y, CancellationToken ct = default)
			=> PostPointAsync("/api/v1/ui/actions/move", x, y, ct);

		// A full native down+up click (not a drag). clickCount=2/3 drives AvalonEdit's
		// WholeWord/WholeLine selection modes, a code path our drag-based tests don't touch.
		public async Task<JsonElement> ClickAsync(double x, double y, int clickCount = 1, CancellationToken ct = default)
		{
			string body = JsonSerializer.Serialize(new { x, y, global = true, clickCount });
			using var content = new StringContent(body, Encoding.UTF8, "application/json");
			using var resp = await http.PostAsync("/api/v1/ui/actions/click", content, ct).ConfigureAwait(false);
			string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
			if (!resp.IsSuccessStatusCode)
				throw new HttpRequestException($"DevFlow click returned {(int)resp.StatusCode}: {raw}");
			return JsonDocument.Parse(raw).RootElement.Clone();
		}

		async Task<JsonElement> PostPointAsync(string path, double x, double y, CancellationToken ct)
		{
			string body = JsonSerializer.Serialize(new { x, y, global = true });
			using var content = new StringContent(body, Encoding.UTF8, "application/json");
			using var resp = await http.PostAsync(path, content, ct).ConfigureAwait(false);
			string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
			if (!resp.IsSuccessStatusCode)
				throw new HttpRequestException($"DevFlow '{path}' returned {(int)resp.StatusCode}: {raw}");
			return JsonDocument.Parse(raw).RootElement.Clone();
		}

		async Task<string> GetStringAsync(string path, CancellationToken ct)
		{
			using var resp = await http.GetAsync(path, ct).ConfigureAwait(false);
			resp.EnsureSuccessStatusCode();
			return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		}

		static string ExtractResult(string raw)
		{
			if (string.IsNullOrWhiteSpace(raw))
				return raw;
			try {
				using var doc = JsonDocument.Parse(raw);
				if (doc.RootElement.ValueKind == JsonValueKind.Object) {
					foreach (string key in new[] { "returnValue", "result", "value", "output", "data" }) {
						if (doc.RootElement.TryGetProperty(key, out JsonElement el))
							return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
					}
				}
			} catch (JsonException) {
			}
			return raw;
		}

		public void Dispose()
		{
			http.Dispose();
		}
	}

	public sealed class DragRequest
	{
		public bool Global { get; set; }
		public double FromX { get; set; }
		public double FromY { get; set; }
		public double ToX { get; set; }
		public double ToY { get; set; }
		public int Steps { get; set; } = 10;
	}
}
