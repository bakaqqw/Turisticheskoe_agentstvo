using Microsoft.Data.Sqlite;
using TravelAgency.Models;
using TravelAgency.Services;

namespace TravelAgency.Data;

public sealed class AgencyDatabase
{
    public string FilePath { get; }
    public AgencyDatabase(string filePath) => FilePath = filePath;
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = FilePath, ForeignKeys = true, DefaultTimeout = 20 }.ToString());
        connection.Open();
        return connection;
    }
    public static SqliteCommand Command(SqliteConnection db, string sql, SqliteTransaction? tx = null,
        params (string Name, object? Value)[] values)
    {
        var command = db.CreateCommand(); command.CommandText = sql; command.Transaction = tx;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    public static long Scalar(SqliteConnection db, string sql, SqliteTransaction? tx = null,
        params (string Name, object? Value)[] values)
    {
        using var command = Command(db, sql, tx, values);
        return Convert.ToInt64(command.ExecuteScalar());
    }
    public static int Execute(SqliteConnection db, string sql, SqliteTransaction? tx = null,
        params (string Name, object? Value)[] values)
    {
        using var command = Command(db, sql, tx, values); return command.ExecuteNonQuery();
    }
    public static List<T> Read<T>(SqliteConnection db, string sql, Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] values)
    {
        using var command = Command(db, sql, null, values); using var reader = command.ExecuteReader();
        var result = new List<T>(); while (reader.Read()) result.Add(map(reader)); return result;
    }
    public Employee? FindEmployee(string? username = null, long? id = null)
    {
        using var db = Open();
        return Read(db, "SELECT id,username,full_name,role,password_hash,is_active FROM employees WHERE " +
            (id.HasValue ? "id=$value" : "username=$value"),
            r => new Employee(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetBoolean(5)),
            ("$value", id.HasValue ? id.Value : username)).SingleOrDefault();
    }
    public List<Client> Clients(string? query = null, long? id = null)
    {
        using var db = Open();
        var q = (query ?? "").Trim();
        var digits = System.Text.RegularExpressions.Regex.IsMatch(q, @"^[0-9+()\s-]+$")
            ? InputValidation.Digits(q) : "";
        if (digits.Length == 11 && digits.StartsWith('8')) digits = "7" + digits[1..];
        return Read(db, """
            SELECT id,last_name,first_name,patronymic,phone,email,comments FROM clients
            WHERE ($id IS NULL OR id=$id) AND
              ($q='' OR instr(search_name,$q)>0 OR ($digits<>'' AND instr(phone,$digits)>0))
            ORDER BY last_name,first_name,id
            """, r => new Client(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6)),
            ("$id", id), ("$q", q.ToUpperInvariant()), ("$digits", digits));
    }
    public long SaveClient(Client input, long? id)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        var values = new (string, object?)[] { ("$last",input.LastName), ("$first",input.FirstName), ("$middle",input.Patronymic),
            ("$phone",input.Phone), ("$email",input.Email), ("$comments",input.Comments),
            ("$search",$"{input.LastName} {input.FirstName} {input.Patronymic}".ToUpperInvariant()), ("$id",id) };
        var sql = id.HasValue ? """
            UPDATE clients SET last_name=$last,first_name=$first,patronymic=$middle,phone=$phone,
                email=$email,comments=$comments,search_name=$search WHERE id=$id
            """ : """
            INSERT INTO clients(last_name,first_name,patronymic,phone,email,comments,search_name)
                VALUES($last,$first,$middle,$phone,$email,$comments,$search)
            """;
        if (Execute(db, sql, tx, values) == 0) return 0;
        var saved = id ?? Scalar(db, "SELECT last_insert_rowid()", tx); tx.Commit(); return saved;
    }
    private const string TourSelect = """
        SELECT t.id,t.title,t.hotel_id,t.operator_id,c.name,d.city_name,h.name,o.name,
            t.starts_on,t.ends_on,t.services,t.price_kopecks,t.is_active
        FROM tours t JOIN hotels h ON h.id=t.hotel_id JOIN destinations d ON d.id=h.destination_id
        JOIN countries c ON c.id=d.country_id JOIN tour_operators o ON o.id=t.operator_id
        """;
    public List<Tour> Tours(long? id = null, bool activeOnly = false)
    {
        using var db = Open();
        return Read(db, TourSelect + " WHERE ($id IS NULL OR t.id=$id) AND ($active=0 OR (t.is_active=1 AND t.starts_on>=$today)) ORDER BY t.starts_on,t.id",
            r => new Tour(r.GetInt64(0),r.GetString(1),r.GetInt64(2),r.GetInt64(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),
                r.GetString(8),r.GetString(9),r.GetString(10),r.GetInt64(11),r.GetBoolean(12)),
            ("$id",id),("$active",activeOnly),("$today",AppClock.Today.ToString("yyyy-MM-dd")));
    }
    public const string BookingSelect = """
        SELECT b.id,b.client_id,b.tour_id,b.manager_id,
            trim(c.last_name||' '||c.first_name||' '||c.patronymic),c.phone,t.title,h.name,
            co.name||', '||d.city_name,t.starts_on,t.ends_on,e.full_name,
            b.travelers_count,b.total_price_kopecks,b.status,b.created_at
        FROM bookings b JOIN clients c ON c.id=b.client_id JOIN tours t ON t.id=b.tour_id
        JOIN hotels h ON h.id=t.hotel_id JOIN destinations d ON d.id=h.destination_id
        JOIN countries co ON co.id=d.country_id JOIN employees e ON e.id=b.manager_id
        """;
    public List<Booking> Bookings(long? id = null, long? clientId = null)
    {
        using var db = Open();
        return Read(db, BookingSelect + " WHERE ($id IS NULL OR b.id=$id) AND ($client IS NULL OR b.client_id=$client) ORDER BY b.created_at DESC,b.id DESC",
            r => new Booking(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.GetInt64(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),
                r.GetString(8),r.GetString(9),r.GetString(10),r.GetString(11),r.GetInt32(12),r.GetInt64(13),r.GetString(14),r.GetString(15)),
            ("$id",id),("$client",clientId));
    }
    public object Lookups()
    {
        using var db = Open();
        return new {
            hotels = Read(db, "SELECT h.id,h.name||' — '||c.name||', '||d.city_name FROM hotels h JOIN destinations d ON d.id=h.destination_id JOIN countries c ON c.id=d.country_id ORDER BY h.id",
                r => new {id=r.GetInt64(0),name=r.GetString(1)}),
            operators = Read(db,"SELECT id,name FROM tour_operators ORDER BY name",r=>new {id=r.GetInt64(0),name=r.GetString(1)}),
            managers = Read(db,"SELECT id,full_name FROM employees WHERE is_active=1 ORDER BY full_name",r=>new {id=r.GetInt64(0),name=r.GetString(1)})
        };
    }
}
