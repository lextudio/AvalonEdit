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
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit
{
	[Localizability(LocalizationCategory.Text)]
	[ContentProperty("Text")]
	public partial class TextEditor : Control, IWeakEventListener
	{
		static partial void InitializeWpfDefaults()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(TextEditor),
													 new FrameworkPropertyMetadata(typeof(TextEditor)));
			FocusableProperty.OverrideMetadata(typeof(TextEditor),
											   new FrameworkPropertyMetadata(Boxes.True));
		}

		/// <inheritdoc/>
		protected override AutomationPeer OnCreateAutomationPeer()
		{
			return new TextEditorAutomationPeer(this);
		}

		/// Forward focus to TextArea.
		/// <inheritdoc/>
		protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
		{
			base.OnGotKeyboardFocus(e);
			if (e.NewFocus == this) {
				Keyboard.Focus(textArea);
				e.Handled = true;
			}
		}

		#region Document property
		/// <summary>
		/// Document property.
		/// </summary>
		public static readonly DependencyProperty DocumentProperty
			= TextView.DocumentProperty.AddOwner(
				typeof(TextEditor), new FrameworkPropertyMetadata(OnDocumentChanged));
		#endregion

		#region Options property
		/// <summary>
		/// Options property.
		/// </summary>
		public static readonly DependencyProperty OptionsProperty
			= TextView.OptionsProperty.AddOwner(typeof(TextEditor), new FrameworkPropertyMetadata(OnOptionsChanged));

		partial void OnDocumentChangedCore(TextDocument oldValue, TextDocument newValue)
		{
			if (oldValue != null) {
				TextDocumentWeakEventManager.TextChanged.RemoveListener(oldValue, this);
				PropertyChangedEventManager.RemoveListener(oldValue.UndoStack, this, "IsOriginalFile");
			}
			if (newValue != null) {
				TextDocumentWeakEventManager.TextChanged.AddListener(newValue, this);
				PropertyChangedEventManager.AddListener(newValue.UndoStack, this, "IsOriginalFile");
			}
		}

		partial void OnOptionsChangedCore(TextEditorOptions oldValue, TextEditorOptions newValue)
		{
			if (oldValue != null) {
				PropertyChangedWeakEventManager.RemoveListener(oldValue, this);
			}
			if (newValue != null) {
				PropertyChangedWeakEventManager.AddListener(newValue, this);
			}
		}

		/// <inheritdoc cref="IWeakEventListener.ReceiveWeakEvent"/>
		protected virtual bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
		{
			if (managerType == typeof(PropertyChangedWeakEventManager)) {
				OnOptionChanged((System.ComponentModel.PropertyChangedEventArgs)e);
				return true;
			} else if (managerType == typeof(TextDocumentWeakEventManager.TextChanged)) {
				OnTextChanged(e);
				return true;
			} else if (managerType == typeof(PropertyChangedEventManager)) {
				return HandleIsOriginalChanged((System.ComponentModel.PropertyChangedEventArgs)e);
			}
			return false;
		}

		bool IWeakEventListener.ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
		{
			return ReceiveWeakEvent(managerType, sender, e);
		}
		#endregion

		#region TextArea / ScrollViewer
		ScrollViewer scrollViewer;

		/// <summary>
		/// Is called after the template was applied.
		/// </summary>
		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			scrollViewer = (ScrollViewer)Template.FindName("PART_ScrollViewer", this);
		}

		/// <summary>
		/// Gets the scroll viewer used by the text editor.
		/// This property can return null if the template has not been applied / does not contain a scroll viewer.
		/// </summary>
		internal ScrollViewer ScrollViewer {
			get { return scrollViewer; }
		}

		bool CanExecute(RoutedUICommand command)
		{
			return command.CanExecute(null, textArea);
		}

		void Execute(RoutedUICommand command)
		{
			command.Execute(null, textArea);
		}
		#endregion

		#region IsReadOnly automation peer
		partial void OnIsReadOnlyChangedCore(TextEditor editor, bool oldValue, bool newValue)
		{
			TextEditorAutomationPeer peer = TextEditorAutomationPeer.FromElement(editor) as TextEditorAutomationPeer;
			if (peer != null) {
				peer.RaiseIsReadOnlyChanged(oldValue, newValue);
			}
		}
		#endregion

		#region ShowLineNumbers WPF implementation
		partial void OnShowLineNumbersChangedCore(TextEditor editor, bool oldValue, bool newValue)
		{
			var leftMargins = editor.TextArea.LeftMargins;
			if (newValue) {
				LineNumberMargin lineNumbers = new LineNumberMargin();
				Line line = (Line)DottedLineMargin.Create();
				leftMargins.Insert(0, lineNumbers);
				leftMargins.Insert(1, line);
				var lineNumbersForeground = new Binding("LineNumbersForeground") { Source = editor };
				line.SetBinding(Line.StrokeProperty, lineNumbersForeground);
				lineNumbers.SetBinding(Control.ForegroundProperty, lineNumbersForeground);
			} else {
				for (int i = 0; i < leftMargins.Count; i++) {
					if (leftMargins[i] is LineNumberMargin) {
						leftMargins.RemoveAt(i);
						if (i < leftMargins.Count && DottedLineMargin.IsDottedLineMargin(leftMargins[i])) {
							leftMargins.RemoveAt(i);
						}
						break;
					}
				}
			}
		}
		#endregion

		#region LineNumbersForeground WPF implementation
		/// <summary>
		/// LineNumbersForeground dependency property.
		/// </summary>
		public static readonly DependencyProperty LineNumbersForegroundProperty =
			DependencyProperty.Register("LineNumbersForeground", typeof(Brush), typeof(TextEditor),
										new FrameworkPropertyMetadata(Brushes.Gray, OnLineNumbersForegroundChanged));

		partial void OnLineNumbersForegroundChangedCore(TextEditor editor, object newValue)
		{
			var lineNumberMargin = editor.TextArea.LeftMargins.FirstOrDefault(margin => margin is LineNumberMargin) as LineNumberMargin;
			if (lineNumberMargin != null) {
				lineNumberMargin.SetValue(Control.ForegroundProperty, newValue);
			}
		}
		#endregion

		#region TextBoxBase-like methods (WPF command-based)
		/// <summary>
		/// Copies the current selection to the clipboard.
		/// </summary>
		public void Copy()
		{
			Execute(ApplicationCommands.Copy);
		}

		/// <summary>
		/// Removes the current selection and copies it to the clipboard.
		/// </summary>
		public void Cut()
		{
			Execute(ApplicationCommands.Cut);
		}

		/// <summary>
		/// Removes the current selection without copying it to the clipboard.
		/// </summary>
		public void Delete()
		{
			Execute(ApplicationCommands.Delete);
		}

		/// <summary>
		/// Scrolls one line down.
		/// </summary>
		public void LineDown()
		{
			if (scrollViewer != null)
				scrollViewer.LineDown();
		}

		/// <summary>
		/// Scrolls to the left.
		/// </summary>
		public void LineLeft()
		{
			if (scrollViewer != null)
				scrollViewer.LineLeft();
		}

		/// <summary>
		/// Scrolls to the right.
		/// </summary>
		public void LineRight()
		{
			if (scrollViewer != null)
				scrollViewer.LineRight();
		}

		/// <summary>
		/// Scrolls one line up.
		/// </summary>
		public void LineUp()
		{
			if (scrollViewer != null)
				scrollViewer.LineUp();
		}

		/// <summary>
		/// Scrolls one page down.
		/// </summary>
		public void PageDown()
		{
			if (scrollViewer != null)
				scrollViewer.PageDown();
		}

		/// <summary>
		/// Scrolls one page up.
		/// </summary>
		public void PageUp()
		{
			if (scrollViewer != null)
				scrollViewer.PageUp();
		}

		/// <summary>
		/// Scrolls one page left.
		/// </summary>
		public void PageLeft()
		{
			if (scrollViewer != null)
				scrollViewer.PageLeft();
		}

		/// <summary>
		/// Scrolls one page right.
		/// </summary>
		public void PageRight()
		{
			if (scrollViewer != null)
				scrollViewer.PageRight();
		}

		/// <summary>
		/// Pastes the clipboard content.
		/// </summary>
		public void Paste()
		{
			Execute(ApplicationCommands.Paste);
		}

		/// <summary>
		/// Redoes the most recent undone command.
		/// </summary>
		/// <returns>True is the redo operation was successful, false is the redo stack is empty.</returns>
		public bool Redo()
		{
			if (CanExecute(ApplicationCommands.Redo)) {
				Execute(ApplicationCommands.Redo);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Scrolls to the end of the document.
		/// </summary>
		public void ScrollToEnd()
		{
			ApplyTemplate(); // ensure scrollViewer is created
			if (scrollViewer != null)
				scrollViewer.ScrollToEnd();
		}

		/// <summary>
		/// Scrolls to the start of the document.
		/// </summary>
		public void ScrollToHome()
		{
			ApplyTemplate(); // ensure scrollViewer is created
			if (scrollViewer != null)
				scrollViewer.ScrollToHome();
		}

		/// <summary>
		/// Scrolls to the specified position in the document.
		/// </summary>
		public void ScrollToHorizontalOffset(double offset)
		{
			ApplyTemplate(); // ensure scrollViewer is created
			if (scrollViewer != null)
				scrollViewer.ScrollToHorizontalOffset(offset);
		}

		/// <summary>
		/// Scrolls to the specified position in the document.
		/// </summary>
		public void ScrollToVerticalOffset(double offset)
		{
			ApplyTemplate(); // ensure scrollViewer is created
			if (scrollViewer != null)
				scrollViewer.ScrollToVerticalOffset(offset);
		}

		/// <summary>
		/// Selects the entire text.
		/// </summary>
		public void SelectAll()
		{
			Execute(ApplicationCommands.SelectAll);
		}

		/// <summary>
		/// Undoes the most recent command.
		/// </summary>
		/// <returns>True is the undo operation was successful, false is the undo stack is empty.</returns>
		public bool Undo()
		{
			if (CanExecute(ApplicationCommands.Undo)) {
				Execute(ApplicationCommands.Undo);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Gets if the most recent undone command can be redone.
		/// </summary>
		public bool CanRedo {
			get { return CanExecute(ApplicationCommands.Redo); }
		}

		/// <summary>
		/// Gets if the most recent command can be undone.
		/// </summary>
		public bool CanUndo {
			get { return CanExecute(ApplicationCommands.Undo); }
		}

		/// <summary>
		/// Gets the vertical size of the document.
		/// </summary>
		public double ExtentHeight {
			get { return scrollViewer != null ? scrollViewer.ExtentHeight : 0; }
		}

		/// <summary>
		/// Gets the horizontal size of the current document region.
		/// </summary>
		public double ExtentWidth {
			get { return scrollViewer != null ? scrollViewer.ExtentWidth : 0; }
		}

		/// <summary>
		/// Gets the horizontal size of the viewport.
		/// </summary>
		public double ViewportHeight {
			get { return scrollViewer != null ? scrollViewer.ViewportHeight : 0; }
		}

		/// <summary>
		/// Gets the horizontal size of the viewport.
		/// </summary>
		public double ViewportWidth {
			get { return scrollViewer != null ? scrollViewer.ViewportWidth : 0; }
		}

		/// <summary>
		/// Gets the vertical scroll position.
		/// </summary>
		public double VerticalOffset {
			get { return scrollViewer != null ? scrollViewer.VerticalOffset : 0; }
		}

		/// <summary>
		/// Gets the horizontal scroll position.
		/// </summary>
		public double HorizontalOffset {
			get { return scrollViewer != null ? scrollViewer.HorizontalOffset : 0; }
		}
		#endregion

		#region MouseHover events
		/// <summary>
		/// The PreviewMouseHover event.
		/// </summary>
		public static readonly RoutedEvent PreviewMouseHoverEvent =
			TextView.PreviewMouseHoverEvent.AddOwner(typeof(TextEditor));

		/// <summary>
		/// The MouseHover event.
		/// </summary>
		public static readonly RoutedEvent MouseHoverEvent =
			TextView.MouseHoverEvent.AddOwner(typeof(TextEditor));

		/// <summary>
		/// The PreviewMouseHoverStopped event.
		/// </summary>
		public static readonly RoutedEvent PreviewMouseHoverStoppedEvent =
			TextView.PreviewMouseHoverStoppedEvent.AddOwner(typeof(TextEditor));

		/// <summary>
		/// The MouseHoverStopped event.
		/// </summary>
		public static readonly RoutedEvent MouseHoverStoppedEvent =
			TextView.MouseHoverStoppedEvent.AddOwner(typeof(TextEditor));

		/// <summary>
		/// Occurs when the mouse has hovered over a fixed location for some time.
		/// </summary>
		public event MouseEventHandler PreviewMouseHover {
			add { AddHandler(PreviewMouseHoverEvent, value); }
			remove { RemoveHandler(PreviewMouseHoverEvent, value); }
		}

		/// <summary>
		/// Occurs when the mouse has hovered over a fixed location for some time.
		/// </summary>
		public event MouseEventHandler MouseHover {
			add { AddHandler(MouseHoverEvent, value); }
			remove { RemoveHandler(MouseHoverEvent, value); }
		}

		/// <summary>
		/// Occurs when the mouse had previously hovered but now started moving again.
		/// </summary>
		public event MouseEventHandler PreviewMouseHoverStopped {
			add { AddHandler(PreviewMouseHoverStoppedEvent, value); }
			remove { RemoveHandler(PreviewMouseHoverStoppedEvent, value); }
		}

		/// <summary>
		/// Occurs when the mouse had previously hovered but now started moving again.
		/// </summary>
		public event MouseEventHandler MouseHoverStopped {
			add { AddHandler(MouseHoverStoppedEvent, value); }
			remove { RemoveHandler(MouseHoverStoppedEvent, value); }
		}
		#endregion

		#region ScrollBarVisibility
		/// <summary>
		/// Dependency property for <see cref="HorizontalScrollBarVisibility"/>
		/// </summary>
		public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty = ScrollViewer.HorizontalScrollBarVisibilityProperty.AddOwner(typeof(TextEditor), new FrameworkPropertyMetadata(ScrollBarVisibility.Visible));

		/// <summary>
		/// Gets/Sets the horizontal scroll bar visibility.
		/// </summary>
		public ScrollBarVisibility HorizontalScrollBarVisibility {
			get { return (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty); }
			set { SetValue(HorizontalScrollBarVisibilityProperty, value); }
		}

		/// <summary>
		/// Dependency property for <see cref="VerticalScrollBarVisibility"/>
		/// </summary>
		public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = ScrollViewer.VerticalScrollBarVisibilityProperty.AddOwner(typeof(TextEditor), new FrameworkPropertyMetadata(ScrollBarVisibility.Visible));

		/// <summary>
		/// Gets/Sets the vertical scroll bar visibility.
		/// </summary>
		public ScrollBarVisibility VerticalScrollBarVisibility {
			get { return (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty); }
			set { SetValue(VerticalScrollBarVisibilityProperty, value); }
		}
		#endregion

		/// <summary>
		/// Gets the text view position from a point inside the editor.
		/// </summary>
		/// <param name="point">The position, relative to top left
		/// corner of TextEditor control</param>
		/// <returns>The text view position, or null if the point is outside the document.</returns>
		public TextViewPosition? GetPositionFromPoint(Point point)
		{
			if (this.Document == null)
				return null;
			TextView textView = this.TextArea.TextView;
			return textView.GetPosition(TranslatePoint(point, textView) + textView.ScrollOffset);
		}

		/// <summary>
		/// Scrolls to the specified line.
		/// This method requires that the TextEditor was already assigned a size (WPF layout must have run prior).
		/// </summary>
		public void ScrollToLine(int line)
		{
			ScrollTo(line, -1);
		}

		/// <summary>
		/// Scrolls to the specified line/column.
		/// This method requires that the TextEditor was already assigned a size (WPF layout must have run prior).
		/// </summary>
		public void ScrollTo(int line, int column)
		{
			const double MinimumScrollFraction = 0.3;
			ScrollTo(line, column, VisualYPosition.LineMiddle, null != scrollViewer ? scrollViewer.ViewportHeight / 2 : 0.0, MinimumScrollFraction);
		}

		/// <summary>
		/// Scrolls to the specified line/column.
		/// This method requires that the TextEditor was already assigned a size (WPF layout must have run prior).
		/// </summary>
		/// <param name="line">Line to scroll to.</param>
		/// <param name="column">Column to scroll to (important if wrapping is 'on', and for the horizontal scroll position).</param>
		/// <param name="yPositionMode">The mode how to reference the Y position of the line.</param>
		/// <param name="referencedVerticalViewPortOffset">Offset from the top of the viewport to where the referenced line/column should be positioned.</param>
		/// <param name="minimumScrollFraction">The minimum vertical and/or horizontal scroll offset, expressed as fraction of the height or width of the viewport window, respectively.</param>
		public void ScrollTo(int line, int column, VisualYPosition yPositionMode, double referencedVerticalViewPortOffset, double minimumScrollFraction)
		{
			TextView textView = textArea.TextView;
			TextDocument document = textView.Document;
			if (scrollViewer != null && document != null) {
				if (line < 1)
					line = 1;
				if (line > document.LineCount)
					line = document.LineCount;

				IScrollInfo scrollInfo = textView;
				if (!scrollInfo.CanHorizontallyScroll) {
					// Word wrap is enabled. Ensure that we have up-to-date info about line height so that we scroll
					// to the correct position.
					// This avoids that the user has to repeat the ScrollTo() call several times when there are very long lines.
					VisualLine vl = textView.GetOrConstructVisualLine(document.GetLineByNumber(line));
					double remainingHeight = referencedVerticalViewPortOffset;

					while (remainingHeight > 0) {
						DocumentLine prevLine = vl.FirstDocumentLine.PreviousLine;
						if (prevLine == null)
							break;
						vl = textView.GetOrConstructVisualLine(prevLine);
						remainingHeight -= vl.Height;
					}
				}

				Point p = textArea.TextView.GetVisualPosition(new TextViewPosition(line, Math.Max(1, column)), yPositionMode);
				double verticalPos = p.Y - referencedVerticalViewPortOffset;
				if (Math.Abs(verticalPos - scrollViewer.VerticalOffset) > minimumScrollFraction * scrollViewer.ViewportHeight) {
					scrollViewer.ScrollToVerticalOffset(Math.Max(0, verticalPos));
				}
				if (column > 0) {
					if (p.X > scrollViewer.ViewportWidth - Caret.MinimumDistanceToViewBorder * 2) {
						double horizontalPos = Math.Max(0, p.X - scrollViewer.ViewportWidth / 2);
						if (Math.Abs(horizontalPos - scrollViewer.HorizontalOffset) > minimumScrollFraction * scrollViewer.ViewportWidth) {
							scrollViewer.ScrollToHorizontalOffset(horizontalPos);
						}
					} else {
						scrollViewer.ScrollToHorizontalOffset(0);
					}
				}
			}
		}
	}
}
