// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;
using Spectre.Console;
using Spectre.Console.Advanced;

using Table = Markdig.Extensions.Tables.Table;
using TableRow = Markdig.Extensions.Tables.TableRow;
using TableCell = Markdig.Extensions.Tables.TableCell;

namespace Markdown.VT;

/// <summary>
/// A VT100 renderer for a <see cref="Table"/>
/// </summary>
/// <seealso cref="VTObjectRenderer{Table}" />
public class VTTableRenderer : VTObjectRenderer<Table>
{
    private readonly IAnsiConsole _myConsole;

    public VTTableRenderer()
    {
        _myConsole = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Detect,
                ColorSystem = ColorSystemSupport.Detect,
                Out = new AnsiConsoleOutput(Console.Out),
            }
        );
    }

    protected override void Write(VTRenderer renderer, Table table)
    {
        var spectreTable = new Spectre.Console.Table()
            .LeftAligned()
            .MinimalBorder();

        var sb = new StringBuilder();
        var newWriter = new StringWriter(sb);
        var origWriter = renderer.Writer;
        var origUseMarkup = renderer.UseSpectreMarkup;

        try
        {
            renderer.Writer = newWriter;
            renderer.UseSpectreMarkup = true;

            int rowIndex = -1;
            foreach (TableRow row in table)
            {
                if (!row.IsHeader)
                {
                    spectreTable.AddEmptyRow();
                    rowIndex++;
                }

                for (int i = 0; i < row.Count; i++)
                {
                    var cell = (TableCell)row[i];
                    renderer.Write(cell);

                    newWriter.Flush();
                    string cellContent = sb.ToString().Trim();
                    sb.Clear();

                    if (row.IsHeader)
                    {
                        spectreTable.AddColumn($"[green bold]{cellContent}[/]");
                    }
                    else
                    {
                        spectreTable.Rows.Update(rowIndex, i, new Markup(cellContent));
                    }
                }
            }
        }
        finally
        {
            renderer.Writer = origWriter;
            renderer.UseSpectreMarkup = origUseMarkup;
        }

        int start = 0;
        int consoleWidth = AnsiConsole.Profile.Width;
        int indentWidth = renderer.GetIndentWidth();

        _myConsole.Profile.Width = consoleWidth - indentWidth;
        string result = _myConsole.ToAnsi(spectreTable);
        if (OperatingSystem.IsWindows())
        {
            // Normalize line endings to be LF only.
            result = result.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        renderer.WriteLine();
        while (true)
        {
            if (start == result.Length)
            {
                break;
            }

            int nlIndex = result.IndexOf('\n', start);
            int length = nlIndex is -1 ? result.Length - start : nlIndex - start + 1;
            var span = result.AsSpan(start, length);
            if (!span.IsWhiteSpace())
            {
                renderer.Write(span.Trim('\n'));
                // Call 'WriteLine' explicitly to make sure the indentation is applied.
                renderer.WriteLine();
            }

            if (nlIndex is -1)
            {
                break;
            }

            start = nlIndex + 1;
        }

        renderer.EnsureLine();
    }
}
