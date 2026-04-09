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
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit.Editing
{
	public sealed partial class Caret
	{
		CaretLayer caretAdorner;

		partial void Initialize()
		{
			caretAdorner = new CaretLayer(textArea);
			textView.InsertLayer(caretAdorner, KnownLayer.Caret, LayerInsertionPosition.Replace);
		}

		partial void InvalidateCaretVisual()
		{
			if (caretAdorner != null) {
				caretAdorner.InvalidateVisual();
			}
		}

		partial void ShowCaretAsync()
		{
			// Clear showScheduled now so Show()'s fallback guard does not fire synchronously.
			// The dispatcher callback will call ShowInternal() when it runs.
			showScheduled = false;
			textArea.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ShowInternal));
		}

		partial void ShowCaretInternal(Rect caretRect)
		{
			if (caretAdorner != null) {
				if (!hasWin32Caret) {
					hasWin32Caret = Win32.CreateCaret(textView, caretRect.Size);
				}
				if (hasWin32Caret) {
					Win32.SetCaretPosition(textView, caretRect.Location - textView.ScrollOffset);
				}
				caretAdorner.Show(caretRect);
				textArea.ime.UpdateCompositionWindow();
			}
		}

		partial void HideCaretInternal()
		{
			if (caretAdorner != null) {
				caretAdorner.Hide();
			}
		}

		partial void DestroyWin32Caret()
		{
			if (hasWin32Caret) {
				Win32.DestroyCaret();
				hasWin32Caret = false;
			}
		}

		private partial Rect CalcCaretRectangle(VisualLine visualLine)
		{
			if (!visualColumnValid) {
				RevalidateVisualColumn(visualLine);
			}
			TextLine textLine = visualLine.GetTextLine(position.VisualColumn, position.IsAtEndOfLine);
			double xPos = visualLine.GetTextLineVisualXPosition(textLine, position.VisualColumn);
			double lineTop = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextTop);
			double lineBottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextBottom);
			return new Rect(xPos, lineTop, GetCaretWidth(), lineBottom - lineTop);
		}

		partial void GetCaretWidthCore(ref double width)
		{
			width = SystemParameters.CaretWidth;
		}

		private partial Rect CalcCaretOverstrikeRectangle(VisualLine visualLine)
		{
			if (!visualColumnValid) {
				RevalidateVisualColumn(visualLine);
			}
			int currentPos = position.VisualColumn;
			int nextPos = visualLine.GetNextCaretPosition(currentPos, LogicalDirection.Forward, CaretPositioningMode.Normal, true);
			TextLine textLine = visualLine.GetTextLine(currentPos);
			Rect r;
			if (currentPos < visualLine.VisualLength) {
				var textBounds = textLine.GetTextBounds(currentPos, nextPos - currentPos)[0];
				r = textBounds.Rectangle;
				r.Y += visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.LineTop);
			} else {
				double xPos = visualLine.GetTextLineVisualXPosition(textLine, currentPos);
				double xPos2 = visualLine.GetTextLineVisualXPosition(textLine, nextPos);
				double lineTop = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextTop);
				double lineBottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextBottom);
				r = new Rect(xPos, lineTop, xPos2 - xPos, lineBottom - lineTop);
			}
			double caretWidth = GetCaretWidth();
			if (r.Width < caretWidth)
				r.Width = caretWidth;
			return r;
		}

		/// <summary>
		/// Gets/Sets the color of the caret.
		/// </summary>
		public Brush CaretBrush {
			get { return caretAdorner.CaretBrush; }
			set { caretAdorner.CaretBrush = value; }
		}
	}
}
