// Copyright (c) 2025 AlphaSierraPapa for the SharpDevelop Team
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

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Document;
using NUnit.Framework;

namespace ICSharpCode.AvalonEdit.Rendering
{
	/// <summary>
	/// Headless (no window, no HwndSource) regression tests for the block-adornment API
	/// (externals/OpenDevelop/doc/technotes/codelens.md §14.2) - drives <see cref="TextView"/>
	/// directly through Measure/Arrange rather than via a real visual tree.
	/// </summary>
	[TestFixture]
	public class BlockAdornmentTests
	{
		static readonly Size LayoutSize = new(400, 400);

		static TextView CreateLayoutedTextView(string text)
		{
			var textView = new TextView { Document = new TextDocument(text) };
			Layout(textView);
			return textView;
		}

		static void Layout(TextView textView)
		{
			textView.Measure(LayoutSize);
			textView.Arrange(new Rect(LayoutSize));
		}

		sealed class FixedHeightAdornmentGenerator : IVisualLineBlockAdornmentGenerator
		{
			readonly int lineNumber;
			readonly double height;

			public FixedHeightAdornmentGenerator(int lineNumber, double height)
			{
				this.lineNumber = lineNumber;
				this.height = height;
			}

			public IReadOnlyList<IVisualLineBlockAdornment> GetBlockAdornments(TextView textView, DocumentLine documentLine)
			{
				if (documentLine.LineNumber != lineNumber)
					return null;
				return new IVisualLineBlockAdornment[] { new FixedHeightAdornment(height) };
			}
		}

		sealed class FixedHeightAdornment : IVisualLineBlockAdornment
		{
			public FixedHeightAdornment(double height) => DesiredHeight = height;
			public object Key => "fixed";
			public double DesiredHeight { get; }
			public int AlignToColumn => -1;
			public UIElement CreateVisual() => new Border { Width = 10, Height = DesiredHeight };
		}

		[Test]
		public void RegisteringGenerator_IncreasesVisualLineHeightForItsLine()
		{
			var textView = CreateLayoutedTextView("line1\nline2\nline3\n");
			var lineWithoutAdornment = textView.GetOrConstructVisualLine(textView.Document.GetLineByNumber(2));
			double heightWithout = lineWithoutAdornment.Height;

			const double adornmentHeight = 37;
			textView.BlockAdornmentGenerators.Add(new FixedHeightAdornmentGenerator(2, adornmentHeight));
			textView.Redraw(System.Windows.Threading.DispatcherPriority.Send);
			Layout(textView);

			var visualLine = textView.GetVisualLine(2);
			Assert.That(visualLine, Is.Not.Null);
			Assert.That(visualLine.BlockAdornmentHeight, Is.EqualTo(adornmentHeight).Within(0.01));
			Assert.That(visualLine.Height, Is.EqualTo(heightWithout + adornmentHeight).Within(0.01));
		}

		[Test]
		public void RegisteringGenerator_PushesLaterLinesDownByTheReservedHeight()
		{
			var textView = CreateLayoutedTextView("line1\nline2\nline3\n");
			double topOfLine3Without = textView.GetVisualTopByDocumentLine(3);

			const double adornmentHeight = 21;
			textView.BlockAdornmentGenerators.Add(new FixedHeightAdornmentGenerator(2, adornmentHeight));
			textView.Redraw(System.Windows.Threading.DispatcherPriority.Send);
			Layout(textView);

			double topOfLine3With = textView.GetVisualTopByDocumentLine(3);
			Assert.That(topOfLine3With, Is.EqualTo(topOfLine3Without + adornmentHeight).Within(0.01));
		}

		[Test]
		public void NoGenerator_LeavesBlockAdornmentHeightZero()
		{
			var textView = CreateLayoutedTextView("line1\nline2\n");
			var visualLine = textView.GetOrConstructVisualLine(textView.Document.GetLineByNumber(1));
			Assert.That(visualLine.BlockAdornmentHeight, Is.EqualTo(0));
		}
	}
}
