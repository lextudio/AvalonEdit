// Cross-platform (non-Windows) IME support for AvalonEdit, backed by TextCore.Uno's
// CoreTextEditContext/MacOSTextInputAdapter (the original NSTextInputClient bridge -
// see doc comments on LeXtudio.UI.Text.Core.MacOSTextInputAdapter). ImeSupport.cs still owns the
// Windows imm32 path unchanged; this class is only ever constructed/used when
// !OperatingSystem.IsWindows().

using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

using ICSharpCode.AvalonEdit.Document;
using LeXtudio.UI.Text.Core;

namespace ICSharpCode.AvalonEdit.Editing
{
	sealed class MacImeSupport : IDisposable
	{
		readonly TextArea textArea;
		CoreTextEditContext context;
		bool attachAttempted;

		public MacImeSupport(TextArea textArea)
		{
			this.textArea = textArea ?? throw new ArgumentNullException(nameof(textArea));
		}

		public void OnGotKeyboardFocus()
		{
			EnsureAttached();
			if (context != null) {
				context.NotifyFocusEnter();
				UpdateCompositionWindow();
			}
		}

		public void OnLostKeyboardFocus()
		{
			context?.NotifyFocusLeave();
		}

		public void UpdateCompositionWindow()
		{
			if (context == null)
				return;
			Window window = Window.GetWindow(textArea);
			if (window == null)
				return;

			Rect caretRect = textArea.Caret.CalculateCaretRectangle();
			if (caretRect.IsEmpty)
				return;
			caretRect.Offset(-textArea.TextView.ScrollOffset.X, -textArea.TextView.ScrollOffset.Y);

			GeneralTransform transform = textArea.TextView.TransformToAncestor(window);
			Rect windowRect = transform.TransformBounds(caretRect);

			double dpiScale = VisualTreeHelper.GetDpi(textArea).DpiScaleX;
			context.NotifyCaretRectChanged(
				windowRect.X * dpiScale, windowRect.Y * dpiScale,
				windowRect.Width * dpiScale, windowRect.Height * dpiScale, dpiScale);
		}

		void EnsureAttached()
		{
			if (attachAttempted)
				return;
			// The native window handle doesn't change for the lifetime of a TextArea, so a single
			// attach attempt per control is enough - unlike the Windows imm32 path, there's no
			// per-focus context create/destroy cycle here.
			attachAttempted = true;

			Window window = Window.GetWindow(textArea);
			if (window == null)
				return;
			if (!TryGetNativeWindowHandle(window, out IntPtr handle) || handle == IntPtr.Zero)
				return;

			var newContext = CoreTextServicesManager.GetForCurrentView().CreateEditContext();
			newContext.SelectionRequested += OnSelectionRequested;
			newContext.TextUpdating += OnTextUpdating;
			if (!newContext.AttachToWindowHandle(handle)) {
				newContext.Dispose();
				return;
			}
			context = newContext;
		}

		/// <summary>
		/// Resolves the native NSWindow*/HWND/X11 handle backing <paramref name="window"/> via
		/// System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryGetNativeWindowHandle, called
		/// through reflection rather than a compile-time reference. The ProGPU.Wpf build currently
		/// pinned by this repo's local nuget feed predates that method (see doc/technotes -
		/// ProGPU.Wpf's package id was renamed mid-flight and the local feed under
		/// librewpf/artifacts/packages/Release/NonShipping hasn't been republished with it yet).
		/// Reflection means this keeps compiling either way: it's a silent no-op (no IME) against
		/// today's pinned build, and starts working with zero code changes once that package is
		/// updated - a hard ProjectReference would either fail to compile today or require
		/// re-publishing that package as part of an unrelated dependency-rename migration.
		/// </summary>
		static bool TryGetNativeWindowHandle(Window window, out IntPtr handle)
		{
			handle = IntPtr.Zero;
			try {
				Type activationType = AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(a => a.GetName().Name == "ProGPU.Wpf")
					?.GetType("System.Windows.Media.ProGPU.WpfPortableWindowActivation");
				MethodInfo method = activationType?.GetMethod("TryGetNativeWindowHandle", BindingFlags.Public | BindingFlags.Static);
				if (method == null)
					return false;

				object[] args = { window, IntPtr.Zero };
				bool ok = (bool)method.Invoke(null, args);
				if (ok)
					handle = (IntPtr)args[1];
				return ok;
			} catch (Exception) {
				return false;
			}
		}

		void OnSelectionRequested(object sender, CoreTextSelectionRequestedEventArgs e)
		{
			ISegment segment = textArea.Selection.SurroundingSegment;
			int start = segment != null ? segment.Offset : textArea.Caret.Offset;
			int end = segment != null ? segment.EndOffset : textArea.Caret.Offset;
			e.Request.StartCaretPosition = start;
			e.Request.EndCaretPosition = end;
		}

		void OnTextUpdating(object sender, CoreTextTextUpdatingEventArgs e)
		{
			int start = Math.Min(e.Range.StartCaretPosition, e.Range.EndCaretPosition);
			int end = Math.Max(e.Range.StartCaretPosition, e.Range.EndCaretPosition);
			start = Math.Max(0, Math.Min(start, textArea.Document.TextLength));
			end = Math.Max(start, Math.Min(end, textArea.Document.TextLength));

			if (!textArea.ReadOnlySectionProvider.CanInsert(start))
				return;

			textArea.Document.Replace(start, end - start, e.NewText);

			int newCaretOffset = Math.Max(0, Math.Min(e.NewSelection.EndCaretPosition, textArea.Document.TextLength));
			textArea.Caret.Offset = newCaretOffset;
		}

		public void Dispose()
		{
			if (context != null) {
				context.SelectionRequested -= OnSelectionRequested;
				context.TextUpdating -= OnTextUpdating;
				context.Dispose();
				context = null;
			}
		}
	}
}
