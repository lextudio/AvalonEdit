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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Documents;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit.Rendering
{
	/// <summary>
	/// Represents a visual line in the document.
	/// A visual line usually corresponds to one DocumentLine, but it can span multiple lines if
	/// all but the first are collapsed.
	/// </summary>
	public partial class VisualLine
	{
		internal enum LifetimePhase : byte
		{
			Generating,
			Transforming,
			Live,
			Disposed
		}

		internal TextView textView;
		internal List<VisualLineElement> elements;
		internal bool hasInlineObjects;
		internal LifetimePhase phase;

		/// <summary>
		/// Gets the document to which this VisualLine belongs.
		/// </summary>
		public TextDocument Document { get; private set; }

		/// <summary>
		/// Gets the first document line displayed by this visual line.
		/// </summary>
		public DocumentLine FirstDocumentLine { get; private set; }

		/// <summary>
		/// Gets the last document line displayed by this visual line.
		/// </summary>
		public DocumentLine LastDocumentLine { get; internal set; }

		/// <summary>
		/// Gets a read-only collection of line elements.
		/// </summary>
		public ReadOnlyCollection<VisualLineElement> Elements { get; internal set; }

		/// <summary>
		/// Gets the start offset of the VisualLine inside the document.
		/// This is equivalent to <c>FirstDocumentLine.Offset</c>.
		/// </summary>
		public int StartOffset {
			get {
				return FirstDocumentLine.Offset;
			}
		}

		/// <summary>
		/// Length in visual line coordinates.
		/// </summary>
		public int VisualLength { get; internal set; }

		/// <summary>
		/// Length in visual line coordinates including the end of line marker, if TextEditorOptions.ShowEndOfLine is enabled.
		/// </summary>
		public int VisualLengthWithEndOfLineMarker {
			get {
				int length = VisualLength;
				if (textView.Options.ShowEndOfLine && LastDocumentLine.NextLine != null) length++;
				return length;
			}
		}

		/// <summary>
		/// Gets the height of the visual line in device-independent pixels.
		/// </summary>
		public double Height { get; internal set; }

		/// <summary>
		/// Gets the Y position of the line. This is measured in device-independent pixels relative to the start of the document.
		/// </summary>
		public double VisualTop { get; internal set; }

		internal VisualLine(TextView textView, DocumentLine firstDocumentLine)
		{
			Debug.Assert(textView != null);
			Debug.Assert(firstDocumentLine != null);
			this.textView = textView;
			this.Document = textView.Document;
			this.FirstDocumentLine = firstDocumentLine;
		}

		internal void CalculateOffsets()
		{
			int visualOffset = 0;
			int textOffset = 0;
			foreach (VisualLineElement element in elements) {
				element.VisualColumn = visualOffset;
				element.RelativeTextOffset = textOffset;
				visualOffset += element.VisualLength;
				textOffset += element.DocumentLength;
			}
			VisualLength = visualOffset;
			Debug.Assert(textOffset == LastDocumentLine.EndOffset - FirstDocumentLine.Offset);
		}

		/// <summary>
		/// Replaces the single element at <paramref name="elementIndex"/> with the specified elements.
		/// The replacement operation must preserve the document length, but may change the visual length.
		/// </summary>
		/// <remarks>
		/// This method may only be called by line transformers.
		/// </remarks>
		public void ReplaceElement(int elementIndex, params VisualLineElement[] newElements)
		{
			ReplaceElement(elementIndex, 1, newElements);
		}

		/// <summary>
		/// Replaces <paramref name="count"/> elements starting at <paramref name="elementIndex"/> with the specified elements.
		/// The replacement operation must preserve the document length, but may change the visual length.
		/// </summary>
		/// <remarks>
		/// This method may only be called by line transformers.
		/// </remarks>
		public void ReplaceElement(int elementIndex, int count, params VisualLineElement[] newElements)
		{
			if (phase != LifetimePhase.Transforming)
				throw new InvalidOperationException("This method may only be called by line transformers.");
			int oldDocumentLength = 0;
			for (int i = elementIndex; i < elementIndex + count; i++) {
				oldDocumentLength += elements[i].DocumentLength;
			}
			int newDocumentLength = 0;
			foreach (var newElement in newElements) {
				newDocumentLength += newElement.DocumentLength;
			}
			if (oldDocumentLength != newDocumentLength)
				throw new InvalidOperationException("Old elements have document length " + oldDocumentLength + ", but new elements have length " + newDocumentLength);
			elements.RemoveRange(elementIndex, count);
			elements.InsertRange(elementIndex, newElements);
			CalculateOffsets();
		}

		/// <summary>
		/// Gets the visual column from a document offset relative to the first line start.
		/// </summary>
		public int GetVisualColumn(int relativeTextOffset)
		{
			ThrowUtil.CheckNotNegative(relativeTextOffset, "relativeTextOffset");
			foreach (VisualLineElement element in elements) {
				if (element.RelativeTextOffset <= relativeTextOffset
					&& element.RelativeTextOffset + element.DocumentLength >= relativeTextOffset) {
					return element.GetVisualColumn(relativeTextOffset);
				}
			}
			return VisualLength;
		}

		/// <summary>
		/// Gets the document offset (relative to the first line start) from a visual column.
		/// </summary>
		public int GetRelativeOffset(int visualColumn)
		{
			ThrowUtil.CheckNotNegative(visualColumn, "visualColumn");
			int documentLength = 0;
			foreach (VisualLineElement element in elements) {
				if (element.VisualColumn <= visualColumn
					&& element.VisualColumn + element.VisualLength > visualColumn) {
					return element.GetRelativeOffset(visualColumn);
				}
				documentLength += element.DocumentLength;
			}
			return documentLength;
		}

		/// <summary>
		/// Validates the visual column and returns the correct one.
		/// </summary>
		public int ValidateVisualColumn(TextViewPosition position, bool allowVirtualSpace)
		{
			return ValidateVisualColumn(Document.GetOffset(position.Location), position.VisualColumn, allowVirtualSpace);
		}

		/// <summary>
		/// Validates the visual column and returns the correct one.
		/// </summary>
		public int ValidateVisualColumn(int offset, int visualColumn, bool allowVirtualSpace)
		{
			int firstDocumentLineOffset = this.FirstDocumentLine.Offset;
			if (visualColumn < 0) {
				return GetVisualColumn(offset - firstDocumentLineOffset);
			} else {
				int offsetFromVisualColumn = GetRelativeOffset(visualColumn);
				offsetFromVisualColumn += firstDocumentLineOffset;
				if (offsetFromVisualColumn != offset) {
					return GetVisualColumn(offset - firstDocumentLineOffset);
				} else {
					if (visualColumn > VisualLength && !allowVirtualSpace) {
						return VisualLength;
					}
				}
			}
			return visualColumn;
		}

		/// <summary>
		/// Gets the text view position from the specified visual column.
		/// </summary>
		public TextViewPosition GetTextViewPosition(int visualColumn)
		{
			int documentOffset = GetRelativeOffset(visualColumn) + this.FirstDocumentLine.Offset;
			return new TextViewPosition(this.Document.GetLocation(documentOffset), visualColumn);
		}

		/// <summary>
		/// Gets whether the visual line was disposed.
		/// </summary>
		public bool IsDisposed {
			get { return phase == LifetimePhase.Disposed; }
		}

		partial void DisposeCore();

		internal void Dispose()
		{
			if (phase == LifetimePhase.Disposed)
				return;
			Debug.Assert(phase == LifetimePhase.Live);
			phase = LifetimePhase.Disposed;
			DisposeCore();
		}

		/// <summary>
		/// Gets the next possible caret position after visualColumn, or -1 if there is no caret position.
		/// </summary>
		public int GetNextCaretPosition(int visualColumn, LogicalDirection direction, CaretPositioningMode mode, bool allowVirtualSpace)
		{
			if (!HasStopsInVirtualSpace(mode))
				allowVirtualSpace = false;

			if (elements.Count == 0) {
				// special handling for empty visual lines:
				if (allowVirtualSpace) {
					if (direction == LogicalDirection.Forward)
						return Math.Max(0, visualColumn + 1);
					else if (visualColumn > 0)
						return visualColumn - 1;
					else
						return -1;
				} else {
					// even though we don't have any elements,
					// there's a single caret stop at visualColumn 0
					if (visualColumn < 0 && direction == LogicalDirection.Forward)
						return 0;
					else if (visualColumn > 0 && direction == LogicalDirection.Backward)
						return 0;
					else
						return -1;
				}
			}

			int i;
			if (direction == LogicalDirection.Backward) {
				// Search Backwards:
				// If the last element doesn't handle line borders, return the line end as caret stop
				if (visualColumn > this.VisualLength && !elements[elements.Count - 1].HandlesLineBorders && HasImplicitStopAtLineEnd(mode)) {
					if (allowVirtualSpace)
						return visualColumn - 1;
					else
						return this.VisualLength;
				}
				// skip elements that start after or at visualColumn
				for (i = elements.Count - 1; i >= 0; i--) {
					if (elements[i].VisualColumn < visualColumn)
						break;
				}
				// search last element that has a caret stop
				for (; i >= 0; i--) {
					int pos = elements[i].GetNextCaretPosition(
						Math.Min(visualColumn, elements[i].VisualColumn + elements[i].VisualLength + 1),
						direction, mode);
					if (pos >= 0)
						return pos;
				}
				// If we've found nothing, and the first element doesn't handle line borders,
				// return the line start as normal caret stop.
				if (visualColumn > 0 && !elements[0].HandlesLineBorders && HasImplicitStopAtLineStart(mode))
					return 0;
			} else {
				// Search Forwards:
				// If the first element doesn't handle line borders, return the line start as caret stop
				if (visualColumn < 0 && !elements[0].HandlesLineBorders && HasImplicitStopAtLineStart(mode))
					return 0;
				// skip elements that end before or at visualColumn
				for (i = 0; i < elements.Count; i++) {
					if (elements[i].VisualColumn + elements[i].VisualLength > visualColumn)
						break;
				}
				// search first element that has a caret stop
				for (; i < elements.Count; i++) {
					int pos = elements[i].GetNextCaretPosition(
						Math.Max(visualColumn, elements[i].VisualColumn - 1),
						direction, mode);
					if (pos >= 0)
						return pos;
				}
				// if we've found nothing, and the last element doesn't handle line borders,
				// return the line end as caret stop
				if ((allowVirtualSpace || !elements[elements.Count - 1].HandlesLineBorders) && HasImplicitStopAtLineEnd(mode)) {
					if (visualColumn < this.VisualLength)
						return this.VisualLength;
					else if (allowVirtualSpace)
						return visualColumn + 1;
				}
			}
			// we've found nothing, return -1 and let the caret search continue in the next line
			return -1;
		}

		static bool HasStopsInVirtualSpace(CaretPositioningMode mode)
		{
			return mode == CaretPositioningMode.Normal || mode == CaretPositioningMode.EveryCodepoint;
		}

		static bool HasImplicitStopAtLineStart(CaretPositioningMode mode)
		{
			return mode == CaretPositioningMode.Normal || mode == CaretPositioningMode.EveryCodepoint;
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "mode",
														 Justification = "make method consistent with HasImplicitStopAtLineStart; might depend on mode in the future")]
		static bool HasImplicitStopAtLineEnd(CaretPositioningMode mode)
		{
			return true;
		}
	}
}
