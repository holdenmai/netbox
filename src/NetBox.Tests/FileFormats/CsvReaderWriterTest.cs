﻿using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NetBox.FileFormats;
using Xunit;

namespace NetBox.Tests.FileFormats
{

   public class CsvReaderWriterTest
   {
      private CsvWriter _writer;
      private CsvReader _reader;
      private MemoryStream _ms;

      public CsvReaderWriterTest()
      {
         _ms = new MemoryStream();
         _writer = new CsvWriter(_ms, Encoding.UTF8);
         _reader = new CsvReader(_ms, Encoding.UTF8);
      }

      [Fact]
      public void Write_2RowsOfDifferentSize_Succeeds()
      {
         _writer.Write("11", "12");
         _writer.Write("21", "22", "23");
      }

      [Fact]
      public void Write_2RowsOfSameSize_Succeeds()
      {
         _writer.Write("11", "12");
         _writer.Write("21", "22");
      }

      [Fact]
      public void Write_NoEscaping_JustQuotes()
      {
         _writer.Write("1", "-=--=,,**\r\n77$$");

         string result = Encoding.UTF8.GetString(_ms.ToArray());

         Assert.Equal("1,\"-=--=,,**\r77$$\"", result);
      }

      [Fact]
      public void Write_WithEscaping_EscapingAndQuoting()
      {
         _writer.Write("1", "two of \"these\"");

         string result = Encoding.UTF8.GetString(_ms.ToArray());

         Assert.Equal("1,\"two of \"\"these\"\"\"", result);

      }

      [Fact]
      public void WriteRead_WriteTwoRows_ReadsTwoRows()
      {
         _writer.Write("r1c1", "r1c2", "r1c3");
         _writer.Write("r2c1", "r2c2");

         _ms.Flush();
         _ms.Position = 0;

         _reader = new CsvReader(_ms, Encoding.UTF8);
         string[] r1 = _reader.ReadNextRow().ToArray();
         string[] r2 = _reader.ReadNextRow().ToArray();
         var r3 = _reader.ReadNextRow();

         Assert.Null(r3);
         Assert.Equal(2, r2.Length);
         Assert.Equal(3, r1.Length);

         Assert.Equal("r2c1", r2[0]);
      }

      [Fact]
      public void WriteRead_Multiline_Succeeds()
      {
         _writer.Write(@"mu
lt", "nm");
         _writer.Write("1", "2");

         _ms.Flush();
         _ms.Position = 0;

         _reader = new CsvReader(_ms, Encoding.UTF8);


         //validate first row
         string[] r = _reader.ReadNextRow().ToArray();
         Assert.Equal(2, r.Length);
         Assert.Equal(@"mu
lt", r[0]);
         Assert.Equal("nm", r[1]);

         //validate second row
         r = _reader.ReadNextRow().ToArray();
         Assert.Equal(2, r.Length);
         Assert.Equal("1", r[0]);
         Assert.Equal("2", r[1]);

         //validate there is no more rows
         Assert.Null(_reader.ReadNextRow());
      }

      [Fact]
      public void Performance_Escaping_Stands()
      {
         const string ValueEscapeFind = "\"";
         const string ValueEscapeValue = "\"\"";

         const int loops = 10000;
         const string s = "kjkj\"jfjflj\"\"\"";
         long time1, time2;

         //experiment 1
         using (var m = new Measure())
         {
            for(int i = 0; i < loops; i++)
            {
               string s1 = s.Replace(ValueEscapeFind, ValueEscapeValue);
            }

            time1 = m.ElapsedTicks;
         }

         //experiment 2
         var rgx = new Regex("\"", RegexOptions.Compiled);
         using (var m = new Measure())
         {
            for(int i = 0; i < loops; i++)
            {
               string s1 = rgx.Replace(s, ValueEscapeValue);
            }

            time2 = m.ElapsedTicks;
         }

         //regex.replace is MUCH slower than string.replace

         Assert.NotEqual(time1, time2);
      }
   }
}
