﻿namespace NetBox.FileFormats.Csv
{
   using global::System.Collections.Generic;
   using global::System.IO;
   using global::System.Linq;
   using global::System.Runtime.CompilerServices;
   using global::System.Text;

   /// <summary>
   /// Reads data from a CSV file. Fast and reliable, supports:
   /// - newline characters
   /// - double quotes
   /// - commas
   /// </summary>
   class CsvReader
   {
      private readonly StreamReader _reader;
      private char[] _buffer;
      private const int BufferSize = 1024 * 10; //10k buffer
      private int _pos;
      private int _size = -1;
      private readonly List<char> _chars = new List<char>();
      private readonly List<string> _row = new List<string>();
      private ValueState _lastState = ValueState.None;

      private enum ValueState
      {
         None,
         HasMore,
         EndOfLine,
         EndOfFile
      }

      /// <summary>
      /// Creates an instance from an open stream and encoding
      /// </summary>
      public CsvReader(Stream stream, Encoding encoding)
      {
         _reader = new StreamReader(stream, encoding);
         _buffer = new char[BufferSize];
      }

      /// <summary>
      /// Reads all file as a dictionary of column name to list of values
      /// </summary>
      /// <param name="content">File content</param>
      /// <param name="hasColumns">When true, the first line of the file includes columns</param>
      /// <returns>Dictionary mapping the column name to the list of values</returns>
      public static Dictionary<string, List<string>> ReadAllFromContent(string content, bool hasColumns = true)
      {
         var result = new Dictionary<string, List<string>>();

         using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(content)))
         {
            var reader = new CsvReader(ms, Encoding.UTF8);

            string[] columnNames = hasColumns ? reader.ReadNextRow() : null;

            string[] values;
            while ((values = reader.ReadNextRow()) != null)
            {
               if (columnNames == null)
               {
                  columnNames = Enumerable.Range(1, values.Length).Select(v => v.ToString()).ToArray();
               }


               for (int i = 0; i < values.Length; i++)
               {
                  if(!result.TryGetValue(columnNames[i], out List<string> list))
                  {
                     list = new List<string>();
                     result[columnNames[i]] = list;
                  }

                  list.Add(values[i]);
               }

            }
         }

         return result;
      }

      /// <summary>
      /// Reads next row of data if available.
      /// </summary>
      /// <returns>Null when end of file is reached, or array of strings for each column.</returns>
      public string[] ReadNextRow()
      {
         if (ValueState.EndOfFile == _lastState) return null;

         _row.Clear();
         _chars.Clear();

         while (ValueState.HasMore == (_lastState = ReadNextValue()))
         {
            _row.Add(Str());
            _chars.Clear();
         }

         _row.Add(Str());

         return _row.ToArray();
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private string Str()
      {
         return new string(_chars.ToArray());
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private ValueState ReadNextValue()
      {
         int curr, next;
         bool quoted = false;
         short state = 0;
         while (NextChars(out curr, out next))
         {
            switch (state)
            {
               case 0:  //value start
                  if (curr == CsvFormat.ValueQuote)
                  {
                     //if the value starts with quote it:
                     // - ends with quote
                     // - double quote must be transformed into single quote
                     // - column separator (usuallly ',') can be contained within the value
                     // - line separator '\r' can be inside the value and must be transforted to a proper line feed
                     quoted = true;
                     state = 1;
                  }
                  else if (IsLineEndChar(curr))
                  {
                     while (IsLineEndChar(next))
                     {
                        NextChars(out curr, out next);
                     }

                     return next == -1 ? ValueState.EndOfFile : ValueState.EndOfLine;
                  }
                  else if (CsvFormat.ValueSeparator == curr)
                  {
                     //start from value separator, meaning it's an empty value
                     return next == -1 ? ValueState.EndOfFile : ValueState.HasMore;
                  }
                  else
                  {
                     //if the value doesn't start with quote:
                     // - it can't contain column separator or quote characters inside
                     // - it can't contain line separators
                     _chars.Add((char)curr);

                     if (CsvFormat.ValueSeparator == next)
                     {
                        state = 2;
                     }
                     else
                     {
                        state = 1;
                     }
                  }
                  break;

               case 1:  //reading value
                  if (quoted)
                  {
                     switch (curr)
                     {
                        case CsvFormat.ValueQuote:
                           if (next == CsvFormat.ValueQuote)
                           {
                              //escaped quote, make a single one
                              _chars.Add(CsvFormat.ValueQuote);

                              //fast-forward to the next character
                              _pos++;
                           }
                           else if (next == CsvFormat.ValueSeparator || next == '\r' || next == '\n')
                           {
                              //this is the end of value
                              state = 2;
                           }
                           else
                           {
                              throw new IOException($"unexpected character {next} after {curr} at position {_pos}");
                           }
                           break;
                        case '\r':
                           _chars.Add('\r');
                           _chars.Add('\n');
                           break;
                        default:
                           _chars.Add((char)curr);
                           break;
                     }
                  }
                  else
                  {
                     _chars.Add((char)curr);

                     //simple and most common case
                     if (next == CsvFormat.ValueSeparator || next == '\r' || next == '\n')
                     {
                        state = 2;
                     }
                  }
                  break;

               case 2:  //end of value
                  //if the character after end of value (curr) is a value separator it's not the end of line
                  bool hasMore = (curr == CsvFormat.ValueSeparator);

                  if (!hasMore)
                  {
                     while (IsLineEndChar(next))
                     {
                        NextChars(out curr, out next);
                     }
                  }

                  return hasMore
                     ? ValueState.HasMore
                     : (next == -1 ? ValueState.EndOfFile : ValueState.EndOfLine);
            }

         }

         return ValueState.EndOfFile;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private bool NextChars(out int curr, out int next)
      {
         if (_pos >= _size)
         {
            NextBlock();

            if (_size == 0)
            {
               curr = next = -1;
               return false;
            }
         }
         curr = _buffer[_pos++];


         if (_pos >= _size)
         {
            NextBlock();

            if (_size == 0)
            {
               next = -1;
               return true;
            }
         }
         next = _buffer[_pos];
         return true;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private bool NextBlock()
      {
         _size = _reader.ReadBlock(_buffer, 0, BufferSize);
         _pos = 0;
         return _size > 0;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static bool IsLineEndChar(int ch)
      {
         return ch == '\r' || ch == '\n';
      }
   }
}
