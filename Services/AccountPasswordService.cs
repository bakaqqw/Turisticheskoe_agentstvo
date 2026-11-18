using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using TravelAgency.Data;
using TravelAgency.Models;

namespace TravelAgency.Services;

public static class AccountPasswordService
{
    public const string FingerprintClaim = "password_version";
    public static string Fingerprint(string hash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash)));

    public static void ChangePassword(AgencyDatabase database, string username, string password)
    {
        if (!IsValid(password))
            throw new ArgumentException("Пароль должен содержать от 8 до 128 символов и не состоять только из пробелов.");
        var employee = database.FindEmployee(username: username)
            ?? throw new ArgumentException($"Сотрудник с логином {username} не найден.");
        var hash = new PasswordHasher<Employee>().HashPassword(employee, password);
        using var db = database.Open();
        if (AgencyDatabase.Execute(db, "UPDATE employees SET password_hash=$hash,password_new='' WHERE id=$id", null,
            ("$hash", hash), ("$id", employee.Id)) != 1)
            throw new InvalidOperationException("Не удалось изменить пароль сотрудника.");
    }

    private static bool IsValid(string password) => password.Length is >= 8 and <= 128 && !string.IsNullOrWhiteSpace(password);

    public static void ApplyPendingPasswords(AgencyDatabase database)
    {
        using var db=database.Open();
        using var tx=db.BeginTransaction();
        var pending=new List<(Employee Employee,string Password)>();
        using (var command=AgencyDatabase.Command(db,"SELECT id,username,full_name,role,password_hash,is_active,password_new FROM employees WHERE password_new IS NOT NULL AND password_new<>''",tx))
        using (var rows=command.ExecuteReader())
            while (rows.Read()) pending.Add((new Employee(rows.GetInt64(0),rows.GetString(1),rows.GetString(2),rows.GetString(3),rows.GetString(4),rows.GetBoolean(5)),rows.GetString(6)));
        var hasher=new PasswordHasher<Employee>();
        foreach (var (employee,password) in pending) {
            if (!IsValid(password)) {
                Console.WriteLine($"Пароль {employee.Username} не изменён: в password_new нужно от 8 до 128 символов, кроме строки из пробелов. Исправьте или очистите поле и перезапустите сайт.");
                continue;
            }
            AgencyDatabase.Execute(db,"UPDATE employees SET password_hash=$hash,password_new='' WHERE id=$id",tx,
                ("$hash",hasher.HashPassword(employee,password)),("$id",employee.Id));
        }
        tx.Commit();
    }
}
