using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ValcoSetup
{
    /// <summary>
    /// ValcoOffice-Setup.exe is setup.exe with the whole payload folder appended as a zip:
    ///   [setup.exe][payload.zip][Int64 zip length][8 bytes "VALCOPKG"]
    /// so a release is a single file. build.ps1 writes it; this reads it.
    /// </summary>
    public static class Package
    {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("VALCOPKG");

        /// <summary>Offset and length of the appended payload zip, or null when the exe has none.</summary>
        public static (long Offset, long Length)? Find(string exe)
        {
            try
            {
                using var fs = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length < 16) return null;
                var tail = new byte[16];
                fs.Seek(-16, SeekOrigin.End);
                fs.ReadExactly(tail, 0, 16);
                for (int i = 0; i < 8; i++) if (tail[8 + i] != Magic[i]) return null;
                long len = BitConverter.ToInt64(tail, 0);
                long off = fs.Length - 16 - len;
                if (len <= 0 || off <= 0) return null;
                return (off, len);
            }
            catch { return null; }
        }

        public static bool HasPayload(string exe) => Find(exe) != null;

        /// <summary>Size of the setup program itself, without any appended payload.</summary>
        public static long ExeLength(string exe) => Find(exe)?.Offset ?? new FileInfo(exe).Length;

        /// <summary>Unpacks the appended payload into <paramref name="dir"/> (reused when already unpacked).</summary>
        public static string Extract(string exe, string dir)
        {
            var p = Find(exe) ?? throw new InvalidDataException("This copy of Valco Office Setup doesn't contain the installation files. Download it again.");
            if (File.Exists(Path.Combine(dir, "valco-office.json")) && File.Exists(Path.Combine(dir, ".complete"))) return dir;
            if (Directory.Exists(dir)) { try { Directory.Delete(dir, true); } catch { } }
            Directory.CreateDirectory(dir);
            using (var fs = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var part = new SubStream(fs, p.Offset, p.Length))
            using (var zip = new ZipArchive(part, ZipArchiveMode.Read))
                zip.ExtractToDirectory(dir, true);
            File.WriteAllText(Path.Combine(dir, ".complete"), "");
            return dir;
        }

        /// <summary>Copies just the setup program (without the payload) to <paramref name="dest"/>.</summary>
        public static void CopyExe(string exe, string dest)
        {
            long n = ExeLength(exe);
            using var src = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = File.Create(dest);
            var buf = new byte[1 << 20];
            while (n > 0)
            {
                int r = src.Read(buf, 0, (int)Math.Min(buf.Length, n));
                if (r <= 0) break;
                dst.Write(buf, 0, r);
                n -= r;
            }
        }

        /// <summary>A read-only window onto part of a stream (the zip inside the exe).</summary>
        sealed class SubStream : Stream
        {
            readonly Stream _s; readonly long _start, _len; long _pos;
            public SubStream(Stream s, long start, long len) { _s = s; _start = start; _len = len; }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _len;
            public override long Position { get => _pos; set => _pos = Math.Clamp(value, 0, _len); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_pos >= _len) return 0;
                _s.Seek(_start + _pos, SeekOrigin.Begin);
                int n = _s.Read(buffer, offset, (int)Math.Min(count, _len - _pos));
                _pos += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin)
            {
                Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _pos + offset, _ => _len + offset };
                return _pos;
            }
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
