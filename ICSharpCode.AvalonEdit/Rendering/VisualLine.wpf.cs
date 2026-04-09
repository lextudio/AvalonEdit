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
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit.Rendering
{
	public partial class VisualLine
	{
		ReadOnlyCollection<TextLine> textLines;
		VisualLineDrawingVisual visual;

		/// <summary>
		/// Gets a read-only collection of text lines.
		/// </summary>
		public ReadOnlyCollection<TextLine> TextLines {
			get {
				if (phase < LifetimePhase.Live)
					throw new InvalidOperationException();
				return textLines;
			}
		}

		internal void ConstructVisualElements(ITextRunConstructionContext context, VisualLineElementGenerator[] generators)
		{
			Debug.Assert(phase == LifetimePhase.Generating);
			foreach (VisualLineElementGenerator g in generators) {
				g.StartGeneration(context);
			}
			elements = new List<VisualLineElement>();
			PerformVisualElementConstruction(generators);
			foreach (VisualLineElementGenerator g in generators) {
				g.FinishGeneration();
			}

			var globalTextRunProperties = context.GlobalTextRunProperties;
			foreach (var element in elements) {
				element.SetTextRunProperties(new VisualLineElementTextRunProperties(globalTextRunProperties));
			}
			this.Elements = elements.AsReadOnly();
			CalculateOffsets();
			phase = LifetimePhase.Transforming;
		}

		void PerformVisualElementConstruction(VisualLineElementGenerator[] generators)
		{
			TextDocument document = this.Document;
			int offset = FirstDocumentLine.Offset;
			int currentLineEnd = offset + FirstDocumentLine.Length;
			LastDocumentLine = FirstDocumentLine;
			int askInterestOffset = 0; // 0 or 1
			while (offset + askInterestOffset <= currentLineEnd) {
				int textPieceEndOffset = currentLineEnd;
				foreach (VisualLineElementGenerator g in generators) {
					g.cachedInterest = g.GetFirstInterestedOffset(offset + askInterestOffset);
					if (g.cachedInterest != -1) {
						if (g.cachedInterest < offset)
							throw new ArgumentOutOfRangeException(g.GetType().Name + ".GetFirstInterestedOffset",
																  g.cachedInterest,
																  "GetFirstInterestedOffset must not return an offset less than startOffset. Return -1 to signal no interest.");
						if (g.cachedInterest < textPieceEndOffset)
							textPieceEndOffset = g.cachedInterest;
					}
				}
				Debug.Assert(textPieceEndOffset >= offset);
				if (textPieceEndOffset > offset) {
					int textPieceLength = textPieceEndOffset - offset;
					elements.Add(new VisualLineText(this, textPieceLength));
					offset = textPieceEndOffset;
				}
				// If no elements constructed / only zero-length elements constructed:
				// do not asking the generators again for the same location (would cause endless loop)
				askInterestOffset = 1;
				foreach (VisualLineElementGenerator g in generators) {
					if (g.cachedInterest == offset) {
						VisualLineElement element = g.ConstructElement(offset);
						if (element != null) {
							elements.Add(element);
							if (element.DocumentLength > 0) {
								// a non-zero-length element was constructed
								askInterestOffset = 0;
								offset += element.DocumentLength;
								if (offset > currentLineEnd) {
									DocumentLine newEndLine = document.GetLineByOffset(offset);
									currentLineEnd = newEndLine.Offset + newEndLine.Length;
									this.LastDocumentLine = newEndLine;
									if (currentLineEnd < offset) {
										throw new InvalidOperationException(
											"The VisualLineElementGenerator " + g.GetType().Name +
											" produced an element which ends within the line delimiter");
									}
								}
								break;
							}
						}
					}
				}
			}
		}

		internal void RunTransformers(ITextRunConstructionContext context, IVisualLineTransformer[] transformers)
		{
			Debug.Assert(phase == LifetimePhase.Transforming);
			foreach (IVisualLineTransformer transformer in transformers) {
				transformer.Transform(context, elements);
			}
			// For some strange reason, WPF requires that either all or none of the typography properties are set.
			if (elements.Any(e => e.TextRunProperties.TypographyProperties != null)) {
				// Fix typographic properties
				foreach (VisualLineElement element in elements) {
					if (element.TextRunProperties.TypographyProperties == null) {
						element.TextRunProperties.SetTypographyProperties(new DefaultTextRunTypographyProperties());
					}
				}
			}
			phase = LifetimePhase.Live;
		}

		internal void SetTextLines(List<TextLine> textLines)
		{
			this.textLines = textLines.AsReadOnly();
			Height = 0;
			foreach (TextLine line in textLines)
				Height += line.Height;
		}

		partial void DisposeCore()
		{
			foreach (TextLine textLine in textLines) {
				textLine.Dispose();
			}
		}

		/// <summary>
		/// Gets the text line containing the specified visual column.
		/// </summary>
		public TextLine GetTextLine(int visualColumn)
		{
			return GetTextLine(visualColumn, false);
		}

		/// <summary>
		/// Gets the text line containing the specified visual column.
		/// </summary>
		public TextLine GetTextLine(int visualColumn, bool isAtEndOfLine)
		{
			if (visualColumn < 0)
				throw new ArgumentOutOfRangeException("visualColumn");
			if (visualColumn >= VisualLengthWithEndOfLineMarker)
				return TextLines[TextLines.Count - 1];
			foreach (TextLine line in TextLines) {
				if (isAtEndOfLine ? visualColumn <= line.Length : visualColumn < line.Length)
					return line;
				else
					visualColumn -= line.Length;
			}
			throw new InvalidOperationException("Shouldn't happen (VisualLength incorrect?)");
		}

		/// <summary>
		/// Gets the visual top from the specified text line.
		/// </summary>
		/// <returns>Distance in device-independent pixels
		/// from the top of the document to the top of the specified text line.</returns>
		public double GetTextLineVisualYPosition(TextLine textLine, VisualYPosition yPositionMode)
		{
			if (textLine == null)
				throw new ArgumentNullException("textLine");
			double pos = VisualTop;
			foreach (TextLine tl in TextLines) {
				if (tl == textLine) {
					switch (yPositionMode) {
						case VisualYPosition.LineTop:
							return pos;
						case VisualYPosition.LineMiddle:
							return pos + tl.Height / 2;
						case VisualYPosition.LineBottom:
							return pos + tl.Height;
						case VisualYPosition.TextTop:
							return pos + tl.Baseline - textView.DefaultBaseline;
						case VisualYPosition.TextBottom:
							return pos + tl.Baseline - textView.DefaultBaseline + textView.DefaultLineHeight;
						case VisualYPosition.TextMiddle:
							return pos + tl.Baseline - textView.DefaultBaseline + textView.DefaultLineHeight / 2;
						case VisualYPosition.Baseline:
							return pos + tl.Baseline;
						default:
							throw new ArgumentException("Invalid yPositionMode:" + yPositionMode);
					}
				} else {
					pos += tl.Height;
				}
			}
			throw new ArgumentException("textLine is not a line in this VisualLine");
		}

		/// <summary>
		/// Gets the start visual column from the specified text line.
		/// </summary>
		public int GetTextLineVisualStartColumn(TextLine textLine)
		{
			if (!TextLines.Contains(textLine))
				throw new ArgumentException("textLine is not a line in this VisualLine");
			int col = 0;
			foreach (TextLine tl in TextLines) {
				if (tl == textLine)
					break;
				else
					col += tl.Length;
			}
			return col;
		}

		/// <summary>
		/// Gets a TextLine by the visual position.
		/// </summary>
		public TextLine GetTextLineByVisualYPosition(double visualTop)
		{
			const double epsilon = 0.0001;
			double pos = this.VisualTop;
			foreach (TextLine tl in TextLines) {
				pos += tl.Height;
				if (visualTop + epsilon < pos)
					return tl;
			}
			return TextLines[TextLines.Count - 1];
		}

		/// <summary>
		/// Gets the visual position from the specified visualColumn.
		/// </summary>
		/// <returns>Position in device-independent pixels
		/// relative to the top left of the document.</returns>
		public Point GetVisualPosition(int visualColumn, VisualYPosition yPositionMode)
		{
			TextLine textLine = GetTextLine(visualColumn);
			double xPos = GetTextLineVisualXPosition(textLine, visualColumn);
			double yPos = GetTextLineVisualYPosition(textLine, yPositionMode);
			return new Point(xPos, yPos);
		}

		internal Point GetVisualPosition(int visualColumn, bool isAtEndOfLine, VisualYPosition yPositionMode)
		{
			TextLine textLine = GetTextLine(visualColumn, isAtEndOfLine);
			double xPos = GetTextLineVisualXPosition(textLine, visualColumn);
			double yPos = GetTextLineVisualYPosition(textLine, yPositionMode);
			return new Point(xPos, yPos);
		}

		/// <summary>
		/// Gets the distance to the left border of the text area of the specified visual column.
		/// The visual column must belong to the specified text line.
		/// </summary>
		public double GetTextLineVisualXPosition(TextLine textLine, int visualColumn)
		{
			if (textLine == null)
				throw new ArgumentNullException("textLine");
			double xPos = textLine.GetDistanceFromCharacterHit(
				new CharacterHit(Math.Min(visualColumn, VisualLengthWithEndOfLineMarker), 0));
			if (visualColumn > VisualLengthWithEndOfLineMarker) {
				xPos += (visualColumn - VisualLengthWithEndOfLineMarker) * textView.WideSpaceWidth;
			}
			return xPos;
		}

		/// <summary>
		/// Gets the visual column from a document position (relative to top left of the document).
		/// If the user clicks between two visual columns, rounds to the nearest column.
		/// </summary>
		public int GetVisualColumn(Point point)
		{
			return GetVisualColumn(point, textView.Options.EnableVirtualSpace);
		}

		/// <summary>
		/// Gets the visual column from a document position (relative to top left of the document).
		/// If the user clicks between two visual columns, rounds to the nearest column.
		/// </summary>
		public int GetVisualColumn(Point point, bool allowVirtualSpace)
		{
			return GetVisualColumn(GetTextLineByVisualYPosition(point.Y), point.X, allowVirtualSpace);
		}

		internal int GetVisualColumn(Point point, bool allowVirtualSpace, out bool isAtEndOfLine)
		{
			var textLine = GetTextLineByVisualYPosition(point.Y);
			int vc = GetVisualColumn(textLine, point.X, allowVirtualSpace);
			isAtEndOfLine = (vc >= GetTextLineVisualStartColumn(textLine) + textLine.Length);
			return vc;
		}

		/// <summary>
		/// Gets the visual column from a document position (relative to top left of the document).
		/// If the user clicks between two visual columns, rounds to the nearest column.
		/// </summary>
		public int GetVisualColumn(TextLine textLine, double xPos, bool allowVirtualSpace)
		{
			if (xPos > textLine.WidthIncludingTrailingWhitespace) {
				if (allowVirtualSpace && textLine == TextLines[TextLines.Count - 1]) {
					int virtualX = (int)Math.Round((xPos - textLine.WidthIncludingTrailingWhitespace) / textView.WideSpaceWidth, MidpointRounding.AwayFromZero);
					return VisualLengthWithEndOfLineMarker + virtualX;
				}
			}
			CharacterHit ch = textLine.GetCharacterHitFromDistance(xPos);
			return ch.FirstCharacterIndex + ch.TrailingLength;
		}

		/// <summary>
		/// Gets the visual column from a document position (relative to top left of the document).
		/// If the user clicks between two visual columns, returns the first of those columns.
		/// </summary>
		public int GetVisualColumnFloor(Point point)
		{
			return GetVisualColumnFloor(point, textView.Options.EnableVirtualSpace);
		}

		/// <summary>
		/// Gets the visual column from a document position (relative to top left of the document).
		/// If the user clicks between two visual columns, returns the first of those columns.
		/// </summary>
		public int GetVisualColumnFloor(Point point, bool allowVirtualSpace)
		{
			bool tmp;
			return GetVisualColumnFloor(point, allowVirtualSpace, out tmp);
		}

		internal int GetVisualColumnFloor(Point point, bool allowVirtualSpace, out bool isAtEndOfLine)
		{
			TextLine textLine = GetTextLineByVisualYPosition(point.Y);
			if (point.X > textLine.WidthIncludingTrailingWhitespace) {
				isAtEndOfLine = true;
				if (allowVirtualSpace && textLine == TextLines[TextLines.Count - 1]) {
					// clicking virtual space in the last line
					int virtualX = (int)((point.X - textLine.WidthIncludingTrailingWhitespace) / textView.WideSpaceWidth);
					return VisualLengthWithEndOfLineMarker + virtualX;
				} else {
					// GetCharacterHitFromDistance returns a hit with FirstCharacterIndex=last character in line
					// and TrailingLength=1 when clicking behind the line, so the floor function needs to handle this case
					// specially and return the line's end column instead.
					return GetTextLineVisualStartColumn(textLine) + textLine.Length;
				}
			} else {
				isAtEndOfLine = false;
			}
			CharacterHit ch = textLine.GetCharacterHitFromDistance(point.X);
			return ch.FirstCharacterIndex;
		}

		/// <summary>
		/// Gets the text view position from the specified visual position.
		/// If the position is within a character, it is rounded to the next character boundary.
		/// </summary>
		/// <param name="visualPosition">The position in WPF device-independent pixels relative
		/// to the top left corner of the document.</param>
		/// <param name="allowVirtualSpace">Controls whether positions in virtual space may be returned.</param>
		public TextViewPosition GetTextViewPosition(Point visualPosition, bool allowVirtualSpace)
		{
			bool isAtEndOfLine;
			int visualColumn = GetVisualColumn(visualPosition, allowVirtualSpace, out isAtEndOfLine);
			int documentOffset = GetRelativeOffset(visualColumn) + this.FirstDocumentLine.Offset;
			TextViewPosition pos = new TextViewPosition(this.Document.GetLocation(documentOffset), visualColumn);
			pos.IsAtEndOfLine = isAtEndOfLine;
			return pos;
		}

		/// <summary>
		/// Gets the text view position from the specified visual position.
		/// If the position is inside a character, the position in front of the character is returned.
		/// </summary>
		/// <param name="visualPosition">The position in WPF device-independent pixels relative
		/// to the top left corner of the document.</param>
		/// <param name="allowVirtualSpace">Controls whether positions in virtual space may be returned.</param>
		public TextViewPosition GetTextViewPositionFloor(Point visualPosition, bool allowVirtualSpace)
		{
			bool isAtEndOfLine;
			int visualColumn = GetVisualColumnFloor(visualPosition, allowVirtualSpace, out isAtEndOfLine);
			int documentOffset = GetRelativeOffset(visualColumn) + this.FirstDocumentLine.Offset;
			TextViewPosition pos = new TextViewPosition(this.Document.GetLocation(documentOffset), visualColumn);
			pos.IsAtEndOfLine = isAtEndOfLine;
			return pos;
		}

		internal VisualLineDrawingVisual Render()
		{
			Debug.Assert(phase == LifetimePhase.Live);
			if (visual == null)
				visual = new VisualLineDrawingVisual(this, textView.FlowDirection);
			return visual;
		}
	}

	sealed class VisualLineDrawingVisual : DrawingVisual
	{
		public readonly VisualLine VisualLine;
		public readonly double Height;
		internal bool IsAdded;

		public VisualLineDrawingVisual(VisualLine visualLine, FlowDirection flow)
		{
			this.VisualLine = visualLine;
			var drawingContext = RenderOpen();
			double pos = 0;
			foreach (TextLine textLine in visualLine.TextLines) {
				if (flow == FlowDirection.LeftToRight) {
					textLine.Draw(drawingContext, new Point(0, pos), InvertAxes.None);
				} else {
					// Invert Axis for RightToLeft (Arabic language) support
					textLine.Draw(drawingContext, new Point(0, pos), InvertAxes.Horizontal);
				}
				pos += textLine.Height;
			}
			this.Height = pos;
			drawingContext.Close();
		}

		protected override GeometryHitTestResult HitTestCore(GeometryHitTestParameters hitTestParameters)
		{
			return null;
		}

		protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
		{
			return null;
		}
	}
}
