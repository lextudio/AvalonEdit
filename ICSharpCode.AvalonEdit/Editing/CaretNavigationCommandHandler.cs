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
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit.Editing
{
	enum CaretMovementType
	{
		None,
		CharLeft,
		CharRight,
		Backspace,
		WordLeft,
		WordRight,
		LineUp,
		LineDown,
		PageUp,
		PageDown,
		LineStart,
		LineEnd,
		DocumentStart,
		DocumentEnd
	}

	static partial class CaretNavigationCommandHandler
	{
		#region Caret movement
		/// <summary>Moves the caret in the given direction, accounting for FlowDirection.</summary>
		internal static void MoveCaret(TextArea textArea, CaretMovementType direction)
		{
			double desiredXPos = textArea.Caret.DesiredXPos;

			if (textArea.FlowDirection == FlowDirection.RightToLeft) {
				if (direction == CaretMovementType.CharLeft) {
					direction = CaretMovementType.CharRight;
				} else if (direction == CaretMovementType.CharRight) {
					direction = CaretMovementType.CharLeft;
				} else if (direction == CaretMovementType.WordRight) {
					direction = CaretMovementType.WordLeft;
				} else if (direction == CaretMovementType.WordLeft) {
					direction = CaretMovementType.WordRight;
				}
			}

			textArea.Caret.Position = GetNewCaretPosition(textArea.TextView, textArea.Caret.Position, direction, textArea.Selection.EnableVirtualSpace, ref desiredXPos);
			textArea.Caret.DesiredXPos = desiredXPos;
		}
		#endregion

		#region By-character / By-word movement (platform-independent)
		static TextViewPosition GetNextCaretPosition(TextView textView, TextViewPosition caretPosition, VisualLine visualLine, CaretPositioningMode mode, bool enableVirtualSpace)
		{
			int pos = visualLine.GetNextCaretPosition(caretPosition.VisualColumn, LogicalDirection.Forward, mode, enableVirtualSpace);
			if (pos >= 0) {
				return visualLine.GetTextViewPosition(pos);
			} else {
				// move to start of next line
				DocumentLine nextDocumentLine = visualLine.LastDocumentLine.NextLine;
				if (nextDocumentLine != null) {
					VisualLine nextLine = textView.GetOrConstructVisualLine(nextDocumentLine);
					pos = nextLine.GetNextCaretPosition(-1, LogicalDirection.Forward, mode, enableVirtualSpace);
					if (pos < 0)
						throw ThrowUtil.NoValidCaretPosition();
					return nextLine.GetTextViewPosition(pos);
				} else {
					// at end of document
					Debug.Assert(visualLine.LastDocumentLine.Offset + visualLine.LastDocumentLine.TotalLength == textView.Document.TextLength);
					return new TextViewPosition(textView.Document.GetLocation(textView.Document.TextLength));
				}
			}
		}

		static TextViewPosition GetPrevCaretPosition(TextView textView, TextViewPosition caretPosition, VisualLine visualLine, CaretPositioningMode mode, bool enableVirtualSpace)
		{
			int pos = visualLine.GetNextCaretPosition(caretPosition.VisualColumn, LogicalDirection.Backward, mode, enableVirtualSpace);
			if (pos >= 0) {
				return visualLine.GetTextViewPosition(pos);
			} else {
				// move to end of previous line
				DocumentLine previousDocumentLine = visualLine.FirstDocumentLine.PreviousLine;
				if (previousDocumentLine != null) {
					VisualLine previousLine = textView.GetOrConstructVisualLine(previousDocumentLine);
					pos = previousLine.GetNextCaretPosition(previousLine.VisualLength + 1, LogicalDirection.Backward, mode, enableVirtualSpace);
					if (pos < 0)
						throw ThrowUtil.NoValidCaretPosition();
					return previousLine.GetTextViewPosition(pos);
				} else {
					// at start of document
					Debug.Assert(visualLine.FirstDocumentLine.Offset == 0);
					return new TextViewPosition(0, 0);
				}
			}
		}
		#endregion
	}
}
