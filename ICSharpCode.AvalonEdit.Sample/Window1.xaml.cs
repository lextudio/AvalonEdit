// Copyright (c) 2009 Daniel Grunwald
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;

using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Search;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Win32;

namespace ICSharpCode.AvalonEdit.Sample
{
	/// <summary>
	/// Interaction logic for Window1.xaml
	/// </summary>
	public partial class Window1 : Window
	{
		readonly Dictionary<string, int> inputEventCounts = new Dictionary<string, int>();
		Point lastTextAreaMousePosition;
		MouseButtonState lastTextAreaLeftButton;
		string lastInputOriginalSource;

		public Window1()
		{
			// Load our custom highlighting definition
			IHighlightingDefinition customHighlighting;
			using (Stream s = typeof(Window1).Assembly.GetManifestResourceStream("ICSharpCode.AvalonEdit.Sample.CustomHighlighting.xshd")) {
				if (s == null)
					throw new InvalidOperationException("Could not find embedded resource");
				using (XmlReader reader = new XmlTextReader(s)) {
					customHighlighting = ICSharpCode.AvalonEdit.Highlighting.Xshd.
						HighlightingLoader.Load(reader, HighlightingManager.Instance);
				}
			}
			// and register it in the HighlightingManager
			HighlightingManager.Instance.RegisterHighlighting("Custom Highlighting", new string[] { ".cool" }, customHighlighting);
			
			
			InitializeComponent();

			this.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Display);

			//textEditor.TextArea.SelectionBorder = null;
			
			//textEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
			//textEditor.SyntaxHighlighting = customHighlighting;
			// initial highlighting now set by XAML
			
			textEditor.TextArea.TextEntering += textEditor_TextArea_TextEntering;
			textEditor.TextArea.TextEntered += textEditor_TextArea_TextEntered;
			textEditor.TextArea.PreviewKeyDown += textEditor_TextArea_KeyTrace;
			textEditor.TextArea.KeyDown += textEditor_TextArea_KeyTrace;
			textEditor.TextArea.TextInput += textEditor_TextArea_TextInputTrace;
			textEditor.TextArea.PreviewMouseDown += textEditor_TextArea_InputTrace;
			textEditor.TextArea.PreviewMouseMove += textEditor_TextArea_InputTrace;
			textEditor.TextArea.PreviewMouseUp += textEditor_TextArea_InputTrace;
			SearchPanel.Install(textEditor);
			
			DispatcherTimer foldingUpdateTimer = new DispatcherTimer();
			foldingUpdateTimer.Interval = TimeSpan.FromSeconds(2);
			foldingUpdateTimer.Tick += delegate { UpdateFoldings(); };
			foldingUpdateTimer.Start();
		}

		void textEditor_TextArea_InputTrace(object sender, MouseEventArgs e)
		{
			string name = e.RoutedEvent != null ? e.RoutedEvent.Name : "<null>";
			if (!inputEventCounts.ContainsKey(name))
				inputEventCounts[name] = 0;
			inputEventCounts[name]++;
			lastTextAreaMousePosition = e.GetPosition(textEditor.TextArea);
			lastTextAreaLeftButton = e.LeftButton;
			lastInputOriginalSource = e.OriginalSource != null ? e.OriginalSource.GetType().FullName : null;
		}

		[DevFlowAction("avedit.activate", Description = "Activate and foreground the AvalonEdit sample window")]
		public string ActivateSampleWindow()
		{
			if (WindowState == WindowState.Minimized)
				WindowState = WindowState.Normal;
			Activate();
			Topmost = true;
			Topmost = false;
			Focus();
			textEditor.Focus();
			textEditor.TextArea.Focus();
			return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
			{
				["isActive"] = IsActive,
				["isKeyboardFocusWithin"] = IsKeyboardFocusWithin,
				["textAreaFocused"] = textEditor.TextArea.IsKeyboardFocused,
			});
		}

		[DevFlowAction("avedit.reset", Description = "Reset text editor content and diagnostics")]
		public string ResetEditor()
		{
			inputEventCounts.Clear();
			lastTextAreaMousePosition = default(Point);
			lastTextAreaLeftButton = Mouse.LeftButton;
			lastInputOriginalSource = null;
			// Line 1 is kept exactly "alpha beta gamma" (17 chars) for tests that assert on it.
			// The rest pads the document with many real text lines so mouse-driven selection
			// repro scenarios always land on actual words/lines instead of empty space below
			// a too-short document (AvalonEdit doesn't have visual lines past the last line).
			var sb = new System.Text.StringBuilder();
			sb.Append("alpha beta gamma\n");
			sb.Append("second line\n");
			sb.Append("third line\n");
			for (int i = 4; i <= 60; i++) {
				sb.Append("line ").Append(i).Append(" has some words in it for testing purposes\n");
			}
			textEditor.Text = sb.ToString().TrimEnd('\n');
			textEditor.TextArea.ClearSelection();
			textEditor.TextArea.Caret.Offset = 0;
			textEditor.Focus();
			textEditor.TextArea.Focus();
			return QueryEditorState();
		}

		[DevFlowAction("avedit.query.state", Description = "Query AvalonEdit text, caret, selection, and mouse diagnostics")]
		public string QueryEditorState()
		{
			ISegment selection = textEditor.TextArea.Selection != null ? textEditor.TextArea.Selection.SurroundingSegment : null;
			var result = new Dictionary<string, object>
			{
				["text"] = textEditor.Text,
				["textLength"] = textEditor.Document != null ? textEditor.Document.TextLength : 0,
				["caretOffset"] = textEditor.TextArea.Caret.Offset,
				["selectionIsEmpty"] = textEditor.TextArea.Selection == null || textEditor.TextArea.Selection.IsEmpty,
				["selectionOffset"] = selection != null ? selection.Offset : 0,
				["selectionLength"] = selection != null ? selection.Length : 0,
				["mouseSelectionMode"] = textEditor.TextArea.MouseSelectionMode.ToString(),
				["mouseLeftButton"] = Mouse.LeftButton.ToString(),
				["lastTextAreaLeftButton"] = lastTextAreaLeftButton.ToString(),
				["lastMouseX"] = lastTextAreaMousePosition.X,
				["lastMouseY"] = lastTextAreaMousePosition.Y,
				["captured"] = Mouse.Captured != null ? Mouse.Captured.GetType().FullName : null,
				["directlyOver"] = Mouse.DirectlyOver != null ? Mouse.DirectlyOver.GetType().FullName : null,
				["originalSource"] = lastInputOriginalSource,
				["counts"] = inputEventCounts.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value),
			};
			return System.Text.Json.JsonSerializer.Serialize(result);
		}

		[DevFlowAction("avedit.query.bounds", Description = "Query screen bounds for AvalonEdit sample elements")]
		public string QueryEditorBounds(string target)
		{
			FrameworkElement element = target == "text-area"
				? (FrameworkElement)textEditor.TextArea
				: target == "text-editor"
					? (FrameworkElement)textEditor
					: null;
			return System.Text.Json.JsonSerializer.Serialize(CreateBoundsPayload(target, element));
		}

		[DevFlowAction("avedit.insert-newline", Description = "Insert a newline through AvalonEdit text input")]
		public string InsertNewLine()
		{
			textEditor.TextArea.PerformTextInput("\n");
			return QueryEditorState();
		}

		[DevFlowAction("avedit.cancel-selection", Description = "Cancel AvalonEdit mouse selection mode")]
		public string CancelSelection()
		{
			textEditor.TextArea.MouseSelectionMode = MouseSelectionMode.None;
			textEditor.TextArea.ClearSelection();
			return QueryEditorState();
		}

		static Dictionary<string, object> CreateBoundsPayload(string target, FrameworkElement element)
		{
			if (element == null || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
				return new Dictionary<string, object> { ["target"] = target, ["found"] = false };

			Point topLeft = element.PointToScreen(new Point(0, 0));
			return new Dictionary<string, object>
			{
				["target"] = target,
				["found"] = true,
				["x"] = topLeft.X,
				["y"] = topLeft.Y,
				["width"] = element.ActualWidth,
				["height"] = element.ActualHeight,
			};
		}

		string currentFileName;
		
		void openFileClick(object sender, RoutedEventArgs e)
		{
			OpenFileDialog dlg = new OpenFileDialog();
			dlg.CheckFileExists = true;
			if (dlg.ShowDialog() ?? false) {
				currentFileName = dlg.FileName;
				textEditor.Load(currentFileName);
				textEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(currentFileName));
			}
		}
		
		void saveFileClick(object sender, EventArgs e)
		{
			if (currentFileName == null) {
				SaveFileDialog dlg = new SaveFileDialog();
				dlg.DefaultExt = ".txt";
				if (dlg.ShowDialog() ?? false) {
					currentFileName = dlg.FileName;
				} else {
					return;
				}
			}
			textEditor.Save(currentFileName);
		}
		
		CompletionWindow completionWindow;

		static readonly object inputTraceLock = new object();
		static bool inputTraceInitialized;
		static readonly string inputTracePath = "/tmp/opendevelop-avalonedit-key.log";

		void WriteInputTrace(string line)
		{
			try {
				lock (inputTraceLock) {
					if (!inputTraceInitialized) {
						inputTraceInitialized = true;
						File.AppendAllText(inputTracePath, Environment.NewLine + "=== AvalonEdit sample input trace pid=" + Process.GetCurrentProcess().Id + " ===" + Environment.NewLine);
					}
					File.AppendAllText(inputTracePath, DateTime.UtcNow.ToString("O") + " " + line + Environment.NewLine);
				}
			} catch {
				// Temporary diagnostics must not affect editor input.
			}
		}

		void textEditor_TextArea_KeyTrace(object sender, KeyEventArgs e)
		{
			WriteInputTrace(string.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"tid={0} apt={1} routed={2} key={3} systemKey={4} imeKey={5} deadChar={6} handled={7} modifiers={8} focused={9} caret={10} textLength={11}",
				Thread.CurrentThread.ManagedThreadId,
				Thread.CurrentThread.GetApartmentState(),
				e.RoutedEvent != null ? e.RoutedEvent.Name : "<null>",
				e.Key,
				e.SystemKey,
				e.ImeProcessedKey,
				e.DeadCharProcessedKey,
				e.Handled,
				Keyboard.Modifiers,
				textEditor.TextArea.IsKeyboardFocused,
				textEditor.TextArea.Caret.Offset,
				textEditor.Document != null ? textEditor.Document.TextLength : -1));
		}

		void textEditor_TextArea_TextInputTrace(object sender, TextCompositionEventArgs e)
		{
			WriteInputTrace(string.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"tid={0} apt={1} routed={2} text={3} handled={4} caret={5} textLength={6}",
				Thread.CurrentThread.ManagedThreadId,
				Thread.CurrentThread.GetApartmentState(),
				e.RoutedEvent != null ? e.RoutedEvent.Name : "<null>",
				FormatTraceText(e.Text),
				e.Handled,
				textEditor.TextArea.Caret.Offset,
				textEditor.Document != null ? textEditor.Document.TextLength : -1));
		}

		static string FormatTraceText(string text)
		{
			if (text == null)
				return "<null>";
			return text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
		}
		
		void textEditor_TextArea_TextEntered(object sender, TextCompositionEventArgs e)
		{
			if (e.Text == ".") {
				// open code completion after the user has pressed dot:
				completionWindow = new CompletionWindow(textEditor.TextArea);
				// provide AvalonEdit with the data:
				IList<ICompletionData> data = completionWindow.CompletionList.CompletionData;
				data.Add(new MyCompletionData("Item1"));
				data.Add(new MyCompletionData("Item2"));
				data.Add(new MyCompletionData("Item3"));
				data.Add(new MyCompletionData("Another item"));
				completionWindow.Show();
				completionWindow.Closed += delegate {
					completionWindow = null;
				};
			}
		}
		
		void textEditor_TextArea_TextEntering(object sender, TextCompositionEventArgs e)
		{
			if (e.Text.Length > 0 && completionWindow != null) {
				if (!char.IsLetterOrDigit(e.Text[0])) {
					// Whenever a non-letter is typed while the completion window is open,
					// insert the currently selected element.
					completionWindow.CompletionList.RequestInsertion(e);
				}
			}
			// do not set e.Handled=true - we still want to insert the character that was typed
		}
		
		#region Folding
		FoldingManager foldingManager;
		object foldingStrategy;
		
		void HighlightingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (textEditor.SyntaxHighlighting == null) {
				foldingStrategy = null;
			} else {
				switch (textEditor.SyntaxHighlighting.Name) {
					case "XML":
						foldingStrategy = new XmlFoldingStrategy();
						textEditor.TextArea.IndentationStrategy = new ICSharpCode.AvalonEdit.Indentation.DefaultIndentationStrategy();
						break;
					case "C#":
					case "C++":
					case "PHP":
					case "Java":
						textEditor.TextArea.IndentationStrategy = new ICSharpCode.AvalonEdit.Indentation.CSharp.CSharpIndentationStrategy(textEditor.Options);
						foldingStrategy = new BraceFoldingStrategy();
						break;
					default:
						textEditor.TextArea.IndentationStrategy = new ICSharpCode.AvalonEdit.Indentation.DefaultIndentationStrategy();
						foldingStrategy = null;
						break;
				}
			}
			if (foldingStrategy != null) {
				if (foldingManager == null)
					foldingManager = FoldingManager.Install(textEditor.TextArea);
				UpdateFoldings();
			} else {
				if (foldingManager != null) {
					FoldingManager.Uninstall(foldingManager);
					foldingManager = null;
				}
			}
		}
		
		void UpdateFoldings()
		{
			if (foldingStrategy is BraceFoldingStrategy) {
				((BraceFoldingStrategy)foldingStrategy).UpdateFoldings(foldingManager, textEditor.Document);
			}
			if (foldingStrategy is XmlFoldingStrategy) {
				((XmlFoldingStrategy)foldingStrategy).UpdateFoldings(foldingManager, textEditor.Document);
			}
		}
		#endregion
	}
}
