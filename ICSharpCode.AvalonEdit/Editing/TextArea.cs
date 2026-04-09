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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit.Editing
{
	/// <summary>
	/// Control that wraps a TextView and adds support for user input and the caret.
	/// </summary>
	public partial class TextArea : Control, ITextEditorComponent, IServiceProvider
	{
		#region Partial void declarations
		static partial void InitializeWpfDefaults();
		partial void InitializeIme();
		partial void AttachTypingEvents();
		partial void RequestSelectionValidationAsync();
		partial void InvalidateRequery();
		partial void SubscribeToDocumentEvents(TextDocument document);
		partial void UnsubscribeFromDocumentEvents(TextDocument document);
		partial void SubscribeToOptionsEvents(TextEditorOptions options);
		partial void UnsubscribeFromOptionsEvents(TextEditorOptions options);
		#endregion

		#region Constructor
		static TextArea()
		{
			InitializeWpfDefaults();
		}

		/// <summary>
		/// Creates a new TextArea instance.
		/// </summary>
		public TextArea() : this(new TextView())
		{
		}

		/// <summary>
		/// Creates a new TextArea instance.
		/// </summary>
		protected TextArea(TextView textView)
		{
			if (textView == null)
				throw new ArgumentNullException("textView");
			this.textView = textView;
			this.Options = textView.Options;

			selection = emptySelection = new EmptySelection(this);

			textView.Services.AddService(typeof(TextArea), this);

			textView.LineTransformers.Add(new SelectionColorizer(this));
			textView.InsertLayer(new SelectionLayer(this), KnownLayer.Selection, LayerInsertionPosition.Replace);

			caret = new Caret(this);
			caret.PositionChanged += (sender, e) => RequestSelectionValidation();
			caret.PositionChanged += CaretPositionChanged;
			AttachTypingEvents();
			InitializeIme();

			leftMargins.CollectionChanged += leftMargins_CollectionChanged;

			this.DefaultInputHandler = new TextAreaDefaultInputHandler(this);
			this.ActiveInputHandler = this.DefaultInputHandler;
		}
		#endregion

		#region InputHandler management
		/// <summary>
		/// Gets the default input handler.
		/// </summary>
		/// <remarks><inheritdoc cref="ITextAreaInputHandler"/></remarks>
		public TextAreaDefaultInputHandler DefaultInputHandler { get; private set; }

		ITextAreaInputHandler activeInputHandler;
		bool isChangingInputHandler;

		/// <summary>
		/// Gets/Sets the active input handler.
		/// This property does not return currently active stacked input handlers. Setting this property detached all stacked input handlers.
		/// </summary>
		/// <remarks><inheritdoc cref="ITextAreaInputHandler"/></remarks>
		public ITextAreaInputHandler ActiveInputHandler {
			get { return activeInputHandler; }
			set {
				if (value != null && value.TextArea != this)
					throw new ArgumentException("The input handler was created for a different text area than this one.");
				if (isChangingInputHandler)
					throw new InvalidOperationException("Cannot set ActiveInputHandler recursively");
				if (activeInputHandler != value) {
					isChangingInputHandler = true;
					try {
						// pop the whole stack
						PopStackedInputHandler(stackedInputHandlers.LastOrDefault());
						Debug.Assert(stackedInputHandlers.IsEmpty);

						if (activeInputHandler != null)
							activeInputHandler.Detach();
						activeInputHandler = value;
						if (value != null)
							value.Attach();
					} finally {
						isChangingInputHandler = false;
					}
					if (ActiveInputHandlerChanged != null)
						ActiveInputHandlerChanged(this, EventArgs.Empty);
				}
			}
		}

		/// <summary>
		/// Occurs when the ActiveInputHandler property changes.
		/// </summary>
		public event EventHandler ActiveInputHandlerChanged;

		ImmutableStack<TextAreaStackedInputHandler> stackedInputHandlers = ImmutableStack<TextAreaStackedInputHandler>.Empty;

		/// <summary>
		/// Gets the list of currently active stacked input handlers.
		/// </summary>
		/// <remarks><inheritdoc cref="ITextAreaInputHandler"/></remarks>
		public ImmutableStack<TextAreaStackedInputHandler> StackedInputHandlers {
			get { return stackedInputHandlers; }
		}

		/// <summary>
		/// Pushes an input handler onto the list of stacked input handlers.
		/// </summary>
		/// <remarks><inheritdoc cref="ITextAreaInputHandler"/></remarks>
		public void PushStackedInputHandler(TextAreaStackedInputHandler inputHandler)
		{
			if (inputHandler == null)
				throw new ArgumentNullException("inputHandler");
			stackedInputHandlers = stackedInputHandlers.Push(inputHandler);
			inputHandler.Attach();
		}

		/// <summary>
		/// Pops the stacked input handler (and all input handlers above it).
		/// If <paramref name="inputHandler"/> is not found in the currently stacked input handlers, or is null, this method
		/// does nothing.
		/// </summary>
		/// <remarks><inheritdoc cref="ITextAreaInputHandler"/></remarks>
		public void PopStackedInputHandler(TextAreaStackedInputHandler inputHandler)
		{
			if (stackedInputHandlers.Any(i => i == inputHandler)) {
				ITextAreaInputHandler oldHandler;
				do {
					oldHandler = stackedInputHandlers.Peek();
					stackedInputHandlers = stackedInputHandlers.Pop();
					oldHandler.Detach();
				} while (oldHandler != inputHandler);
			}
		}
		#endregion

		#region Document property
		/// <summary>
		/// Document property.
		/// </summary>
		public static readonly DependencyProperty DocumentProperty
			= TextView.DocumentProperty.AddOwner(typeof(TextArea), new FrameworkPropertyMetadata(OnDocumentChanged));

		/// <summary>
		/// Gets/Sets the document displayed by the text editor.
		/// </summary>
		public TextDocument Document {
			get { return (TextDocument)GetValue(DocumentProperty); }
			set { SetValue(DocumentProperty, value); }
		}

		/// <inheritdoc/>
		public event EventHandler DocumentChanged;

		static void OnDocumentChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
		{
			((TextArea)dp).OnDocumentChanged((TextDocument)e.OldValue, (TextDocument)e.NewValue);
		}

		void OnDocumentChanged(TextDocument oldValue, TextDocument newValue)
		{
			UnsubscribeFromDocumentEvents(oldValue);
			textView.Document = newValue;
			SubscribeToDocumentEvents(newValue);
			// Reset caret location and selection: this is necessary because the caret/selection might be invalid
			// in the new document (e.g. if new document is shorter than the old document).
			caret.Location = new TextLocation(1, 1);
			this.ClearSelection();
			if (DocumentChanged != null)
				DocumentChanged(this, EventArgs.Empty);
			InvalidateRequery();
		}
		#endregion

		#region Options property
		/// <summary>
		/// Options property.
		/// </summary>
		public static readonly DependencyProperty OptionsProperty
			= TextView.OptionsProperty.AddOwner(typeof(TextArea), new FrameworkPropertyMetadata(OnOptionsChanged));

		/// <summary>
		/// Gets/Sets the document displayed by the text editor.
		/// </summary>
		public TextEditorOptions Options {
			get { return (TextEditorOptions)GetValue(OptionsProperty); }
			set { SetValue(OptionsProperty, value); }
		}

		/// <summary>
		/// Occurs when a text editor option has changed.
		/// </summary>
		public event PropertyChangedEventHandler OptionChanged;

		/// <summary>
		/// Raises the <see cref="OptionChanged"/> event.
		/// </summary>
		protected virtual void OnOptionChanged(PropertyChangedEventArgs e)
		{
			if (OptionChanged != null) {
				OptionChanged(this, e);
			}
		}

		static void OnOptionsChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
		{
			((TextArea)dp).OnOptionsChanged((TextEditorOptions)e.OldValue, (TextEditorOptions)e.NewValue);
		}

		void OnOptionsChanged(TextEditorOptions oldValue, TextEditorOptions newValue)
		{
			UnsubscribeFromOptionsEvents(oldValue);
			textView.Options = newValue;
			SubscribeToOptionsEvents(newValue);
			OnOptionChanged(new PropertyChangedEventArgs(null));
		}
		#endregion

		#region Caret handling on document changes
		void OnDocumentChanging()
		{
			caret.OnDocumentChanging();
		}

		void OnDocumentChanged(DocumentChangeEventArgs e)
		{
			caret.OnDocumentChanged(e);
			this.Selection = selection.UpdateOnDocumentChange(e);
		}

		void OnUpdateStarted()
		{
			Document.UndoStack.PushOptional(new RestoreCaretAndSelectionUndoAction(this));
		}

		void OnUpdateFinished()
		{
			caret.OnDocumentUpdateFinished();
		}

		sealed class RestoreCaretAndSelectionUndoAction : IUndoableOperation
		{
			// keep textarea in weak reference because the IUndoableOperation is stored with the document
			WeakReference textAreaReference;
			TextViewPosition caretPosition;
			Selection selection;

			public RestoreCaretAndSelectionUndoAction(TextArea textArea)
			{
				this.textAreaReference = new WeakReference(textArea);
				// Just save the old caret position, no need to validate here.
				// If we restore it, we'll validate it anyways.
				this.caretPosition = textArea.Caret.NonValidatedPosition;
				this.selection = textArea.Selection;
			}

			public void Undo()
			{
				TextArea textArea = (TextArea)textAreaReference.Target;
				if (textArea != null) {
					textArea.Caret.Position = caretPosition;
					textArea.Selection = selection;
				}
			}

			public void Redo()
			{
				// redo=undo: we just restore the caret/selection state
				Undo();
			}
		}
		#endregion

		#region TextView property
		readonly TextView textView;

		/// <summary>
		/// Gets the text view used to display text in this text area.
		/// </summary>
		public TextView TextView {
			get {
				return textView;
			}
		}
		#endregion

		#region Selection property
		internal readonly Selection emptySelection;
		Selection selection;

		/// <summary>
		/// Occurs when the selection has changed.
		/// </summary>
		public event EventHandler SelectionChanged;

		/// <summary>
		/// Gets/Sets the selection in this text area.
		/// </summary>
		public Selection Selection {
			get { return selection; }
			set {
				if (value == null)
					throw new ArgumentNullException("value");
				if (value.textArea != this)
					throw new ArgumentException("Cannot use a Selection instance that belongs to another text area.");
				if (!object.Equals(selection, value)) {
					//					Debug.WriteLine("Selection change from " + selection + " to " + value);
					if (textView != null) {
						ISegment oldSegment = selection.SurroundingSegment;
						ISegment newSegment = value.SurroundingSegment;
						if (!Selection.EnableVirtualSpace && (selection is SimpleSelection && value is SimpleSelection && oldSegment != null && newSegment != null)) {
							// perf optimization:
							// When a simple selection changes, don't redraw the whole selection, but only the changed parts.
							int oldSegmentOffset = oldSegment.Offset;
							int newSegmentOffset = newSegment.Offset;
							if (oldSegmentOffset != newSegmentOffset) {
								textView.Redraw(Math.Min(oldSegmentOffset, newSegmentOffset),
												Math.Abs(oldSegmentOffset - newSegmentOffset),
												DispatcherPriority.Render);
							}
							int oldSegmentEndOffset = oldSegment.EndOffset;
							int newSegmentEndOffset = newSegment.EndOffset;
							if (oldSegmentEndOffset != newSegmentEndOffset) {
								textView.Redraw(Math.Min(oldSegmentEndOffset, newSegmentEndOffset),
												Math.Abs(oldSegmentEndOffset - newSegmentEndOffset),
												DispatcherPriority.Render);
							}
						} else {
							textView.Redraw(oldSegment, DispatcherPriority.Render);
							textView.Redraw(newSegment, DispatcherPriority.Render);
						}
					}
					selection = value;
					if (SelectionChanged != null)
						SelectionChanged(this, EventArgs.Empty);
					// a selection change causes commands like copy/paste/etc. to change status
					InvalidateRequery();
				}
			}
		}

		/// <summary>
		/// Clears the current selection.
		/// </summary>
		public void ClearSelection()
		{
			this.Selection = emptySelection;
		}

		/// <summary>
		/// The <see cref="SelectionBrush"/> property.
		/// </summary>
		public static readonly DependencyProperty SelectionBrushProperty =
			DependencyProperty.Register("SelectionBrush", typeof(System.Windows.Media.Brush), typeof(TextArea));

		/// <summary>
		/// Gets/Sets the background brush used for the selection.
		/// </summary>
		public System.Windows.Media.Brush SelectionBrush {
			get { return (System.Windows.Media.Brush)GetValue(SelectionBrushProperty); }
			set { SetValue(SelectionBrushProperty, value); }
		}

		/// <summary>
		/// The <see cref="SelectionForeground"/> property.
		/// </summary>
		public static readonly DependencyProperty SelectionForegroundProperty =
			DependencyProperty.Register("SelectionForeground", typeof(System.Windows.Media.Brush), typeof(TextArea));

		/// <summary>
		/// Gets/Sets the foreground brush used for selected text.
		/// </summary>
		public System.Windows.Media.Brush SelectionForeground {
			get { return (System.Windows.Media.Brush)GetValue(SelectionForegroundProperty); }
			set { SetValue(SelectionForegroundProperty, value); }
		}

		/// <summary>
		/// The <see cref="SelectionBorder"/> property.
		/// </summary>
		public static readonly DependencyProperty SelectionBorderProperty =
			DependencyProperty.Register("SelectionBorder", typeof(System.Windows.Media.Pen), typeof(TextArea));

		/// <summary>
		/// Gets/Sets the pen used for the border of the selection.
		/// </summary>
		public System.Windows.Media.Pen SelectionBorder {
			get { return (System.Windows.Media.Pen)GetValue(SelectionBorderProperty); }
			set { SetValue(SelectionBorderProperty, value); }
		}

		/// <summary>
		/// The <see cref="SelectionCornerRadius"/> property.
		/// </summary>
		public static readonly DependencyProperty SelectionCornerRadiusProperty =
			DependencyProperty.Register("SelectionCornerRadius", typeof(double), typeof(TextArea),
										new FrameworkPropertyMetadata(3.0));

		/// <summary>
		/// Gets/Sets the corner radius of the selection.
		/// </summary>
		public double SelectionCornerRadius {
			get { return (double)GetValue(SelectionCornerRadiusProperty); }
			set { SetValue(SelectionCornerRadiusProperty, value); }
		}

		/// <summary>
		/// Gets/Sets the active mouse selection mode.
		///
		/// Setting this property to MouseSelectionMode.None will cancel mouse selection
		/// and release mouse capture.
		///
		/// Setting this property to another value will acquire mouse capture and
		/// activate the mouse selection mode.
		/// If mouse capture cannot be acquired, MouseSelectionMode will stay unchanged.
		///
		/// Currently, the setter only supports the values <c>None</c>, <c>Normal</c>
		/// and <c>Rectangular</c>.
		/// </summary>
		public MouseSelectionMode MouseSelectionMode {
			get {
				var mouseHandler = DefaultInputHandler.MouseSelection as SelectionMouseHandler;
				if (mouseHandler != null) {
					return mouseHandler.MouseSelectionMode;
				} else {
					return MouseSelectionMode.None;
				}
			}
			set {
				var mouseHandler = DefaultInputHandler.MouseSelection as SelectionMouseHandler;
				if (mouseHandler != null) {
					mouseHandler.MouseSelectionMode = value;
				}
			}
		}
		#endregion

		#region Force caret to stay inside selection
		bool ensureSelectionValidRequested;
		int allowCaretOutsideSelection;

		void RequestSelectionValidation()
		{
			if (!ensureSelectionValidRequested && allowCaretOutsideSelection == 0) {
				ensureSelectionValidRequested = true;
				RequestSelectionValidationAsync();
			}
		}

		/// <summary>
		/// Code that updates only the caret but not the selection can cause confusion when
		/// keys like 'Delete' delete the (possibly invisible) selected text and not the
		/// text around the caret.
		///
		/// So we'll ensure that the caret is inside the selection.
		/// (when the caret is not in the selection, we'll clear the selection)
		///
		/// This method is invoked using the Dispatcher so that code may temporarily violate this rule
		/// (e.g. most 'extend selection' methods work by first setting the caret, then the selection),
		/// it's sufficient to fix it after any event handlers have run.
		/// </summary>
		void EnsureSelectionValid()
		{
			ensureSelectionValidRequested = false;
			if (allowCaretOutsideSelection == 0) {
				if (!selection.IsEmpty && !selection.Contains(caret.Offset)) {
					Debug.WriteLine("Resetting selection because caret is outside");
					this.ClearSelection();
				}
			}
		}

		/// <summary>
		/// Temporarily allows positioning the caret outside the selection.
		/// Dispose the returned IDisposable to revert the allowance.
		/// </summary>
		/// <remarks>
		/// The text area only forces the caret to be inside the selection when other events
		/// have finished running (using the dispatcher), so you don't have to use this method
		/// for temporarily positioning the caret in event handlers.
		/// This method is only necessary if you want to run the WPF dispatcher, e.g. if you
		/// perform a drag'n'drop operation.
		/// </remarks>
		public IDisposable AllowCaretOutsideSelection()
		{
			VerifyAccess();
			allowCaretOutsideSelection++;
			return new CallbackOnDispose(
				delegate {
					VerifyAccess();
					allowCaretOutsideSelection--;
					RequestSelectionValidation();
				});
		}
		#endregion

		#region Properties
		readonly Caret caret;

		/// <summary>
		/// Gets the Caret used for this text area.
		/// </summary>
		public Caret Caret {
			get { return caret; }
		}

		void CaretPositionChanged(object sender, EventArgs e)
		{
			if (textView == null)
				return;

			this.textView.HighlightedLine = this.Caret.Line;
		}

		ObservableCollection<UIElement> leftMargins = new ObservableCollection<UIElement>();

		/// <summary>
		/// Gets the collection of margins displayed to the left of the text view.
		/// </summary>
		public ObservableCollection<UIElement> LeftMargins {
			get {
				return leftMargins;
			}
		}

		void leftMargins_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.OldItems != null) {
				foreach (ITextViewConnect c in e.OldItems.OfType<ITextViewConnect>()) {
					c.RemoveFromTextView(textView);
				}
			}
			if (e.NewItems != null) {
				foreach (ITextViewConnect c in e.NewItems.OfType<ITextViewConnect>()) {
					c.AddToTextView(textView);
				}
			}
		}

		IReadOnlySectionProvider readOnlySectionProvider = NoReadOnlySections.Instance;

		/// <summary>
		/// Gets/Sets an object that provides read-only sections for the text area.
		/// </summary>
		public IReadOnlySectionProvider ReadOnlySectionProvider {
			get { return readOnlySectionProvider; }
			set {
				if (value == null)
					throw new ArgumentNullException("value");
				readOnlySectionProvider = value;
				InvalidateRequery(); // the read-only status effects Paste.CanExecute and the IME
			}
		}
		#endregion

		#region OnTextInput / RemoveSelectedText / ReplaceSelectionWithText
		void ReplaceSelectionWithNewLine()
		{
			string newLine = TextUtilities.GetNewLineFromDocument(this.Document, this.Caret.Line);
			using (this.Document.RunUpdate()) {
				ReplaceSelectionWithText(newLine);
				if (this.IndentationStrategy != null) {
					DocumentLine line = this.Document.GetLineByNumber(this.Caret.Line);
					ISegment[] deletable = GetDeletableSegments(line);
					if (deletable.Length == 1 && deletable[0].Offset == line.Offset && deletable[0].Length == line.Length) {
						// use indentation strategy only if the line is not read-only
						this.IndentationStrategy.IndentLine(this.Document, line);
					}
				}
			}
		}

		internal void RemoveSelectedText()
		{
			if (this.Document == null)
				throw ThrowUtil.NoDocumentAssigned();
			selection.ReplaceSelectionWithText(string.Empty);
#if DEBUG
			if (!selection.IsEmpty) {
				foreach (ISegment s in selection.Segments) {
					Debug.Assert(this.ReadOnlySectionProvider.GetDeletableSegments(s).Count() == 0);
				}
			}
#endif
		}

		internal void ReplaceSelectionWithText(string newText)
		{
			if (newText == null)
				throw new ArgumentNullException("newText");
			if (this.Document == null)
				throw ThrowUtil.NoDocumentAssigned();
			selection.ReplaceSelectionWithText(newText);
		}

		internal ISegment[] GetDeletableSegments(ISegment segment)
		{
			var deletableSegments = this.ReadOnlySectionProvider.GetDeletableSegments(segment);
			if (deletableSegments == null)
				throw new InvalidOperationException("ReadOnlySectionProvider.GetDeletableSegments returned null");
			var array = deletableSegments.ToArray();
			int lastIndex = segment.Offset;
			for (int i = 0; i < array.Length; i++) {
				if (array[i].Offset < lastIndex)
					throw new InvalidOperationException("ReadOnlySectionProvider returned incorrect segments (outside of input segment / wrong order)");
				lastIndex = array[i].EndOffset;
			}
			if (lastIndex > segment.EndOffset)
				throw new InvalidOperationException("ReadOnlySectionProvider returned incorrect segments (outside of input segment / wrong order)");
			return array;
		}

		/// <summary>Core text insertion used by both the shared path and the WPF TextComposition path.</summary>
		internal void InsertText(string text)
		{
			if (text == "\n" || text == "\r" || text == "\r\n")
				ReplaceSelectionWithNewLine();
			else {
				if (OverstrikeMode && Selection.IsEmpty && Document.GetLineByNumber(Caret.Line).EndOffset > Caret.Offset)
					EditingCommands.SelectRightByCharacter.Execute(null, this);
				ReplaceSelectionWithText(text);
			}
		}
		#endregion

		#region IndentationStrategy property
		/// <summary>
		/// IndentationStrategy property.
		/// </summary>
		public static readonly DependencyProperty IndentationStrategyProperty =
			DependencyProperty.Register("IndentationStrategy", typeof(IIndentationStrategy), typeof(TextArea),
										new FrameworkPropertyMetadata(new DefaultIndentationStrategy()));

		/// <summary>
		/// Gets/Sets the indentation strategy used when inserting new lines.
		/// </summary>
		public IIndentationStrategy IndentationStrategy {
			get { return (IIndentationStrategy)GetValue(IndentationStrategyProperty); }
			set { SetValue(IndentationStrategyProperty, value); }
		}
		#endregion

		#region Overstrike mode

		/// <summary>
		/// The <see cref="OverstrikeMode"/> dependency property.
		/// </summary>
		public static readonly DependencyProperty OverstrikeModeProperty =
			DependencyProperty.Register("OverstrikeMode", typeof(bool), typeof(TextArea),
										new FrameworkPropertyMetadata(Boxes.False));

		/// <summary>
		/// Gets/Sets whether overstrike mode is active.
		/// </summary>
		public bool OverstrikeMode {
			get { return (bool)GetValue(OverstrikeModeProperty); }
			set { SetValue(OverstrikeModeProperty, value); }
		}

		#endregion

		/// <summary>
		/// Gets the requested service.
		/// </summary>
		/// <returns>Returns the requested service instance, or null if the service cannot be found.</returns>
		public virtual object GetService(Type serviceType)
		{
			return textView.GetService(serviceType);
		}

		/// <summary>
		/// Occurs when text inside the TextArea was copied.
		/// </summary>
		public event EventHandler<TextEventArgs> TextCopied;

		internal void OnTextCopied(TextEventArgs e)
		{
			if (TextCopied != null)
				TextCopied(this, e);
		}
	}

	/// <summary>
	/// EventArgs with text.
	/// </summary>
	[Serializable]
	public class TextEventArgs : EventArgs
	{
		string text;

		/// <summary>
		/// Gets the text.
		/// </summary>
		public string Text {
			get {
				return text;
			}
		}

		/// <summary>
		/// Creates a new TextEventArgs instance.
		/// </summary>
		public TextEventArgs(string text)
		{
			if (text == null)
				throw new ArgumentNullException("text");
			this.text = text;
		}
	}
}
