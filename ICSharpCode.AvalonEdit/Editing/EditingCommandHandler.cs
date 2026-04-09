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
using System.Globalization;

using ICSharpCode.AvalonEdit.Document;

namespace ICSharpCode.AvalonEdit.Editing
{
	/// <summary>
	/// We re-use the CommandBinding and InputBinding instances between multiple text areas,
	/// so this class is static.
	/// </summary>
	static partial class EditingCommandHandler
	{
		#region Text Transformation Helpers
		enum DefaultSegmentType
		{
			None,
			WholeDocument,
			CurrentLine
		}

		static void ConvertTabsToSpaces(TextArea textArea, ISegment segment)
		{
			TextDocument document = textArea.Document;
			int endOffset = segment.EndOffset;
			string indentationString = new string(' ', textArea.Options.IndentationSize);
			for (int offset = segment.Offset; offset < endOffset; offset++) {
				if (document.GetCharAt(offset) == '\t') {
					document.Replace(offset, 1, indentationString, OffsetChangeMappingType.CharacterReplace);
					endOffset += indentationString.Length - 1;
				}
			}
		}

		static void ConvertSpacesToTabs(TextArea textArea, ISegment segment)
		{
			TextDocument document = textArea.Document;
			int endOffset = segment.EndOffset;
			int indentationSize = textArea.Options.IndentationSize;
			int spacesCount = 0;
			for (int offset = segment.Offset; offset < endOffset; offset++) {
				if (document.GetCharAt(offset) == ' ') {
					spacesCount++;
					if (spacesCount == indentationSize) {
						document.Replace(offset - (indentationSize - 1), indentationSize, "\t", OffsetChangeMappingType.CharacterReplace);
						spacesCount = 0;
						offset -= indentationSize - 1;
						endOffset -= indentationSize - 1;
					}
				} else {
					spacesCount = 0;
				}
			}
		}

		static string InvertCase(string text)
		{
			CultureInfo culture = CultureInfo.CurrentCulture;
			char[] buffer = text.ToCharArray();
			for (int i = 0; i < buffer.Length; ++i) {
				char c = buffer[i];
				buffer[i] = char.IsUpper(c) ? char.ToLower(c, culture) : char.ToUpper(c, culture);
			}
			return new string(buffer);
		}
		#endregion
	}
}
