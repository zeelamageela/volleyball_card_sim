using System.IO;
using NUnit.Framework;
using VolleyballData;

namespace VolleyballCore.Tests
{
    public class CsvUtilTests
    {
        private string _tempFile;

        [TearDown]
        public void Cleanup()
        {
            if (_tempFile != null && File.Exists(_tempFile))
            {
                File.Delete(_tempFile);
            }
        }

        private string WriteTemp(string content)
        {
            _tempFile = Path.GetTempFileName();
            File.WriteAllText(_tempFile, content);
            return _tempFile;
        }

        [Test]
        public void ParsesSimpleRows()
        {
            string path = WriteTemp("a,b,c\n1,2,3\n4,5,6\n");
            var rows = CsvUtil.ReadRows(path);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("1", rows[0]["a"]);
            Assert.AreEqual("6", rows[1]["c"]);
        }

        [Test]
        public void HandlesQuotedFieldWithEmbeddedComma()
        {
            string path = WriteTemp("name,desc\nHard,\"draws 2, keeps higher\"\n");
            var rows = CsvUtil.ReadRows(path);
            Assert.AreEqual("draws 2, keeps higher", rows[0]["desc"]);
        }

        [Test]
        public void HandlesEscapedQuoteInsideQuotedField()
        {
            string path = WriteTemp("name,desc\nX,\"say \"\"hi\"\" now\"\n");
            var rows = CsvUtil.ReadRows(path);
            Assert.AreEqual("say \"hi\" now", rows[0]["desc"]);
        }

        [Test]
        public void StripsLeadingUtf8Bom()
        {
            string path = WriteTemp("﻿a,b\n1,2\n");
            var rows = CsvUtil.ReadRows(path);
            Assert.AreEqual("1", rows[0]["a"]);
        }

        [Test]
        public void MissingTrailingColumnDefaultsToEmptyString()
        {
            string path = WriteTemp("a,b,c\n1,2\n");
            var rows = CsvUtil.ReadRows(path);
            Assert.AreEqual("", rows[0]["c"]);
        }

        [Test]
        public void GetReturnsEmptyStringForAbsentColumn()
        {
            string path = WriteTemp("a\n1\n");
            var rows = CsvUtil.ReadRows(path);
            Assert.AreEqual("", CsvUtil.Get(rows[0], "nonexistent"));
        }
    }
}
