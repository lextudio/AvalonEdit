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
using System.Collections.Generic;

using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace ICSharpCode.AvalonEdit.Folding
{
	public partial class FoldingManager
	{
		internal readonly List<TextView> textViews = new List<TextView>();

		internal void AddToTextView(TextView textView)
		{
			if (textView == null || textViews.Contains(textView))
				throw new ArgumentException();
			textViews.Add(textView);
			foreach (FoldingSection fs in AllFoldings) {
				if (fs.collapsedSections != null) {
					Array.Resize(ref fs.collapsedSections, textViews.Count);
					fs.ValidateCollapsedLineSections();
				}
			}
		}

		internal void RemoveFromTextView(TextView textView)
		{
			int pos = textViews.IndexOf(textView);
			if (pos < 0)
				throw new ArgumentException();
			textViews.RemoveAt(pos);
			foreach (FoldingSection fs in AllFoldings) {
				if (fs.collapsedSections != null) {
					var c = new CollapsedLineSection[textViews.Count];
					Array.Copy(fs.collapsedSections, 0, c, 0, pos);
					fs.collapsedSections[pos].Uncollapse();
					Array.Copy(fs.collapsedSections, pos + 1, c, pos, c.Length - pos);
					fs.collapsedSections = c;
				}
			}
		}

		internal void Redraw()
		{
			foreach (TextView textView in textViews)
				textView.Redraw();
		}

		internal void Redraw(FoldingSection fs)
		{
			foreach (TextView textView in textViews)
				textView.Redraw(fs);
		}

		partial void OnFoldingsChanged(FoldingSection fs)
		{
			if (fs != null)
				Redraw(fs);
			else
				Redraw();
		}

		/// <summary>
		/// Adds Folding support to the specified text area.
		/// Warning: The folding manager is only valid for the text area's current document. The folding manager
		/// must be uninstalled before the text area is bound to a different document.
		/// </summary>
		/// <returns>The <see cref="FoldingManager"/> that manages the list of foldings inside the text area.</returns>
		public static FoldingManager Install(TextArea textArea)
		{
			if (textArea == null)
				throw new ArgumentNullException("textArea");
			return new FoldingManagerInstallation(textArea);
		}

		/// <summary>
		/// Uninstalls the folding manager.
		/// </summary>
		/// <exception cref="ArgumentException">The specified manager was not created using <see cref="Install"/>.</exception>
		public static void Uninstall(FoldingManager manager)
		{
			if (manager == null)
				throw new ArgumentNullException("manager");
			FoldingManagerInstallation installation = manager as FoldingManagerInstallation;
			if (installation != null) {
				installation.Uninstall();
			} else {
				throw new ArgumentException("FoldingManager was not created using FoldingManager.Install");
			}
		}

		sealed class FoldingManagerInstallation : FoldingManager
		{
			TextArea textArea;
			FoldingMargin margin;
			FoldingElementGenerator generator;

			public FoldingManagerInstallation(TextArea textArea) : base(textArea.Document)
			{
				this.textArea = textArea;
				margin = new FoldingMargin() { FoldingManager = this };
				generator = new FoldingElementGenerator() { FoldingManager = this };
				textArea.LeftMargins.Add(margin);
				textArea.TextView.Services.AddService(typeof(FoldingManager), this);
				// HACK: folding only works correctly when it has highest priority
				textArea.TextView.ElementGenerators.Insert(0, generator);
				textArea.Caret.PositionChanged += textArea_Caret_PositionChanged;
			}

			public void Uninstall()
			{
				Clear();
				if (textArea != null) {
					textArea.Caret.PositionChanged -= textArea_Caret_PositionChanged;
					textArea.LeftMargins.Remove(margin);
					textArea.TextView.ElementGenerators.Remove(generator);
					textArea.TextView.Services.RemoveService(typeof(FoldingManager));
					margin = null;
					generator = null;
					textArea = null;
				}
			}

			void textArea_Caret_PositionChanged(object sender, EventArgs e)
			{
				// Expand Foldings when Caret is moved into them.
				int caretOffset = textArea.Caret.Offset;
				foreach (FoldingSection s in GetFoldingsContaining(caretOffset)) {
					if (s.IsFolded && s.StartOffset < caretOffset && caretOffset < s.EndOffset) {
						s.IsFolded = false;
					}
				}
			}
		}
	}
}
