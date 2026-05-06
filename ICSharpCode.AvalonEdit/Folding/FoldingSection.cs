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

using System.Diagnostics;

using ICSharpCode.AvalonEdit.Document;

namespace ICSharpCode.AvalonEdit.Folding
{
	/// <summary>
	/// A section that can be folded.
	/// </summary>
	public sealed partial class FoldingSection : TextSegment
	{
		readonly FoldingManager manager;
		bool isFolded;
		string title;

		/// <summary>
		/// Gets/sets if the section is folded.
		/// </summary>
		public bool IsFolded {
			get { return isFolded; }
			set {
				if (isFolded != value) {
					isFolded = value;
					ValidateCollapsedLineSections();
					manager.RaiseFoldingsChanged(this);
				}
			}
		}

		internal void ValidateCollapsedLineSections()
		{
			ValidateCollapsedLineSectionsCore();
		}

		partial void ValidateCollapsedLineSectionsCore();

		/// <inheritdoc/>
		protected override void OnSegmentChanged()
		{
			ValidateCollapsedLineSections();
			base.OnSegmentChanged();
			// don't redraw if the FoldingSection wasn't added to the FoldingManager's collection yet
			if (IsConnectedToCollection)
				manager.RaiseFoldingsChanged(this);
		}

		/// <summary>
		/// Gets/Sets the text used to display the collapsed version of the folding section.
		/// </summary>
		public string Title {
			get {
				return title;
			}
			set {
				if (title != value) {
					title = value;
					if (this.IsFolded)
						manager.RaiseFoldingsChanged(this);
				}
			}
		}

		/// <summary>
		/// Gets the content of the collapsed lines as text.
		/// </summary>
		public string TextContent {
			get {
				return manager.document.GetText(StartOffset, EndOffset - StartOffset);
			}
		}

		/// <summary>
		/// Gets/Sets an additional object associated with this folding section.
		/// </summary>
		public object Tag { get; set; }

		internal FoldingSection(FoldingManager manager, int startOffset, int endOffset)
		{
			Debug.Assert(manager != null);
			this.manager = manager;
			this.StartOffset = startOffset;
			this.Length = endOffset - startOffset;
		}
	}
}
