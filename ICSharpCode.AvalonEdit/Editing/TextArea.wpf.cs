// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit.Editing
{
	public partial class TextArea : IScrollInfo, IWeakEventListener
	{
		internal ImeSupport ime;

		IScrollInfo scrollInfo;
		ScrollViewer scrollOwner;
		bool canVerticallyScroll, canHorizontallyScroll;
		bool isMouseCursorHidden;

		static partial void InitializeWpfDefaults()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(TextArea),
													 new FrameworkPropertyMetadata(typeof(TextArea)));
			KeyboardNavigation.IsTabStopProperty.OverrideMetadata(
				typeof(TextArea), new FrameworkPropertyMetadata(Boxes.True));
			KeyboardNavigation.TabNavigationProperty.OverrideMetadata(
				typeof(TextArea), new FrameworkPropertyMetadata(KeyboardNavigationMode.None));
			FocusableProperty.OverrideMetadata(
				typeof(TextArea), new FrameworkPropertyMetadata(Boxes.True));
		}

		partial void InitializeIme()
		{
			ime = new ImeSupport(this);
		}

		partial void AttachTypingEvents()
		{
			// Use the PreviewMouseMove event in case some other editor layer consumes the MouseMove event (e.g. SD's InsertionCursorLayer)
			this.MouseEnter += delegate { ShowMouseCursor(); };
			this.MouseLeave += delegate { ShowMouseCursor(); };
			this.PreviewMouseMove += delegate { ShowMouseCursor(); };
			this.TouchEnter += delegate { ShowMouseCursor(); };
			this.TouchLeave += delegate { ShowMouseCursor(); };
			this.PreviewTouchMove += delegate { ShowMouseCursor(); };
		}

		partial void RequestSelectionValidationAsync()
		{
			Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(EnsureSelectionValid));
		}

		partial void InvalidateRequery()
		{
			CommandManager.InvalidateRequerySuggested();
		}

		partial void SubscribeToDocumentEvents(TextDocument document)
		{
			if (document != null) {
				TextDocumentWeakEventManager.Changing.AddListener(document, this);
				TextDocumentWeakEventManager.Changed.AddListener(document, this);
				TextDocumentWeakEventManager.UpdateStarted.AddListener(document, this);
				TextDocumentWeakEventManager.UpdateFinished.AddListener(document, this);
			}
		}

		partial void UnsubscribeFromDocumentEvents(TextDocument document)
		{
			if (document != null) {
				TextDocumentWeakEventManager.Changing.RemoveListener(document, this);
				TextDocumentWeakEventManager.Changed.RemoveListener(document, this);
				TextDocumentWeakEventManager.UpdateStarted.RemoveListener(document, this);
				TextDocumentWeakEventManager.UpdateFinished.RemoveListener(document, this);
			}
		}

		partial void SubscribeToOptionsEvents(TextEditorOptions options)
		{
			if (options != null) {
				PropertyChangedWeakEventManager.AddListener(options, this);
			}
		}

		partial void UnsubscribeFromOptionsEvents(TextEditorOptions options)
		{
			if (options != null) {
				PropertyChangedWeakEventManager.RemoveListener(options, this);
			}
		}

		void ShowMouseCursor()
		{
			if (this.isMouseCursorHidden) {
				System.Windows.Forms.Cursor.Show();
				this.isMouseCursorHidden = false;
			}
		}

		void HideMouseCursor()
		{
			if (Options.HideCursorWhileTyping && !this.isMouseCursorHidden && this.IsMouseOver) {
				this.isMouseCursorHidden = true;
				System.Windows.Forms.Cursor.Hide();
			}
		}

		void ApplyScrollInfo()
		{
			if (scrollInfo != null) {
				scrollInfo.ScrollOwner = scrollOwner;
				scrollInfo.CanVerticallyScroll = canVerticallyScroll;
				scrollInfo.CanHorizontallyScroll = canHorizontallyScroll;
				scrollOwner = null;
			}
		}

		#region ReceiveWeakEvent
		/// <inheritdoc cref="IWeakEventListener.ReceiveWeakEvent"/>
		protected virtual bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
		{
			if (managerType == typeof(TextDocumentWeakEventManager.Changing)) {
				OnDocumentChanging();
				return true;
			} else if (managerType == typeof(TextDocumentWeakEventManager.Changed)) {
				OnDocumentChanged((DocumentChangeEventArgs)e);
				return true;
			} else if (managerType == typeof(TextDocumentWeakEventManager.UpdateStarted)) {
				OnUpdateStarted();
				return true;
			} else if (managerType == typeof(TextDocumentWeakEventManager.UpdateFinished)) {
				OnUpdateFinished();
				return true;
			} else if (managerType == typeof(PropertyChangedWeakEventManager)) {
				OnOptionChanged((System.ComponentModel.PropertyChangedEventArgs)e);
				return true;
			}
			return false;
		}

		bool IWeakEventListener.ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
		{
			return ReceiveWeakEvent(managerType, sender, e);
		}
		#endregion

		/// <inheritdoc/>
		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			scrollInfo = textView;
			ApplyScrollInfo();
		}

		#region IScrollInfo implementation
		bool IScrollInfo.CanVerticallyScroll {
			get { return scrollInfo != null ? scrollInfo.CanVerticallyScroll : false; }
			set {
				canVerticallyScroll = value;
				if (scrollInfo != null)
					scrollInfo.CanVerticallyScroll = value;
			}
		}

		bool IScrollInfo.CanHorizontallyScroll {
			get { return scrollInfo != null ? scrollInfo.CanHorizontallyScroll : false; }
			set {
				canHorizontallyScroll = value;
				if (scrollInfo != null)
					scrollInfo.CanHorizontallyScroll = value;
			}
		}

		double IScrollInfo.ExtentWidth {
			get { return scrollInfo != null ? scrollInfo.ExtentWidth : 0; }
		}

		double IScrollInfo.ExtentHeight {
			get { return scrollInfo != null ? scrollInfo.ExtentHeight : 0; }
		}

		double IScrollInfo.ViewportWidth {
			get { return scrollInfo != null ? scrollInfo.ViewportWidth : 0; }
		}

		double IScrollInfo.ViewportHeight {
			get { return scrollInfo != null ? scrollInfo.ViewportHeight : 0; }
		}

		double IScrollInfo.HorizontalOffset {
			get { return scrollInfo != null ? scrollInfo.HorizontalOffset : 0; }
		}

		double IScrollInfo.VerticalOffset {
			get { return scrollInfo != null ? scrollInfo.VerticalOffset : 0; }
		}

		ScrollViewer IScrollInfo.ScrollOwner {
			get { return scrollInfo != null ? scrollInfo.ScrollOwner : null; }
			set {
				if (scrollInfo != null)
					scrollInfo.ScrollOwner = value;
				else
					scrollOwner = value;
			}
		}

		void IScrollInfo.LineUp()
		{
			if (scrollInfo != null) scrollInfo.LineUp();
		}

		void IScrollInfo.LineDown()
		{
			if (scrollInfo != null) scrollInfo.LineDown();
		}

		void IScrollInfo.LineLeft()
		{
			if (scrollInfo != null) scrollInfo.LineLeft();
		}

		void IScrollInfo.LineRight()
		{
			if (scrollInfo != null) scrollInfo.LineRight();
		}

		void IScrollInfo.PageUp()
		{
			if (scrollInfo != null) scrollInfo.PageUp();
		}

		void IScrollInfo.PageDown()
		{
			if (scrollInfo != null) scrollInfo.PageDown();
		}

		void IScrollInfo.PageLeft()
		{
			if (scrollInfo != null) scrollInfo.PageLeft();
		}

		void IScrollInfo.PageRight()
		{
			if (scrollInfo != null) scrollInfo.PageRight();
		}

		void IScrollInfo.MouseWheelUp()
		{
			if (scrollInfo != null) scrollInfo.MouseWheelUp();
		}

		void IScrollInfo.MouseWheelDown()
		{
			if (scrollInfo != null) scrollInfo.MouseWheelDown();
		}

		void IScrollInfo.MouseWheelLeft()
		{
			if (scrollInfo != null) scrollInfo.MouseWheelLeft();
		}

		void IScrollInfo.MouseWheelRight()
		{
			if (scrollInfo != null) scrollInfo.MouseWheelRight();
		}

		void IScrollInfo.SetHorizontalOffset(double offset)
		{
			if (scrollInfo != null) scrollInfo.SetHorizontalOffset(offset);
		}

		void IScrollInfo.SetVerticalOffset(double offset)
		{
			if (scrollInfo != null) scrollInfo.SetVerticalOffset(offset);
		}

		Rect IScrollInfo.MakeVisible(System.Windows.Media.Visual visual, Rect rectangle)
		{
			if (scrollInfo != null)
				return scrollInfo.MakeVisible(visual, rectangle);
			else
				return Rect.Empty;
		}
		#endregion

		#region Focus Handling (Show/Hide Caret)
		/// <inheritdoc/>
		protected override void OnMouseDown(MouseButtonEventArgs e)
		{
			base.OnMouseDown(e);
			Focus();
		}

		/// <inheritdoc/>
		protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
		{
			base.OnGotKeyboardFocus(e);
			// First activate IME, then show caret
			ime.OnGotKeyboardFocus(e);
			caret.Show();
		}

		/// <inheritdoc/>
		protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
		{
			base.OnLostKeyboardFocus(e);
			caret.Hide();
			ime.OnLostKeyboardFocus(e);
		}
		#endregion

		#region OnTextInput / RemoveSelectedText / ReplaceSelectionWithText
		/// <summary>
		/// Occurs when the TextArea receives text input.
		/// This is like the <see cref="UIElement.TextInput"/> event,
		/// but occurs immediately before the TextArea handles the TextInput event.
		/// </summary>
		public event TextCompositionEventHandler TextEntering;

		/// <summary>
		/// Occurs when the TextArea receives text input.
		/// This is like the <see cref="UIElement.TextInput"/> event,
		/// but occurs immediately after the TextArea handles the TextInput event.
		/// </summary>
		public event TextCompositionEventHandler TextEntered;

		/// <summary>
		/// Raises the TextEntering event.
		/// </summary>
		protected virtual void OnTextEntering(TextCompositionEventArgs e)
		{
			if (TextEntering != null) {
				TextEntering(this, e);
			}
		}

		/// <summary>
		/// Raises the TextEntered event.
		/// </summary>
		protected virtual void OnTextEntered(TextCompositionEventArgs e)
		{
			if (TextEntered != null) {
				TextEntered(this, e);
			}
		}

		/// <inheritdoc/>
		protected override void OnTextInput(TextCompositionEventArgs e)
		{
			//Debug.WriteLine("TextInput: Text='" + e.Text + "' SystemText='" + e.SystemText + "' ControlText='" + e.ControlText + "'");
			base.OnTextInput(e);
			if (!e.Handled && this.Document != null) {
				if (string.IsNullOrEmpty(e.Text) || e.Text == "\x1b" || e.Text == "\b") {
					// ASCII 0x1b = ESC.
					// WPF produces a TextInput event with that old ASCII control char
					// when Escape is pressed. We'll just ignore it.

					// A deadkey followed by backspace causes a textinput event for the BS character.

					// Similarly, some shortcuts like Alt+Space produce an empty TextInput event.
					// We have to ignore those (not handle them) to keep the shortcut working.
					return;
				}
				HideMouseCursor();
				PerformTextInput(e);
				e.Handled = true;
			}
		}

		/// <summary>
		/// Performs text input.
		/// This raises the <see cref="TextEntering"/> event, replaces the selection with the text,
		/// and then raises the <see cref="TextEntered"/> event.
		/// </summary>
		public void PerformTextInput(string text)
		{
			TextComposition textComposition = new TextComposition(InputManager.Current, this, text);
			TextCompositionEventArgs e = new TextCompositionEventArgs(Keyboard.PrimaryDevice, textComposition);
			e.RoutedEvent = TextInputEvent;
			PerformTextInput(e);
		}

		/// <summary>
		/// Performs text input.
		/// This raises the <see cref="TextEntering"/> event, replaces the selection with the text,
		/// and then raises the <see cref="TextEntered"/> event.
		/// </summary>
		public void PerformTextInput(TextCompositionEventArgs e)
		{
			if (e == null)
				throw new ArgumentNullException("e");
			if (this.Document == null)
				throw ThrowUtil.NoDocumentAssigned();
			OnTextEntering(e);
			if (!e.Handled) {
				InsertText(e.Text);
				OnTextEntered(e);
				caret.BringCaretToView();
			}
		}
		#endregion

		#region OnKeyDown/OnKeyUp
		/// <inheritdoc/>
		protected override void OnPreviewKeyDown(KeyEventArgs e)
		{
			base.OnPreviewKeyDown(e);
			foreach (TextAreaStackedInputHandler h in stackedInputHandlers) {
				if (e.Handled)
					break;
				h.OnPreviewKeyDown(e);
			}
		}

		/// <inheritdoc/>
		protected override void OnPreviewKeyUp(KeyEventArgs e)
		{
			base.OnPreviewKeyUp(e);
			foreach (TextAreaStackedInputHandler h in stackedInputHandlers) {
				if (e.Handled)
					break;
				h.OnPreviewKeyUp(e);
			}
		}

		// Make life easier for text editor extensions that use a different cursor based on the pressed modifier keys.
		/// <inheritdoc/>
		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			TextView.InvalidateCursorIfMouseWithinTextView();
		}

		/// <inheritdoc/>
		protected override void OnKeyUp(KeyEventArgs e)
		{
			base.OnKeyUp(e);
			TextView.InvalidateCursorIfMouseWithinTextView();
		}
		#endregion

		/// <inheritdoc/>
		protected override AutomationPeer OnCreateAutomationPeer()
		{
			return new TextAreaAutomationPeer(this);
		}

		/// <inheritdoc/>
		protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
		{
			// accept clicks even where the text area draws no background
			return new PointHitTestResult(this, hitTestParameters.HitPoint);
		}

		/// <inheritdoc/>
		protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			base.OnPropertyChanged(e);
			if (e.Property == SelectionBrushProperty
				|| e.Property == SelectionBorderProperty
				|| e.Property == SelectionForegroundProperty
				|| e.Property == SelectionCornerRadiusProperty) {
				textView.Redraw();
			} else if (e.Property == OverstrikeModeProperty) {
				caret.UpdateIfVisible();
			}
		}
	}
}
