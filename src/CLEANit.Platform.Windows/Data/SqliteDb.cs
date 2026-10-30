using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CLEANit.Platform.Windows;

/// <summary>
/// Minimal SQLite access through winsqlite3.dll, the SQLite build that ships with Windows 10/11 and Server 2016+.
/// No extra native library or package is needed.
/// </summary>
public sealed class SqliteDb : IDisposable
{
    private const int Ok = 0;
    private const int Row = 100;
    private const int Done = 101;
    private const int OpenReadOnly = 0x1;
    private const int OpenReadWrite = 0x2;
    private const int OpenCreate = 0x4;
    private static readonly IntPtr Transient = new(-1);

    private IntPtr _db;

    private SqliteDb(IntPtr db) => _db = db;

    /// <summary>Uses another SQLite library (tests on other systems).</summary>
    public static void UseLibrary(string path) =>
        NativeLibrary.SetDllImportResolver(typeof(SqliteDb).Assembly,
            (name, _, _) => name == Native.Lib ? NativeLibrary.Load(path) : IntPtr.Zero);

    public static SqliteDb Open(string path, bool readOnly = false)
    {
        if (!readOnly)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        }

        var rc = Native.sqlite3_open_v2(Utf8(path), out var db, readOnly ? OpenReadOnly : OpenReadWrite | OpenCreate, IntPtr.Zero);
        if (rc != Ok)
        {
            var message = db == IntPtr.Zero ? $"SQLite error {rc}" : Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(db));
            Native.sqlite3_close_v2(db);
            throw new InvalidOperationException(message);
        }

        Native.sqlite3_busy_timeout(db, 5000);
        return new SqliteDb(db);
    }

    /// <summary>Runs a statement (or several without parameters); returns the changed rows of the last one.</summary>
    public int Execute(string sql, params object?[] args)
    {
        if (args.Length == 0)
        {
            var rc = Native.sqlite3_exec(_db, Utf8(sql), IntPtr.Zero, IntPtr.Zero, out var error);
            if (rc != Ok)
            {
                var message = error != IntPtr.Zero ? Marshal.PtrToStringUTF8(error) : LastError();
                Native.sqlite3_free(error);
                throw new InvalidOperationException(message);
            }

            return Native.sqlite3_changes(_db);
        }

        using var statement = Prepare(sql, args);
        statement.StepToEnd();
        return Native.sqlite3_changes(_db);
    }

    public long Scalar(string sql, params object?[] args)
    {
        using var statement = Prepare(sql, args);
        return statement.Step() ? Native.sqlite3_column_int64(statement.Handle, 0) : 0;
    }

    public IEnumerable<SqliteRow> Query(string sql, params object?[] args)
    {
        using var statement = Prepare(sql, args);
        while (statement.Step())
        {
            yield return new SqliteRow(statement.Handle);
        }
    }

    public bool TableExists(string name) =>
        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", name) > 0;

    public Statement Prepare(string sql, params object?[] args)
    {
        var bytes = Utf8(sql);
        if (Native.sqlite3_prepare_v2(_db, bytes, bytes.Length, out var handle, IntPtr.Zero) != Ok)
        {
            throw new InvalidOperationException(LastError());
        }

        var statement = new Statement(this, handle);
        statement.Bind(args);
        return statement;
    }

    internal string LastError() => Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(_db)) ?? "SQLite error";

    public void Dispose()
    {
        if (_db != IntPtr.Zero)
        {
            Native.sqlite3_close_v2(_db);
            _db = IntPtr.Zero;
        }
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");

    public sealed class Statement : IDisposable
    {
        private readonly SqliteDb _owner;

        internal Statement(SqliteDb owner, IntPtr handle)
        {
            _owner = owner;
            Handle = handle;
        }

        internal IntPtr Handle { get; private set; }

        public void Bind(params object?[] args)
        {
            Native.sqlite3_reset(Handle);
            for (var i = 0; i < args.Length; i++)
            {
                var index = i + 1;
                var rc = args[i] switch
                {
                    null => Native.sqlite3_bind_null(Handle, index),
                    string s => BindText(index, s),
                    bool b => Native.sqlite3_bind_int64(Handle, index, b ? 1 : 0),
                    DateTime d => Native.sqlite3_bind_int64(Handle, index, d.ToUniversalTime().Ticks),
                    IConvertible c => Native.sqlite3_bind_int64(Handle, index, c.ToInt64(null)),
                    _ => BindText(index, args[i]!.ToString() ?? "")
                };
                if (rc != Ok)
                {
                    throw new InvalidOperationException(_owner.LastError());
                }
            }
        }

        public bool Step()
        {
            var rc = Native.sqlite3_step(Handle);
            return rc switch
            {
                Row => true,
                Done => false,
                _ => throw new InvalidOperationException(_owner.LastError())
            };
        }

        public void StepToEnd()
        {
            while (Step())
            {
            }
        }

        private int BindText(int index, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            return Native.sqlite3_bind_text(Handle, index, bytes, bytes.Length, Transient);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                Native.sqlite3_finalize(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }

    private static class Native
    {
        public const string Lib = "winsqlite3";

        [DllImport(Lib)] public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Lib)] public static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Lib)] public static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(Lib)] public static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr error);
        [DllImport(Lib)] public static extern void sqlite3_free(IntPtr ptr);
        [DllImport(Lib)] public static extern int sqlite3_changes(IntPtr db);
        [DllImport(Lib)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Lib)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
        [DllImport(Lib)] public static extern int sqlite3_step(IntPtr statement);
        [DllImport(Lib)] public static extern int sqlite3_reset(IntPtr statement);
        [DllImport(Lib)] public static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(Lib)] public static extern int sqlite3_bind_null(IntPtr statement, int index);
        [DllImport(Lib)] public static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
        [DllImport(Lib)] public static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int bytes, IntPtr destructor);
        [DllImport(Lib)] public static extern long sqlite3_column_int64(IntPtr statement, int column);
        [DllImport(Lib)] public static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
        [DllImport(Lib)] public static extern int sqlite3_column_type(IntPtr statement, int column);
    }

    /// <summary>Current row of a query (valid until the next step).</summary>
    public readonly struct SqliteRow(IntPtr statement)
    {
        public long Int(int column) => Native.sqlite3_column_int64(statement, column);

        public string? Text(int column) =>
            Native.sqlite3_column_type(statement, column) == 5 ? null : Marshal.PtrToStringUTF8(Native.sqlite3_column_text(statement, column));
    }
}
