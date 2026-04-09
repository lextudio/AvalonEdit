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
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit
{
	/// <summary>
	/// The text editor control.
	/// Contains a scrollable TextArea.
	/// </summary>
	public partial class TextEditor : ITextEditorComponent, IServiceProvider
	{
		#region Constructors
		static TextEditor()
		{
			InitializeWpfDefaults();
		}

		static partial void InitializeWpfDefaults();

		// Platform-specific hooks for property-changed callbacks.
		partial void OnDocumentChangedCore(TextDocument oldValue, TextDocument newValue);
		partial void OnOptionsChangedCore(TextEditorOptions oldValue, TextEditorOptions newValue);
		partial void OnIsReadOnlyChangedCore(TextEditor editor, bool oldValue, bool newValue);
		partial void OnShowLineNumbersChangedCore(TextEditor editor, bool oldValue, bool newValue);
		partial void OnLineNumbersForegroundChangedCore(TextEditor editor, object newValue);

		/// <summary>
		/// Creates a new TextEditor instance.
		/// </summary>
		public TextEditor() : this(new TextArea())
		{
		}

		/// <summary>
		/// Creates a new TextEditor instance.
		/// </summary>
		protected TextEditor(TextArea textArea)
		{
			if (textArea == null)
				throw new ArgumentNullException("textArea");
			this.textArea = textArea;

			textArea.TextView.Services.AddService(typeof(TextEditor), this);

			SetCurrentValue(OptionsProperty, textArea.Options);
			SetCurrentValue(DocumentProperty, new TextDocument());
		}
		#endregion

		#region Document property
		// DocumentProperty is declared in TextEditor.wpf.cs (WPF: AddOwner from TextView)
		// or TextEditor.uno.cs (Uno: DependencyProperty.Register) for each platform.

		/// <summary>
		/// Gets/Sets the document displayed by the text editor.
		/// This is a dependency property.
		/// </summary>
		public TextDocument Document {
			get { return (TextDocument)GetValue(DocumentProperty); }
			set { SetValue(DocumentProperty, value); }
		}

		/// <summary>
		/// Occurs when the document property has changed.
		/// </summary>
		public event EventHandler DocumentChanged;

		/// <summary>
		/// Raises the <see cref="DocumentChanged"/> event.
		/// </summary>
		protected virtual void OnDocumentChanged(EventArgs e)
		{
			if (DocumentChanged != null) {
				DocumentChanged(this, e);
			}
		}

		static void OnDocumentChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
		{
			((TextEditor)dp).OnDocumentChanged((TextDocument)e.OldValue, (TextDocument)e.NewValue);
		}

		void OnDocumentChanged(TextDocument oldValue, TextDocument newValue)
		{
			OnDocumentChangedCore(oldValue, newValue);
			textArea.Document = newValue;
			OnDocumentChanged(EventArgs.Empty);
			OnTextChanged(EventArgs.Empty);
		}
		#endregion

		#region Options property
		// OptionsProperty is declared in TextEditor.wpf.cs (WPF: AddOwner from TextView)
		// or TextEditor.uno.cs (Uno: DependencyProperty.Register) for each platform.

		/// <summary>
		/// Gets/Sets the options currently used by the text editor.
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
			((TextEditor)dp).OnOptionsChanged((TextEditorOptions)e.OldValue, (TextEditorOptions)e.NewValue);
		}

		void OnOptionsChanged(TextEditorOptions oldValue, TextEditorOptions newValue)
		{
			OnOptionsChangedCore(oldValue, newValue);
			textArea.Options = newValue;
			OnOptionChanged(new PropertyChangedEventArgs(null));
		}
		#endregion

		#region Text property
		/// <summary>
		/// Gets/Sets the text of the current document.
		/// </summary>
		[DefaultValue("")]
		public string Text {
			get {
				TextDocument document = this.Document;
				return document != null ? document.Text : string.Empty;
			}
			set {
				TextDocument document = GetDocument();
				document.Text = value ?? string.Empty;
				// after replacing the full text, the caret is positioned at the end of the document
				// - reset it to the beginning.
				this.CaretOffset = 0;
				document.UndoStack.ClearAll();
			}
		}

		TextDocument GetDocument()
		{
			TextDocument document = this.Document;
			if (document == null)
				throw ThrowUtil.NoDocumentAssigned();
			return document;
		}

		/// <summary>
		/// Occurs when the Text property changes.
		/// </summary>
		public event EventHandler TextChanged;

		/// <summary>
		/// Raises the <see cref="TextChanged"/> event.
		/// </summary>
		protected virtual void OnTextChanged(EventArgs e)
		{
			if (TextChanged != null) {
				TextChanged(this, e);
			}
		}
		#endregion

		#region TextArea property
		readonly TextArea textArea;

		/// <summary>
		/// Gets the text area.
		/// </summary>
		public TextArea TextArea {
			get { return textArea; }
		}

		object IServiceProvider.GetService(Type serviceType)
		{
			return textArea.GetService(serviceType);
		}
		#endregion

		#region Syntax highlighting
		/// <summary>
		/// The <see cref="SyntaxHighlighting"/> property.
		/// </summary>
		public static readonly DependencyProperty SyntaxHighlightingProperty =
			DependencyProperty.Register("SyntaxHighlighting", typeof(IHighlightingDefinition), typeof(TextEditor),
										new FrameworkPropertyMetadata(OnSyntaxHighlightingChanged));

		/// <summary>
		/// Gets/sets the syntax highlighting definition used to colorize the text.
		/// </summary>
		public IHighlightingDefinition SyntaxHighlighting {
			get { return (IHighlightingDefinition)GetValue(SyntaxHighlightingProperty); }
			set { SetValue(SyntaxHighlightingProperty, value); }
		}

		IVisualLineTransformer colorizer;

		static void OnSyntaxHighlightingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			((TextEditor)d).OnSyntaxHighlightingChanged(e.NewValue as IHighlightingDefinition);
		}

		void OnSyntaxHighlightingChanged(IHighlightingDefinition newValue)
		{
			if (colorizer != null) {
				textArea.TextView.LineTransformers.Remove(colorizer);
				colorizer = null;
			}
			if (newValue != null) {
				colorizer = CreateColorizer(newValue);
				if (colorizer != null)
					textArea.TextView.LineTransformers.Insert(0, colorizer);
			}
		}

		/// <summary>
		/// Creates the highlighting colorizer for the specified highlighting definition.
		/// Allows derived classes to provide custom colorizer implementations for special highlighting definitions.
		/// </summary>
		protected virtual IVisualLineTransformer CreateColorizer(IHighlightingDefinition highlightingDefinition)
		{
			if (highlightingDefinition == null)
				throw new ArgumentNullException("highlightingDefinition");
			return new HighlightingColorizer(highlightingDefinition);
		}
		#endregion

		#region WordWrap
		/// <summary>
		/// Word wrap dependency property.
		/// </summary>
		public static readonly DependencyProperty WordWrapProperty =
			DependencyProperty.Register("WordWrap", typeof(bool), typeof(TextEditor),
										new FrameworkPropertyMetadata(Boxes.False));

		/// <summary>
		/// Specifies whether the text editor uses word wrapping.
		/// </summary>
		/// <remarks>
		/// Setting WordWrap=true has the same effect as setting HorizontalScrollBarVisibility=Disabled and will override the
		/// HorizontalScrollBarVisibility setting.
		/// </remarks>
		public bool WordWrap {
			get { return (bool)GetValue(WordWrapProperty); }
			set { SetValue(WordWrapProperty, Boxes.Box(value)); }
		}
		#endregion

		#region IsReadOnly
		/// <summary>
		/// IsReadOnly dependency property.
		/// </summary>
		public static readonly DependencyProperty IsReadOnlyProperty =
			DependencyProperty.Register("IsReadOnly", typeof(bool), typeof(TextEditor),
										new FrameworkPropertyMetadata(Boxes.False, OnIsReadOnlyChanged));

		/// <summary>
		/// Specifies whether the user can change the text editor content.
		/// Setting this property will replace the
		/// <see cref="Editing.TextArea.ReadOnlySectionProvider">TextArea.ReadOnlySectionProvider</see>.
		/// </summary>
		public bool IsReadOnly {
			get { return (bool)GetValue(IsReadOnlyProperty); }
			set { SetValue(IsReadOnlyProperty, Boxes.Box(value)); }
		}

		static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			TextEditor editor = d as TextEditor;
			if (editor != null) {
				if ((bool)e.NewValue)
					editor.TextArea.ReadOnlySectionProvider = ReadOnlySectionDocument.Instance;
				else
					editor.TextArea.ReadOnlySectionProvider = NoReadOnlySections.Instance;
				editor.OnIsReadOnlyChangedCore(editor, (bool)e.OldValue, (bool)e.NewValue);
			}
		}
		#endregion

		#region IsModified
		/// <summary>
		/// Dependency property for <see cref="IsModified"/>
		/// </summary>
		public static readonly DependencyProperty IsModifiedProperty =
			DependencyProperty.Register("IsModified", typeof(bool), typeof(TextEditor),
										new FrameworkPropertyMetadata(Boxes.False, OnIsModifiedChanged));

		/// <summary>
		/// Gets/Sets the 'modified' flag.
		/// </summary>
		public bool IsModified {
			get { return (bool)GetValue(IsModifiedProperty); }
			set { SetValue(IsModifiedProperty, Boxes.Box(value)); }
		}

		static void OnIsModifiedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			TextEditor editor = d as TextEditor;
			if (editor != null) {
				TextDocument document = editor.Document;
				if (document != null) {
					UndoStack undoStack = document.UndoStack;
					if ((bool)e.NewValue) {
						if (undoStack.IsOriginalFile)
							undoStack.DiscardOriginalFileMarker();
					} else {
						undoStack.MarkAsOriginalFile();
					}
				}
			}
		}

		bool HandleIsOriginalChanged(PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "IsOriginalFile") {
				TextDocument document = this.Document;
				if (document != null) {
					SetCurrentValue(IsModifiedProperty, Boxes.Box(!document.UndoStack.IsOriginalFile));
				}
				return true;
			} else {
				return false;
			}
		}
		#endregion

		#region ShowLineNumbers
		/// <summary>
		/// ShowLineNumbers dependency property.
		/// </summary>
		public static readonly DependencyProperty ShowLineNumbersProperty =
			DependencyProperty.Register("ShowLineNumbers", typeof(bool), typeof(TextEditor),
										new FrameworkPropertyMetadata(Boxes.False, OnShowLineNumbersChanged));

		/// <summary>
		/// Specifies whether line numbers are shown on the left to the text view.
		/// </summary>
		public bool ShowLineNumbers {
			get { return (bool)GetValue(ShowLineNumbersProperty); }
			set { SetValue(ShowLineNumbersProperty, Boxes.Box(value)); }
		}

		static void OnShowLineNumbersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			TextEditor editor = (TextEditor)d;
			editor.OnShowLineNumbersChangedCore(editor, (bool)e.OldValue, (bool)e.NewValue);
		}
		#endregion

		#region LineNumbersForeground
		// LineNumbersForegroundProperty is declared in TextEditor.wpf.cs (default=Brushes.Gray, WPF-specific)
		// or TextEditor.uno.cs for the Uno platform.

		/// <summary>
		/// Gets/sets the Brush used for displaying the foreground color of line numbers.
		/// </summary>
		public Brush LineNumbersForeground {
			get { return (Brush)GetValue(LineNumbersForegroundProperty); }
			set { SetValue(LineNumbersForegroundProperty, value); }
		}

		static void OnLineNumbersForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			((TextEditor)d).OnLineNumbersForegroundChangedCore((TextEditor)d, e.NewValue);
		}
		#endregion

		#region TextBoxBase-like methods
		/// <summary>
		/// Appends text to the end of the document.
		/// </summary>
		public void AppendText(string textData)
		{
			var document = GetDocument();
			document.Insert(document.TextLength, textData);
		}

		/// <summary>
		/// Begins a group of document changes.
		/// </summary>
		public void BeginChange()
		{
			GetDocument().BeginUpdate();
		}

		/// <summary>
		/// Begins a group of document changes and returns an object that ends the group of document
		/// changes when it is disposed.
		/// </summary>
		public IDisposable DeclareChangeBlock()
		{
			return GetDocument().RunUpdate();
		}

		/// <summary>
		/// Ends the current group of document changes.
		/// </summary>
		public void EndChange()
		{
			GetDocument().EndUpdate();
		}

		/// <summary>
		/// Clears the text.
		/// </summary>
		public void Clear()
		{
			this.Text = string.Empty;
		}
		#endregion

		#region TextBox methods
		/// <summary>
		/// Gets/Sets the selected text.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string SelectedText {
			get {
				// We'll get the text from the whole surrounding segment.
				// This is done to ensure that SelectedText.Length == SelectionLength.
				if (textArea.Document != null && !textArea.Selection.IsEmpty)
					return textArea.Document.GetText(textArea.Selection.SurroundingSegment);
				else
					return string.Empty;
			}
			set {
				if (value == null)
					throw new ArgumentNullException("value");
				if (textArea.Document != null) {
					int offset = this.SelectionStart;
					int length = this.SelectionLength;
					textArea.Document.Replace(offset, length, value);
					// keep inserted text selected
					textArea.Selection = SimpleSelection.Create(textArea, offset, offset + value.Length);
				}
			}
		}

		/// <summary>
		/// Gets/sets the caret position.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int CaretOffset {
			get { return textArea.Caret.Offset; }
			set { textArea.Caret.Offset = value; }
		}

		/// <summary>
		/// Gets/sets the start position of the selection.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int SelectionStart {
			get {
				if (textArea.Selection.IsEmpty)
					return textArea.Caret.Offset;
				else
					return textArea.Selection.SurroundingSegment.Offset;
			}
			set { Select(value, SelectionLength); }
		}

		/// <summary>
		/// Gets/sets the length of the selection.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int SelectionLength {
			get {
				if (!textArea.Selection.IsEmpty)
					return textArea.Selection.SurroundingSegment.Length;
				else
					return 0;
			}
			set { Select(SelectionStart, value); }
		}

		/// <summary>
		/// Selects the specified text section.
		/// </summary>
		public void Select(int start, int length)
		{
			int documentLength = Document != null ? Document.TextLength : 0;
			if (start < 0 || start > documentLength)
				throw new ArgumentOutOfRangeException("start", start, "Value must be between 0 and " + documentLength);
			if (length < 0 || start + length > documentLength)
				throw new ArgumentOutOfRangeException("length", length, "Value must be between 0 and " + (documentLength - start));
			textArea.Selection = SimpleSelection.Create(textArea, start, start + length);
			textArea.Caret.Offset = start + length;
		}

		/// <summary>
		/// Gets the number of lines in the document.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int LineCount {
			get {
				TextDocument document = this.Document;
				if (document != null)
					return document.LineCount;
				else
					return 1;
			}
		}
		#endregion

		#region Loading from stream
		/// <summary>
		/// Loads the text from the stream, auto-detecting the encoding.
		/// </summary>
		/// <remarks>
		/// This method sets <see cref="IsModified"/> to false.
		/// </remarks>
		public void Load(Stream stream)
		{
			using (StreamReader reader = FileReader.OpenStream(stream, this.Encoding ?? Encoding.UTF8)) {
				this.Text = reader.ReadToEnd();
				SetCurrentValue(EncodingProperty, reader.CurrentEncoding); // assign encoding after ReadToEnd() so that the StreamReader can autodetect the encoding
			}
			SetCurrentValue(IsModifiedProperty, Boxes.False);
		}

		/// <summary>
		/// Loads the text from the stream, auto-detecting the encoding.
		/// </summary>
		public void Load(string fileName)
		{
			if (fileName == null)
				throw new ArgumentNullException("fileName");
			using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				Load(fs);
			}
		}

		/// <summary>
		/// Encoding dependency property.
		/// </summary>
		public static readonly DependencyProperty EncodingProperty =
			DependencyProperty.Register("Encoding", typeof(Encoding), typeof(TextEditor));

		/// <summary>
		/// Gets/sets the encoding used when the file is saved.
		/// </summary>
		/// <remarks>
		/// The <see cref="Load(Stream)"/> method autodetects the encoding of the file and sets this property accordingly.
		/// The <see cref="Save(Stream)"/> method uses the encoding specified in this property.
		/// </remarks>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Encoding Encoding {
			get { return (Encoding)GetValue(EncodingProperty); }
			set { SetValue(EncodingProperty, value); }
		}

		/// <summary>
		/// Saves the text to the stream.
		/// </summary>
		/// <remarks>
		/// This method sets <see cref="IsModified"/> to false.
		/// </remarks>
		public void Save(Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException("stream");
			var encoding = this.Encoding;
			var document = this.Document;
			StreamWriter writer = encoding != null ? new StreamWriter(stream, encoding) : new StreamWriter(stream);
			if (document != null)
				document.WriteTextTo(writer);
			writer.Flush();
			// do not close the stream
			SetCurrentValue(IsModifiedProperty, Boxes.False);
		}

		/// <summary>
		/// Saves the text to the file.
		/// </summary>
		public void Save(string fileName)
		{
			if (fileName == null)
				throw new ArgumentNullException("fileName");
			using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None)) {
				Save(fs);
			}
		}
		#endregion
	}
}
