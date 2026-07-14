using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace ICSharpCode.AvalonEdit.DevFlowIntegrationTests
{
	public sealed class AvalonEditSampleIntegrationTests : IntegrationTestBase
	{
		[Fact]
		public async Task Agent_ExposesAvalonEditActions()
		{
			using var client = await TryConnectAsync();
			if (client == null)
				return;

			var actions = await client.ListActionsAsync(TestContext.Current.CancellationToken);

			Assert.Contains("avedit.activate", actions);
			Assert.Contains("avedit.reset", actions);
			Assert.Contains("avedit.query.state", actions);
			Assert.Contains("avedit.query.bounds", actions);
		}

		[Fact]
		public async Task ResetAndInsertNewLine_UpdatesEditorText()
		{
			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			JsonElement before = await client.InvokeJsonAsync("avedit.reset");
			Assert.Equal(0, before.GetProperty("caretOffset").GetInt32());

			JsonElement after = await client.InvokeJsonAsync("avedit.insert-newline");

			Assert.Contains("\n", after.GetProperty("text").GetString());
			Assert.True(after.GetProperty("textLength").GetInt32() > before.GetProperty("textLength").GetInt32());
		}

		[Fact]
		public async Task TextAreaBounds_AreQueryable()
		{
			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");

			Assert.True(bounds.GetProperty("found").GetBoolean(), bounds.GetRawText());
			Assert.True(bounds.GetProperty("width").GetDouble() > 100, bounds.GetRawText());
			Assert.True(bounds.GetProperty("height").GetDouble() > 100, bounds.GetRawText());
		}

		[Fact]
		public async Task SmallNativeDrag_DoesNotLeaveSelectionModeActive()
		{
			if (!IsNativeInputEnabled())
				return;

			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			await client.InvokeAsync("avedit.reset");
			JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");
			double x = bounds.GetProperty("x").GetDouble() + 120;
			double y = bounds.GetProperty("y").GetDouble() + 30;

			await client.DragAsync(new DragRequest
			{
				Global = true,
				FromX = x,
				FromY = y,
				ToX = x + 80,
				ToY = y,
				Steps = 20
			}, TestContext.Current.CancellationToken);

			JsonElement state = await client.InvokeJsonAsync("avedit.query.state");
			Assert.Equal("None", state.GetProperty("mouseSelectionMode").GetString());
			Assert.Equal("Released", state.GetProperty("mouseLeftButton").GetString());
		}

		// Decomposes a drag gesture into press → drag-move → release, then verifies that the
		// release actually reached the TextArea. If LibreWPF drops the mouse-up, mouseSelectionMode
		// stays "Normal" and PreviewMouseUp never fires — this test isolates that failure from the
		// combined DragAsync path (which hides which of the three phases was lost).
		[Fact]
		public async Task NativeRelease_DeliversMouseUp_AndClearsSelectionMode()
		{
			if (!IsNativeInputEnabled())
				return;

			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			await client.InvokeAsync("avedit.reset");
			await client.InvokeAsync("avedit.cancel-selection");

			JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");
			double x = bounds.GetProperty("x").GetDouble() + 30;
			double y = bounds.GetProperty("y").GetDouble() + 20;

			var ct = TestContext.Current.CancellationToken;
			await client.PressAsync(x, y, ct);
			await client.DragMoveAsync(x + 60, y, ct);
			await client.ReleaseAsync(x + 60, y, ct);

			JsonElement state = await client.InvokeJsonAsync("avedit.query.state");

			// The mouse-up must have been delivered and processed.
			int upCount = 0;
			if (state.TryGetProperty("counts", out var counts) && counts.TryGetProperty("PreviewMouseUp", out var up))
				upCount = up.GetInt32();
			Assert.True(upCount > 0, "PreviewMouseUp never fired after native release — LibreWPF dropped the mouse-up. state=" + state.GetRawText());

			Assert.Equal("Released", state.GetProperty("lastTextAreaLeftButton").GetString());
			Assert.Equal("None", state.GetProperty("mouseSelectionMode").GetString());
		}

		// The reported "runaway" symptom: after the button is released, a plain hover move (no button)
		// must NOT extend the selection. If the mouse-up was lost, mode stays Normal and this hover
		// grows the selection — reproducing the mysterious large selection.
		[Fact]
		public async Task HoverMoveAfterRelease_DoesNotExtendSelection()
		{
			if (!IsNativeInputEnabled())
				return;

			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			await client.InvokeAsync("avedit.reset");
			await client.InvokeAsync("avedit.cancel-selection");

			JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");
			double x = bounds.GetProperty("x").GetDouble() + 30;
			double y = bounds.GetProperty("y").GetDouble() + 20;

			var ct = TestContext.Current.CancellationToken;
			await client.PressAsync(x, y, ct);
			await client.DragMoveAsync(x + 40, y, ct);
			await client.ReleaseAsync(x + 40, y, ct);

			JsonElement afterRelease = await client.InvokeJsonAsync("avedit.query.state");
			int lengthAfterRelease = afterRelease.GetProperty("selectionLength").GetInt32();

			// Hover far to the right WITHOUT any button held.
			await client.MoveAsync(x + 200, y, ct);

			JsonElement afterHover = await client.InvokeJsonAsync("avedit.query.state");
			int lengthAfterHover = afterHover.GetProperty("selectionLength").GetInt32();

			Assert.Equal("None", afterHover.GetProperty("mouseSelectionMode").GetString());
			Assert.Equal(lengthAfterRelease, lengthAfterHover);
		}

		// Double-click drives WholeWord mode (a different SelectionMouseHandler path than plain
		// drag) via a real native down+up click, not a synthesized drag. If mouse-up were still
		// broken this would leave mode stuck at WholeWord instead of resetting to None.
		[Fact]
		public async Task DoubleClick_SelectsWord_AndReleaseClearsMode()
		{
			if (!IsNativeInputEnabled())
				return;

			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			await client.InvokeAsync("avedit.reset"); // text: "alpha beta gamma\nsecond line\nthird line"
			JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");
			double x = bounds.GetProperty("x").GetDouble() + 10;
			double y = bounds.GetProperty("y").GetDouble() + 10;

			var ct = TestContext.Current.CancellationToken;
			await client.ClickAsync(x, y, clickCount: 2, ct);

			JsonElement state = await client.InvokeJsonAsync("avedit.query.state");
			Assert.Equal("None", state.GetProperty("mouseSelectionMode").GetString());
			Assert.Equal("Released", state.GetProperty("mouseLeftButton").GetString());
			int selLength = state.GetProperty("selectionLength").GetInt32();
			Assert.True(selLength > 0 && selLength < 17, "double-click should select a single word, not the whole line. state=" + state.GetRawText());
		}

		// Triple-click drives WholeLine mode. Line 1 of the reset text ("alpha beta gamma") is
		// exactly 17 chars, so this asserts the exact selection length, not just "> 0".
		[Fact]
		public async Task TripleClick_SelectsWholeLine_AndReleaseClearsMode()
		{
			if (!IsNativeInputEnabled())
				return;

			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			await client.InvokeAsync("avedit.reset");
			JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");
			double x = bounds.GetProperty("x").GetDouble() + 10;
			double y = bounds.GetProperty("y").GetDouble() + 10;

			var ct = TestContext.Current.CancellationToken;
			await client.ClickAsync(x, y, clickCount: 3, ct);

			JsonElement state = await client.InvokeJsonAsync("avedit.query.state");
			Assert.Equal("None", state.GetProperty("mouseSelectionMode").GetString());
			Assert.Equal("Released", state.GetProperty("mouseLeftButton").GetString());
			Assert.Equal(17, state.GetProperty("selectionLength").GetInt32());
		}

		// The actual reported bug: if a double/triple-click leaves mode stuck at WholeWord/WholeLine
		// (and mouseLeftButton doesn't correctly read Released), the very next hover move - with NO
		// button held - won't get cancelled by ShouldCancelSelectionOnMouseMove and will keep treating
		// the mouse move as extending the word/line selection outward. This is what "randomly enters a
		// huge selection" looks like from the user's seat. A stuck mode alone (previous two tests)
		// is a real bug, but THIS is the test that shows whether it actually cascades into runaway
		// selection growth, or self-heals on the next input.
		[Fact]
		public async Task DoubleClick_ThenHoverMove_DoesNotEnterRunawaySelection()
		{
			if (!IsNativeInputEnabled())
				return;

			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			await client.InvokeAsync("avedit.reset"); // text: "alpha beta gamma\nsecond line\nthird line"
			JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");
			double x = bounds.GetProperty("x").GetDouble() + 10;
			double y = bounds.GetProperty("y").GetDouble() + 10;

			var ct = TestContext.Current.CancellationToken;
			await client.ClickAsync(x, y, clickCount: 2, ct);

			JsonElement afterClick = await client.InvokeJsonAsync("avedit.query.state");
			int lengthAfterClick = afterClick.GetProperty("selectionLength").GetInt32();

			// Hover far to the right, well past the end of line 1, WITHOUT any button held.
			await client.MoveAsync(x + 250, y, ct);

			JsonElement afterHover = await client.InvokeJsonAsync("avedit.query.state");
			int lengthAfterHover = afterHover.GetProperty("selectionLength").GetInt32();

			Assert.Equal("None", afterHover.GetProperty("mouseSelectionMode").GetString());
			Assert.Equal(lengthAfterClick, lengthAfterHover);
		}

		[Fact]
		public async Task TripleClick_ThenHoverMove_DoesNotEnterRunawaySelection()
		{
			if (!IsNativeInputEnabled())
				return;

			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			await client.InvokeAsync("avedit.reset");
			JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");
			double x = bounds.GetProperty("x").GetDouble() + 10;
			double y = bounds.GetProperty("y").GetDouble() + 10;

			var ct = TestContext.Current.CancellationToken;
			await client.ClickAsync(x, y, clickCount: 3, ct);

			JsonElement afterClick = await client.InvokeJsonAsync("avedit.query.state");
			int lengthAfterClick = afterClick.GetProperty("selectionLength").GetInt32();

			await client.MoveAsync(x + 250, y + 100, ct); // down and to the right, past other lines too

			JsonElement afterHover = await client.InvokeJsonAsync("avedit.query.state");
			int lengthAfterHover = afterHover.GetProperty("selectionLength").GetInt32();

			Assert.Equal("None", afterHover.GetProperty("mouseSelectionMode").GetString());
			Assert.Equal(lengthAfterClick, lengthAfterHover);
		}

		// Exact end-to-end reproduction of the reported bug, confirmed against a real manual repro
		// (see /tmp/opendevelop-avalonedit-selection.log): a double-click's mouse-up is silently
		// dropped, which leaves mouseSelectionMode stuck at WholeWord and Mouse.LeftButton stuck
		// reporting Pressed. From that point on, ShouldCancelSelectionOnMouseMove never trips (it
		// only cancels when the button reads Released), so ANY ordinary mouse move afterward - no
		// click, no drag, just moving the cursor - gets treated as continuing the abandoned
		// word-selection drag. The moment the cursor crosses onto a different line,
		// ExtendSelectionToMouse's WholeWord branch unions the original word with whatever word is
		// now under the cursor, and the selection balloons.
		//
		// A single teleporting move (MoveAsync to one distant point) reliably does NOT reproduce
		// this - it turns out AvalonEdit's WholeWord extend logic needs the cursor to actually be
		// over rendered text (a single jump can land past the end of short content, where there's
		// no VisualLine at all and the extend silently no-ops). Real mouse movement is many small
		// steps landing on real text the whole way, so this drives many one-word moves instead.
		[Fact]
		public async Task DoubleClick_ThenPlainHoverAcrossLines_DoesNotBalloonSelectionToWholeDocument()
		{
			if (!IsNativeInputEnabled())
				return;

			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			JsonElement reset = await client.InvokeJsonAsync("avedit.reset");
			int fullDocumentLength = reset.GetProperty("textLength").GetInt32();

			JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");
			double x0 = bounds.GetProperty("x").GetDouble() + 10;
			double y0 = bounds.GetProperty("y").GetDouble() + 10;

			var ct = TestContext.Current.CancellationToken;
			await client.ClickAsync(x0, y0, clickCount: 2, ct); // double-click "alpha" on line 1

			JsonElement afterClick = await client.InvokeJsonAsync("avedit.query.state");
			int lengthAfterClick = afterClick.GetProperty("selectionLength").GetInt32();
			Assert.True(lengthAfterClick > 0 && lengthAfterClick < fullDocumentLength,
				"precondition: double-click should select just 'alpha', not the whole document. state=" + afterClick.GetRawText());

			// No click, no drag - just many small ordinary hover moves drifting down and to the
			// right across real text lines, like a user's hand casually moving the mouse.
			for (int i = 1; i <= 200; i++) {
				await client.MoveAsync(x0 + i * 0.8, y0 + i * 2.0, ct);
			}

			JsonElement afterHover = await client.InvokeJsonAsync("avedit.query.state");
			int lengthAfterHover = afterHover.GetProperty("selectionLength").GetInt32();

			Assert.True(lengthAfterHover == lengthAfterClick,
				$"BUG REPRODUCED: plain hover moves (no button held) after a double-click grew the " +
				$"selection from {lengthAfterClick} to {lengthAfterHover} chars (document is {fullDocumentLength} chars). " +
				$"state after hover={afterHover.GetRawText()}");
		}

		// After a drag-select cycle completes, a fresh plain click elsewhere must still behave
		// normally: clear the old selection and reposition the caret. Verifies residual drag state
		// doesn't wedge subsequent ordinary clicks.
		[Fact]
		public async Task ClickAfterDrag_ClearsSelectionAndRepositionsCaret()
		{
			if (!IsNativeInputEnabled())
				return;

			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			await client.InvokeAsync("avedit.reset");
			JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");
			double x = bounds.GetProperty("x").GetDouble() + 10;
			double y = bounds.GetProperty("y").GetDouble() + 10;

			var ct = TestContext.Current.CancellationToken;
			await client.PressAsync(x, y, ct);
			await client.DragMoveAsync(x + 60, y, ct);
			await client.ReleaseAsync(x + 60, y, ct);

			JsonElement afterDrag = await client.InvokeJsonAsync("avedit.query.state");
			Assert.False(afterDrag.GetProperty("selectionIsEmpty").GetBoolean(), "drag should have produced a selection to clear");

			await client.ClickAsync(x + 5, y, clickCount: 1, ct);

			JsonElement afterClick = await client.InvokeJsonAsync("avedit.query.state");
			Assert.Equal("None", afterClick.GetProperty("mouseSelectionMode").GetString());
			Assert.True(afterClick.GetProperty("selectionIsEmpty").GetBoolean(), "plain click after a drag should clear the prior selection");
		}

		// Stress loop: the reported bug was intermittent ("从时不时" / "from time to time"), so a
		// single pass proved the fix works once but not that it's reliable. Repeats a full
		// press→drag→release cycle at varying offsets and asserts mode resets to None after
		// EVERY iteration, not just the last — an intermittent regression would show up as an
		// isolated failure partway through rather than a clean run or a total failure.
		[Fact]
		public async Task RepeatedDragSelectCycles_NeverLeaveSelectionModeStuck()
		{
			if (!IsNativeInputEnabled())
				return;

			using var client = await TryConnectAsync();
			if (client == null)
				return;

			await client.InvokeAsync("avedit.activate");
			var ct = TestContext.Current.CancellationToken;

			for (int i = 0; i < 20; i++) {
				await client.InvokeAsync("avedit.reset");
				JsonElement bounds = await client.InvokeJsonAsync("avedit.query.bounds", "text-area");
				double baseX = bounds.GetProperty("x").GetDouble() + 10 + (i % 5) * 15;
				double y = bounds.GetProperty("y").GetDouble() + 10;

				await client.PressAsync(baseX, y, ct);
				await client.DragMoveAsync(baseX + 20, y, ct);
				await client.DragMoveAsync(baseX + 50, y, ct);
				await client.ReleaseAsync(baseX + 50, y, ct);

				JsonElement state = await client.InvokeJsonAsync("avedit.query.state");
				Assert.Equal("None", state.GetProperty("mouseSelectionMode").GetString());
				Assert.Equal("Released", state.GetProperty("mouseLeftButton").GetString());
			}
		}

		static bool IsNativeInputEnabled()
			=> System.Environment.GetEnvironmentVariable("AVALONEDIT_NATIVE_INPUT_TESTS") == "1";
	}
}
