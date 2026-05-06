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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit.Folding
{
	public sealed partial class FoldingElementGenerator
	{
		partial void AddFoldingManagerToTextView(FoldingManager manager, TextView textView)
		{
			manager.AddToTextView(textView);
		}

		partial void RemoveFoldingManagerFromTextView(FoldingManager manager, TextView textView)
		{
			manager.RemoveFromTextView(textView);
		}

		partial void ValidateTextView(ITextRunConstructionContext context)
		{
			if (!foldingManager.textViews.Contains(context.TextView))
				throw new ArgumentException("Invalid TextView");
		}

		private partial VisualLineElement CreateFoldingElement(FoldingSection foldingSection, string title, int documentLength)
		{
			var p = new VisualLineElementTextRunProperties(CurrentContext.GlobalTextRunProperties);
			p.SetForegroundBrush(textBrush);
			var textFormatter = TextFormatterFactory.Create(CurrentContext.TextView);
			var text = FormattedTextElement.PrepareText(textFormatter, title, p);
			return new FoldingLineElement(foldingSection, text, documentLength) { textBrush = textBrush };
		}

		sealed class FoldingLineElement : FormattedTextElement
		{
			readonly FoldingSection fs;

			internal Brush textBrush;

			public FoldingLineElement(FoldingSection fs, TextLine text, int documentLength) : base(text, documentLength)
			{
				this.fs = fs;
			}

			public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
			{
				return new FoldingLineTextRun(this, this.TextRunProperties) { textBrush = textBrush };
			}

			protected internal override void OnMouseDown(MouseButtonEventArgs e)
			{
				if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left) {
					fs.IsFolded = false;
					e.Handled = true;
				} else {
					base.OnMouseDown(e);
				}
			}
		}

		sealed class FoldingLineTextRun : FormattedTextRun
		{
			internal Brush textBrush;

			public FoldingLineTextRun(FormattedTextElement element, TextRunProperties properties)
				: base(element, properties)
			{
			}

			public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
			{
				var metrics = Format(double.PositiveInfinity);
				Rect r = new Rect(origin.X, origin.Y - metrics.Baseline, metrics.Width, metrics.Height);
				drawingContext.DrawRectangle(null, new Pen(textBrush, 1), r);
				base.Draw(drawingContext, origin, rightToLeft, sideways);
			}
		}
	}
}
