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

using ICSharpCode.AvalonEdit.Document;

namespace ICSharpCode.AvalonEdit.Rendering
{
	/// <summary>
	/// A block of layout space reserved above a document line's own text - e.g. a OpenLens row
	/// ("12 references | 3 overrides"), as opposed to a <see cref="VisualLineElementGenerator"/>'s
	/// inline element, which sits inside the text flow at a document offset. See
	/// externals/OpenDevelop/doc/technotes/codelens.md §14.2/§14.3 for why an inline element cannot
	/// correctly represent this (no reserved layout space, wrong participation in scrolling/hit
	/// testing/word wrap).
	/// </summary>
	public interface IVisualLineBlockAdornment
	{
		/// <summary>
		/// Identifies this adornment across recomputation passes so a host can associate WPF state
		/// (focus, hover) with "the same" adornment even though a fresh instance is produced on each
		/// <see cref="IVisualLineBlockAdornmentGenerator.GetBlockAdornments"/> call.
		/// </summary>
		object Key { get; }

		/// <summary>
		/// The height, in device-independent pixels, this adornment reserves above the line's text.
		/// Must stay stable for a given <see cref="Key"/> across calls within the same edit
		/// generation - a value that changes on every layout pass would defeat the height tree's
		/// caching and cause layout thrash.
		/// </summary>
		double DesiredHeight { get; }

		/// <summary>
		/// Creates the visual to arrange into the reserved space. Called at most once per
		/// visual-line (re)construction - not on every render pass - so implementations may do
		/// lightweight setup here (event handler wiring etc.) without worrying about being called
		/// redundantly.
		/// </summary>
		UIElement CreateVisual();
	}

	/// <summary>
	/// Contributes <see cref="IVisualLineBlockAdornment"/>s for a document line, registered on
	/// <see cref="TextView.BlockAdornmentGenerators"/>. Multiple generators may contribute to the
	/// same line - their adornments stack vertically in registration order, each in its own
	/// <see cref="IVisualLineBlockAdornment.DesiredHeight"/> slot; composing several logical lenses
	/// into a single row (doc §14.4) is the generator's own responsibility (e.g. OpenLens returns
	/// one combined adornment per line, not one per lens item).
	/// </summary>
	public interface IVisualLineBlockAdornmentGenerator
	{
		/// <summary>
		/// Returns the block adornments (if any) that should be reserved above
		/// <paramref name="documentLine"/> - an empty/null result means "no adornment for this
		/// line". Called once per visual-line construction, on the UI thread, for whichever
		/// document line starts each visual line; a generator with nothing to say about most lines
		/// should return quickly (e.g. a HashSet lookup) rather than doing expensive work here -
		/// expensive computation belongs behind the adornment's own lazily-resolving visual, not in
		/// this method.
		/// </summary>
		IReadOnlyList<IVisualLineBlockAdornment> GetBlockAdornments(TextView textView, DocumentLine documentLine);
	}
}
