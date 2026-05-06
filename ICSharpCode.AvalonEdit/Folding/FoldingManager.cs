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
using System.Linq;
using System.Windows;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit.Folding
{
	/// <summary>
	/// Stores a list of foldings for a specific TextDocument.
	/// </summary>
	public partial class FoldingManager : IWeakEventListener
	{
		internal readonly TextDocument document;
		readonly TextSegmentCollection<FoldingSection> foldings;
		bool isFirstUpdate = true;

		/// <summary>
		/// Raised whenever the set of foldings or the IsFolded state changes.
		/// </summary>
		public event EventHandler FoldingsChanged;

		/// <summary>
		/// Gets the document whose foldings are tracked.
		/// </summary>
		public TextDocument Document {
			get { return document; }
		}

		#region Constructor
		/// <summary>
		/// Creates a new FoldingManager instance.
		/// </summary>
		public FoldingManager(TextDocument document)
		{
			if (document == null)
				throw new ArgumentNullException("document");
			this.document = document;
			this.foldings = new TextSegmentCollection<FoldingSection>();
			document.VerifyAccess();
			TextDocumentWeakEventManager.Changed.AddListener(document, this);
		}
		#endregion

		#region ReceiveWeakEvent
		/// <inheritdoc cref="IWeakEventListener.ReceiveWeakEvent"/>
		protected virtual bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
		{
			if (managerType == typeof(TextDocumentWeakEventManager.Changed)) {
				OnDocumentChanged((DocumentChangeEventArgs)e);
				return true;
			}
			return false;
		}

		bool IWeakEventListener.ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
		{
			return ReceiveWeakEvent(managerType, sender, e);
		}

		void OnDocumentChanged(DocumentChangeEventArgs e)
		{
			foldings.UpdateOffsets(e);
			int newEndOffset = e.Offset + e.InsertionLength;
			// extend end offset to the end of the line (including delimiter)
			var endLine = document.GetLineByOffset(newEndOffset);
			newEndOffset = endLine.Offset + endLine.TotalLength;
			foreach (var affectedFolding in foldings.FindOverlappingSegments(e.Offset, newEndOffset - e.Offset).ToList()) {
				if (affectedFolding.Length == 0) {
					RemoveFolding(affectedFolding);
				} else {
					affectedFolding.ValidateCollapsedLineSections();
				}
			}
			RaiseFoldingsChanged();
		}
		#endregion

		#region Create / Remove / Clear
		/// <summary>
		/// Creates a folding for the specified text section.
		/// </summary>
		public FoldingSection CreateFolding(int startOffset, int endOffset)
		{
			if (startOffset >= endOffset)
				throw new ArgumentException("startOffset must be less than endOffset");
			if (startOffset < 0 || endOffset > document.TextLength)
				throw new ArgumentException("Folding must be within document boundary");
			FoldingSection fs = new FoldingSection(this, startOffset, endOffset);
			foldings.Add(fs);
			RaiseFoldingsChanged(fs);
			return fs;
		}

		/// <summary>
		/// Removes a folding section from this manager.
		/// </summary>
		public void RemoveFolding(FoldingSection fs)
		{
			if (fs == null)
				throw new ArgumentNullException("fs");
			fs.IsFolded = false;
			foldings.Remove(fs);
			RaiseFoldingsChanged(fs);
		}

		/// <summary>
		/// Removes all folding sections.
		/// </summary>
		public void Clear()
		{
			document.VerifyAccess();
			foreach (FoldingSection s in foldings)
				s.IsFolded = false;
			foldings.Clear();
			RaiseFoldingsChanged();
		}
		#endregion

		#region Get...Folding
		/// <summary>
		/// Gets all foldings in this manager.
		/// The foldings are returned sorted by start offset;
		/// for multiple foldings at the same offset the order is undefined.
		/// </summary>
		public IEnumerable<FoldingSection> AllFoldings {
			get { return foldings; }
		}

		/// <summary>
		/// Gets the first offset greater or equal to <paramref name="startOffset"/> where a folded folding starts.
		/// Returns -1 if there are no foldings after <paramref name="startOffset"/>.
		/// </summary>
		public int GetNextFoldedFoldingStart(int startOffset)
		{
			FoldingSection fs = foldings.FindFirstSegmentWithStartAfter(startOffset);
			while (fs != null && !fs.IsFolded)
				fs = foldings.GetNextSegment(fs);
			return fs != null ? fs.StartOffset : -1;
		}

		/// <summary>
		/// Gets the first folding with a <see cref="TextSegment.StartOffset"/> greater or equal to
		/// <paramref name="startOffset"/>.
		/// Returns null if there are no foldings after <paramref name="startOffset"/>.
		/// </summary>
		public FoldingSection GetNextFolding(int startOffset)
		{
			// TODO: returns the longest folding instead of any folding at the first position after startOffset
			return foldings.FindFirstSegmentWithStartAfter(startOffset);
		}

		/// <summary>
		/// Gets all foldings that start exactly at <paramref name="startOffset"/>.
		/// </summary>
		public ReadOnlyCollection<FoldingSection> GetFoldingsAt(int startOffset)
		{
			List<FoldingSection> result = new List<FoldingSection>();
			FoldingSection fs = foldings.FindFirstSegmentWithStartAfter(startOffset);
			while (fs != null && fs.StartOffset == startOffset) {
				result.Add(fs);
				fs = foldings.GetNextSegment(fs);
			}
			return result.AsReadOnly();
		}

		/// <summary>
		/// Gets all foldings that contain <paramref name="offset" />.
		/// </summary>
		public ReadOnlyCollection<FoldingSection> GetFoldingsContaining(int offset)
		{
			return foldings.FindSegmentsContaining(offset);
		}
		#endregion

		#region UpdateFoldings
		/// <summary>
		/// Updates the foldings in this <see cref="FoldingManager"/> using the given new foldings.
		/// This method will try to detect which new foldings correspond to which existing foldings; and will keep the state
		/// (<see cref="FoldingSection.IsFolded"/>) for existing foldings.
		/// </summary>
		/// <param name="newFoldings">The new set of foldings. These must be sorted by starting offset.</param>
		/// <param name="firstErrorOffset">The first position of a parse error. Existing foldings starting after
		/// this offset will be kept even if they don't appear in <paramref name="newFoldings"/>.
		/// Use -1 for this parameter if there were no parse errors.</param>
		public void UpdateFoldings(IEnumerable<NewFolding> newFoldings, int firstErrorOffset)
		{
			if (newFoldings == null)
				throw new ArgumentNullException("newFoldings");

			if (firstErrorOffset < 0)
				firstErrorOffset = int.MaxValue;

			var oldFoldings = this.AllFoldings.ToArray();
			int oldFoldingIndex = 0;
			int previousStartOffset = 0;
			// merge new foldings into old foldings so that sections keep being collapsed
			// both oldFoldings and newFoldings are sorted by start offset
			foreach (NewFolding newFolding in newFoldings) {
				// ensure newFoldings are sorted correctly
				if (newFolding.StartOffset < previousStartOffset)
					throw new ArgumentException("newFoldings must be sorted by start offset");
				previousStartOffset = newFolding.StartOffset;

				int startOffset = newFolding.StartOffset.CoerceValue(0, document.TextLength);
				int endOffset = newFolding.EndOffset.CoerceValue(0, document.TextLength);

				if (newFolding.StartOffset == newFolding.EndOffset)
					continue; // ignore zero-length foldings

				// remove old foldings that were skipped
				while (oldFoldingIndex < oldFoldings.Length && newFolding.StartOffset > oldFoldings[oldFoldingIndex].StartOffset) {
					this.RemoveFolding(oldFoldings[oldFoldingIndex++]);
				}
				FoldingSection section;
				// reuse current folding if its matching:
				if (oldFoldingIndex < oldFoldings.Length && newFolding.StartOffset == oldFoldings[oldFoldingIndex].StartOffset) {
					section = oldFoldings[oldFoldingIndex++];
					section.Length = endOffset - startOffset;
				} else {
					// no matching current folding; create a new one:
					section = this.CreateFolding(startOffset, endOffset);
					// auto-close #regions only when opening the document
					if (isFirstUpdate) {
						section.IsFolded = newFolding.DefaultClosed;
					}
					section.Tag = newFolding;
				}
				section.Title = newFolding.Name;
			}
			isFirstUpdate = false;
			// remove all outstanding old foldings:
			while (oldFoldingIndex < oldFoldings.Length) {
				FoldingSection oldSection = oldFoldings[oldFoldingIndex++];
				if (oldSection.StartOffset >= firstErrorOffset)
					break;
				this.RemoveFolding(oldSection);
			}
		}
		#endregion

		internal void RaiseFoldingsChanged()
		{
			RaiseFoldingsChanged(null);
		}

		internal void RaiseFoldingsChanged(FoldingSection fs)
		{
			FoldingsChanged?.Invoke(this, EventArgs.Empty);
			OnFoldingsChanged(fs);
		}

		partial void OnFoldingsChanged(FoldingSection fs);
	}
}
