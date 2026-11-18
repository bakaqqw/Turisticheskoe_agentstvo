using System.Reflection;
using Microsoft.AspNetCore.Identity;
using TravelAgency.Models;
using TravelAgency.Services;

namespace TravelAgency.Data;

public static class DatabaseInitializer
{
    public static void Initialize(AgencyDatabase agency)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(agency.FilePath)!);
        using var db = agency.Open();
        using var schema = Assembly.GetExecutingAssembly().GetManifestResourceStream("TravelAgency.Schema.sql")!;
        using var reader = new StreamReader(schema);
        using var tx = db.BeginTransaction();
        var version = AgencyDatabase.Scalar(db, "PRAGMA user_version", tx);
        var tables = AgencyDatabase.Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'", tx);
        const string incompatible = "Структура базы несовместима или нарушены связи. Используйте сохранённую копию базы этой C#-реализации; существующие записи не изменены.";
        if (version is not (0 or 3 or 4)) throw new InvalidOperationException(incompatible);
        if (version == 0 && tables > 0) {
            // Recognize the previous C# database after the three unused tables were removed manually.
            // No rows are deleted or seeded during this upgrade.
            var expected = new Dictionary<string, string[]> {
                ["employees"] = ["id","username","full_name","password_hash","role","is_active"],
                ["clients"] = ["id","last_name","first_name","patronymic","phone","email","comments","search_name"],
                ["countries"] = ["id","name"],
                ["destinations"] = ["id","country_id","city_name"],
                ["hotels"] = ["id","destination_id","name"],
                ["tour_operators"] = ["id","name"],
                ["tours"] = ["id","title","hotel_id","operator_id","starts_on","ends_on","services","price_kopecks","is_active"],
                ["bookings"] = ["id","client_id","tour_id","manager_id","travelers_count","total_price_kopecks","status","cancel_reason","created_at"]
            };
            if (tables != expected.Count) throw new InvalidOperationException(incompatible);
            foreach (var (table, columns) in expected) {
                using var command = AgencyDatabase.Command(db, "PRAGMA table_info(" + table + ")", tx);
                using var fields = command.ExecuteReader();
                var actual = new HashSet<string>(StringComparer.Ordinal);
                while (fields.Read()) actual.Add(fields.GetString(1));
                if (table == "employees") actual.Remove("password_new");
                if (!actual.SetEquals(columns)) throw new InvalidOperationException(incompatible);
            }
            using (var command = AgencyDatabase.Command(db, "PRAGMA foreign_key_check", tx))
            using (var violations = command.ExecuteReader())
                if (violations.Read()) throw new InvalidOperationException(incompatible);
            if (AgencyDatabase.Scalar(db, "SELECT COUNT(*) FROM employees", tx) == 0)
                throw new InvalidOperationException(incompatible);
            AgencyDatabase.Execute(db, reader.ReadToEnd(), tx);
            UpgradePasswordColumn(db,tx);
            tx.Commit(); return;
        }
        AgencyDatabase.Execute(db, reader.ReadToEnd(), tx);
        if (version is 3 or 4) { UpgradePasswordColumn(db,tx); tx.Commit(); return; }
        var hasher = new PasswordHasher<Employee>();
        foreach (var item in new[] { ("admin","Ирина Лебедева","admin","TravelAdmin!2027"),("manager","Анна Морозова","manager","TravelManager!2027") })
        {
            var employee = new Employee(0,item.Item1,item.Item2,item.Item3,"",true);
            AgencyDatabase.Execute(db,"INSERT INTO employees(username,full_name,role,password_hash) VALUES($u,$n,$r,$p)",tx,
                ("$u",employee.Username),("$n",employee.FullName),("$r",employee.Role),("$p",hasher.HashPassword(employee,item.Item4)));
        }
        var countries = new[] {"Россия","Турция","Египет"};
        var cities = new[] {"Сочи","Анталья","Хургада"};
        var hotels = new[] {"Морской парк","Лазурный берег","Солнечная бухта"};
        for (var i=0;i<3;i++)
        {
            AgencyDatabase.Execute(db,"INSERT INTO countries(id,name) VALUES($id,$name)",tx,("$id",i+1),("$name",countries[i]));
            AgencyDatabase.Execute(db,"INSERT INTO destinations(id,country_id,city_name) VALUES($id,$id,$name)",tx,("$id",i+1),("$name",cities[i]));
            AgencyDatabase.Execute(db,"INSERT INTO hotels(id,destination_id,name) VALUES($id,$id,$name)",tx,("$id",i+1),("$name",hotels[i]));
        }
        AgencyDatabase.Execute(db,"INSERT INTO tour_operators(id,name) VALUES(1,'Вектор путешествий — демо')",tx);
        var offers = new[] {("Неделя у моря",1,21,13000000L),("Анталья без спешки",2,35,18000000L),
            ("Солнце Красного моря",3,50,21000000L),("Сочи — архивное предложение",1,-25,9500000L)};
        for (var i=0;i<offers.Length;i++)
        {
            var o=offers[i];
            AgencyDatabase.Execute(db,"""
                INSERT INTO tours(id,title,hotel_id,operator_id,starts_on,ends_on,services,price_kopecks,is_active)
                VALUES($id,$title,$hotel,1,$start,$end,$services,$price,$active)
                """,tx,("$id",i+1),("$title",o.Item1),("$hotel",o.Item2),
                ("$start",AppClock.Today.AddDays(o.Item3).ToString("yyyy-MM-dd")),("$end",AppClock.Today.AddDays(o.Item3+7).ToString("yyyy-MM-dd")),
                ("$services","Проживание 7 ночей, завтрак, трансфер. Учебное предложение."),("$price",o.Item4),("$active",o.Item3>0));
        }
        var names=new[] {("Миронова","Александра","Игоревна"),("Соколов","Артём","Сергеевич"),("Волкова","Мария","Алексеевна"),
            ("Кузнецов","Дмитрий","Олегович"),("Орлова","Елена","Викторовна"),("Новиков","Илья","Андреевич")};
        for (var i=0;i<names.Length;i++)
        {
            var n=names[i];
            AgencyDatabase.Execute(db,"""
                INSERT INTO clients(id,last_name,first_name,patronymic,phone,email,comments,search_name)
                VALUES($id,$last,$first,$middle,$phone,$email,'Демонстрационные данные. Все сведения вымышлены.',$search)
                """,tx,("$id",i+1),("$last",n.Item1),("$first",n.Item2),("$middle",n.Item3),("$phone",$"+7900000000{i+1}"),
                ("$email",$"client{i+1}@example.invalid"),("$search",$"{n.Item1} {n.Item2} {n.Item3}".ToUpperInvariant()));
            AgencyDatabase.Execute(db,"""
                INSERT INTO bookings(id,client_id,tour_id,manager_id,travelers_count,total_price_kopecks,status,created_at)
                VALUES($id,$client,$tour,2,1,$price,'new',$now)
                """,tx,("$id",1001+i),("$client",i+1),("$tour",i%3+1),("$price",offers[i%3].Item4),("$now",AppClock.Now));
        }
        UpgradePasswordColumn(db,tx);
        tx.Commit();
    }

    private static void UpgradePasswordColumn(Microsoft.Data.Sqlite.SqliteConnection db,
        Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        var found=false;
        using (var command=AgencyDatabase.Command(db,"PRAGMA table_info(employees)",tx))
        using (var fields=command.ExecuteReader())
            while (fields.Read()) if (fields.GetString(1)=="password_new") found=true;
        if (!found) AgencyDatabase.Execute(db,"ALTER TABLE employees ADD COLUMN password_new TEXT NOT NULL DEFAULT ''",tx);
        AgencyDatabase.Execute(db,"PRAGMA user_version = 4",tx);
    }
}
